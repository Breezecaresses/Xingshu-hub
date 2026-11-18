using System;
using System.Linq;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

// Only the three local decision timers are redirected. The AOT timer library,
// network deadlines, replay timers and other players' timers are untouched.
class ThinkingClockPatcher
{
    static void Main(string[] args)
    {
        try { Patch(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message); Console.Error.WriteLine(ex.StackTrace); Environment.Exit(1); }
    }

    static void Patch(string[] args)
    {
        var resolver = new DefaultAssemblyResolver();
        foreach (string dir in new[] { "updated_20261019_verified", "unity", "framework", "thirdparty" })
            resolver.AddSearchDirectory(Path.Combine(args[3], "XingShu-Dev", "references", dir));
        var reader = new ReaderParameters { AssemblyResolver = resolver };
        using (var game = AssemblyDefinition.ReadAssembly(args[0], reader))
        using (var hooks = AssemblyDefinition.ReadAssembly(args[1], reader))
        {
            var module = game.MainModule;
            var clock = hooks.MainModule.Types.Single(t => t.FullName == "XingShuRuntime.Utilities.ThinkingClock");
            var enabled = hooks.MainModule.Types.Single(t => t.FullName == "XingShuRuntime.Utilities.ThinkingTime")
                .Methods.Single(m => m.Name == "UseRealClock");
            var ctor = clock.Methods.Single(m => m.IsConstructor && m.IsPublic);
            var attach = clock.Methods.Single(m => m.Name == "AttachStop");
            var cancelField = clock.Fields.Single(f => f.Name == "Cancelled");
            var tickField = clock.Fields.Single(f => f.Name == "Tick");
            var owner = module.Types.FirstOrDefault(t => t.Name == "XingShuDecisionTimer");
            if (owner == null) { owner = new TypeDefinition("", "XingShuDecisionTimer", TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object); module.Types.Add(owner); }
            // Resource 150 moved hero selection from Refresh into RefreshChoiceHero.
            // Require exactly one real registration; never patch both or silently skip it.
            var heroPanel = module.Types.Single(t => t.FullName == "UI.RoomHeroPanel");
            var heroTimers = heroPanel.Methods.Where(m => m.HasBody &&
                (m.Name == "Refresh" || m.Name == "RefreshChoiceHero") &&
                m.Body.Instructions.Any(i => IsTimerRegistration(i, owner.Name))).ToArray();
            if (heroTimers.Length != 1)
                throw new Exception("Expected one hero-selection timer in Refresh or RefreshChoiceHero, found " + heroTimers.Length);
            var heroTimer = heroTimers[0];
            int count = 0;
            foreach (var type in module.Types.ToArray())
            foreach (var method in type.Methods.ToArray())
            {
                bool operation = type.FullName == "GameLogic.OperationTimer" && method.Name == "ActionDownTime";
                bool selection = ReferenceEquals(method, heroTimer) || (type.FullName == "UI.RoomHeroPanel" && method.Name == "RefreshSelectSkin");
                if ((!operation && !selection) || !method.HasBody) continue;
                if (method.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.Name == owner.Name)) { count++; continue; }
                var registrations = method.Body.Instructions.Where(i => IsTimerRegistration(i, owner.Name)).ToArray();
                if (registrations.Length != 1)
                    throw new Exception("Expected one decision timer in " + method.FullName + ", found " + registrations.Length);
                var call = registrations[0];
                var register = (MethodReference)call.Operand;
                if (register.Parameters.Count != 13 || register.Parameters[2].ParameterType.FullName != "System.Action" || register.Parameters[4].ParameterType.FullName != "System.Action" || register.Parameters[7].ParameterType.FullName != "System.Action`1<System.Single>") throw new Exception("Unexpected decision timer signature");
                var wrapper = new MethodDefinition("Register" + count, MethodAttributes.Static | MethodAttributes.Assembly, module.ImportReference(register.ReturnType));
                foreach (var p in register.Parameters) wrapper.Parameters.Add(new ParameterDefinition(p.Name, ParameterAttributes.None, module.ImportReference(p.ParameterType)));
                owner.Methods.Add(wrapper);
                wrapper.Body.InitLocals = true;
                var state = new VariableDefinition(module.ImportReference(clock)); wrapper.Body.Variables.Add(state);
                var timer = new VariableDefinition(module.ImportReference(register.ReturnType)); wrapper.Body.Variables.Add(timer);
                var il = wrapper.Body.GetILProcessor();
                var original = il.Create(OpCodes.Nop);
                il.Emit(OpCodes.Call, module.ImportReference(enabled)); il.Emit(OpCodes.Brfalse, original);
                foreach (int i in new [] { 0, 1, 2, 4, 7 }) il.Emit(OpCodes.Ldarg, wrapper.Parameters[i]);
                il.Emit(OpCodes.Newobj, module.ImportReference(ctor)); il.Emit(OpCodes.Stloc, state);
                for (int i = 0; i < 13; i++)
                {
                    if (i == 1) il.Emit(OpCodes.Ldc_R4, float.MaxValue);
                    else if (i == 2) il.Emit(OpCodes.Ldnull);
                    else if (i == 4 || i == 7) { il.Emit(OpCodes.Ldloc, state); il.Emit(OpCodes.Ldfld, module.ImportReference(i == 4 ? cancelField : tickField)); }
                    else il.Emit(OpCodes.Ldarg, wrapper.Parameters[i]);
                }
                il.Emit(OpCodes.Call, register); il.Emit(OpCodes.Stloc, timer);
                var cancel = new MethodReference("Cancel", module.TypeSystem.Void, module.ImportReference(register.ReturnType)) { HasThis = true };
                var actionCtor = new MethodReference(".ctor", module.TypeSystem.Void, module.ImportReference(register.Parameters[2].ParameterType)) { HasThis = true };
                actionCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object)); actionCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
                il.Emit(OpCodes.Ldloc, state); il.Emit(OpCodes.Ldloc, timer); il.Emit(OpCodes.Ldftn, cancel); il.Emit(OpCodes.Newobj, actionCtor); il.Emit(OpCodes.Callvirt, module.ImportReference(attach));
                il.Emit(OpCodes.Ldloc, timer); il.Emit(OpCodes.Ret);
                il.Append(original);
                foreach (var p in wrapper.Parameters) il.Emit(OpCodes.Ldarg, p);
                il.Emit(OpCodes.Call, register); il.Emit(OpCodes.Ret);
                call.Operand = wrapper; count++;
            }
            if (count != 3) throw new Exception("Expected three local decision timer registrations, found " + count);
            var stats = hooks.MainModule.Types.Single(t => t.FullName == "XingShuRuntime.Utilities.BattleStatsOverlay");
            PatchObserver(module, stats, "Core.Unit.BattleProperty", "OnGoldChanged", "ObserveGold", false);
            PatchObserver(module, stats, "GameLogic.BattleResultLogic", "OnGameFinishS2CServerCallBack", "ObserveSettlement", true);
            PatchObserver(module, stats, "GameLogic.BattleLogic", "OnGetHeroInfoS2CServerCallBackAsync", "ObserveHeroInfo", true);
            PatchObserver(module, stats, "GameLogic.ActionLogic", "OnUpdateHeroAttrS2CServerCallBack", "ObserveHealingSources", true);
            var cooldown = hooks.MainModule.Types.Single(t => t.FullName == "XingShuRuntime.Utilities.MonsterCooldownRuntime");
            PatchObserver(module, cooldown, "GameLogic.BattleLogic", "OnGetHeroInfoS2CServerCallBackAsync", "ObserveHeroInfo", true);
            PatchObserver(module, cooldown, "GameLogic.ActionLogic", "OnUpdateHeroAttrS2CServerCallBack", "ObserveAttributes", true);
            PatchObserver(module, cooldown, "GameLogic.CardLogic", "OnUseEffectCardS2CServerCallBack", "ObserveUse", true);
            PatchObserver(module, cooldown, "GameLogic.BattleLogic", "OnRoundStartS2CServerCallBack", "ObserveRound", true);
            PatchObserver(module, cooldown, "GameLogic.BattleLogic", "OnActionStartNotifyS2CServerCallBack", "ObserveAction", true);
            var library = hooks.MainModule.Types.Single(t => t.FullName == "XingShu.Runtime.GameLibraryReplies");
            PatchObserver(module, library, "GameLogic.AccountLogic", "OnGetShowPlayerS2CServerCallBack", "ObserveCareer", true);
            PatchObserver(module, library, "GameLogic.AccountLogic", "OnGetPlayerFightRecordS2CServerCallBack", "ObserveDetail", true);
            PatchProgress(module, enabled.DeclaringType);
            PatchPingTimers(module, hooks.MainModule);
            PatchMarkEffects(module, hooks.MainModule);
            PatchCameraJumps(module, hooks.MainModule);
            game.Write(args[2]);
            Console.WriteLine("Real-clock decision timers: " + count);
        }
    }

    static void PatchPingTimers(ModuleDefinition module, ModuleDefinition hooksModule)
    {
        var pingTiming = hooksModule.Types.Single(t => t.FullName == "XingShuRuntime.Utilities.PingDisplayTiming");
        var ensureInit = module.ImportReference(pingTiming.Methods.Single(m => m.Name == "EnsureInitialized"));
        var pingState = hooksModule.Types.Single(t => t.FullName == "XingShuRuntime.Utilities.PingClockState");
        var ctor = pingState.Methods.Single(m => m.IsConstructor && m.IsPublic);
        var attach = pingState.Methods.Single(m => m.Name == "AttachStop");
        var cancelField = pingState.Fields.Single(f => f.Name == "Cancelled");
        var tickField = pingState.Fields.Single(f => f.Name == "Tick");
        var owner = module.Types.FirstOrDefault(t => t.Name == "XingShuPingTimer");
        if (owner == null)
        {
            owner = new TypeDefinition("", "XingShuPingTimer", TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
            module.Types.Add(owner);
        }
        int pingCount = 0;
        foreach (string typeName in new[] { "UI.UIExpression_Com_ChatItem", "UI.UIExpression_Com_ExpressionItem" })
        {
            var type = module.Types.Single(t => t.FullName == typeName);
            var method = type.Methods.Single(m => m.Name == "RefreshInfo" && m.HasBody);
            if (method.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).DeclaringType.Name == owner.Name))
            {
                pingCount++;
                continue;
            }
            var registrations = method.Body.Instructions.Where(i => IsTimerRegistration(i, owner.Name)).ToArray();
            if (registrations.Length != 1)
                throw new Exception("Expected one ping timer in " + method.FullName + ", found " + registrations.Length);
            var call = registrations[0];
            var register = (MethodReference)call.Operand;
            if (register.Parameters.Count != 13 || register.Parameters[2].ParameterType.FullName != "System.Action" ||
                register.Parameters[4].ParameterType.FullName != "System.Action" || register.Parameters[7].ParameterType.FullName != "System.Action`1<System.Single>")
                throw new Exception("Unexpected ping timer signature in " + method.FullName);
            var wrapper = new MethodDefinition("Register" + pingCount, MethodAttributes.Static | MethodAttributes.Assembly, module.ImportReference(register.ReturnType));
            foreach (var p in register.Parameters) wrapper.Parameters.Add(new ParameterDefinition(p.Name, ParameterAttributes.None, module.ImportReference(p.ParameterType)));
            owner.Methods.Add(wrapper);
            wrapper.Body.InitLocals = true;
            var state = new VariableDefinition(module.ImportReference(pingState));
            wrapper.Body.Variables.Add(state);
            var timer = new VariableDefinition(module.ImportReference(register.ReturnType));
            wrapper.Body.Variables.Add(timer);
            var il = wrapper.Body.GetILProcessor();
            il.Emit(OpCodes.Call, ensureInit);
            foreach (int i in new[] { 0, 1, 2, 4, 7 }) il.Emit(OpCodes.Ldarg, wrapper.Parameters[i]);
            il.Emit(OpCodes.Newobj, module.ImportReference(ctor));
            il.Emit(OpCodes.Stloc, state);
            for (int i = 0; i < 13; i++)
            {
                if (i == 1) il.Emit(OpCodes.Ldc_R4, float.MaxValue);
                else if (i == 2) il.Emit(OpCodes.Ldnull);
                else if (i == 4 || i == 7)
                {
                    il.Emit(OpCodes.Ldloc, state);
                    il.Emit(OpCodes.Ldfld, module.ImportReference(i == 4 ? cancelField : tickField));
                }
                else il.Emit(OpCodes.Ldarg, wrapper.Parameters[i]);
            }
            il.Emit(OpCodes.Call, register);
            il.Emit(OpCodes.Stloc, timer);
            var cancel = new MethodReference("Cancel", module.TypeSystem.Void, module.ImportReference(register.ReturnType)) { HasThis = true };
            var actionCtor = new MethodReference(".ctor", module.TypeSystem.Void, module.ImportReference(register.Parameters[2].ParameterType)) { HasThis = true };
            actionCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            actionCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
            il.Emit(OpCodes.Ldloc, state);
            il.Emit(OpCodes.Ldloc, timer);
            il.Emit(OpCodes.Ldftn, cancel);
            il.Emit(OpCodes.Newobj, actionCtor);
            il.Emit(OpCodes.Callvirt, module.ImportReference(attach));
            il.Emit(OpCodes.Ldloc, timer);
            il.Emit(OpCodes.Ret);
            call.Operand = wrapper;
            pingCount++;
        }
        if (pingCount != 2) throw new Exception("Expected two right-side ping timers, found " + pingCount);
    }

    static void PatchMarkEffects(ModuleDefinition module, ModuleDefinition hooksModule)
    {
        var pingTiming = hooksModule.Types.Single(t => t.FullName == "XingShuRuntime.Utilities.PingDisplayTiming");
        var resolve = module.ImportReference(pingTiming.Methods.Single(m => m.Name == "ResolveEffectDuration"));
        var effectType = module.Types.Single(t => t.FullName == "Core.Effect");
        var keyField = module.ImportReference(effectType.Fields.Single(f => f.Name == "effectPoolKey"));

        var gameEffect = module.Types.Single(t => t.FullName == "Core.GameEffect");
        var play = gameEffect.Methods.Single(m => m.Name == "Play" && m.HasBody);
        if (!Calls(play, "PingDisplayTiming::ResolveEffectDuration"))
        {
            var anchor = play.Body.Instructions.Single(i => i.OpCode.Code == Code.Ldfld && ((FieldReference)i.Operand).FullName.EndsWith("Core.GameEffect::duration"));
            InsertDurationScaleAfter(play, anchor, keyField, resolve);
        }
        var playTimeline = gameEffect.Methods.Single(m => m.Name == "PlayTimeline" && m.HasBody);
        if (!Calls(playTimeline, "PingDisplayTiming::ResolveEffectDuration"))
        {
            var anchor = playTimeline.Body.Instructions.Single(i => i.OpCode.Code == Code.Conv_R4 &&
                i.Previous != null && i.Previous.Operand is MethodReference && ((MethodReference)i.Previous.Operand).Name == "get_duration");
            InsertDurationScaleAfter(playTimeline, anchor, keyField, resolve);
        }
        var cycleEffect = module.Types.Single(t => t.FullName == "Core.GameCycleEffect");
        var playLoop = cycleEffect.Methods.Single(m => m.Name == "PlayLoopEffect" && m.HasBody);
        if (!Calls(playLoop, "PingDisplayTiming::ResolveEffectDuration"))
        {
            var anchor = playLoop.Body.Instructions.Single(i => i.OpCode.Code == Code.Div &&
                i.Previous != null && i.Previous.Operand is MethodReference && ((MethodReference)i.Previous.Operand).Name == "get_effectSpeed");
            InsertDurationScaleAfter(playLoop, anchor, keyField, resolve);
        }
    }

    static void InsertDurationScaleAfter(MethodDefinition method, Instruction anchor, FieldReference keyField, MethodReference resolve)
    {
        method.Body.SimplifyMacros();
        var il = method.Body.GetILProcessor();
        var ldThis = il.Create(OpCodes.Ldarg_0);
        var ldKey = il.Create(OpCodes.Ldfld, keyField);
        var callResolve = il.Create(OpCodes.Call, resolve);
        il.InsertAfter(anchor, callResolve);
        il.InsertAfter(anchor, ldKey);
        il.InsertAfter(anchor, ldThis);
        method.Body.OptimizeMacros();
    }

    static void PatchCameraJumps(ModuleDefinition module, ModuleDefinition hooksModule)
    {
        var mapGui = hooksModule.Types.Single(t => t.FullName == "XingShuRuntime.Utilities.MapCameraGUI");
        var jump = module.ImportReference(mapGui.Methods.Single(m => m.Name == "JumpToFreeCameraTarget"));
        foreach (var target in new[] {
            new[] { "GameLogic.BattleLandMessage", "ShowAddressInfo" },
            new[] { "GameLogic.BattlePlayerMarkMessage", "ShowAddressInfo" },
            new[] { "UI.BattleInfoPanel", "OnClickMiniMap" }
        })
        {
            var type = module.Types.Single(t => t.FullName == target[0]);
            var method = type.Methods.Single(m => m.Name == target[1] && m.HasBody);
            if (Calls(method, "MapCameraGUI::JumpToFreeCameraTarget")) continue;
            var calls = method.Body.Instructions.Where(i =>
                i.Operand is MethodReference && ((MethodReference)i.Operand).FullName.Contains("CameraManager::ControlFreeCamera(")).ToArray();
            if (calls.Length != 1)
                throw new Exception("Expected one ControlFreeCamera jump in " + method.FullName + ", found " + calls.Length);
            calls[0].OpCode = OpCodes.Call;
            calls[0].Operand = jump;
        }
    }

    static bool IsTimerRegistration(Instruction instruction, string wrapperName)
    {
        var method = instruction.Operand as MethodReference;
        return method != null && ((method.DeclaringType.FullName == "UnityTimer.Timer" && method.Name == "Register") ||
            (method.DeclaringType.Name == wrapperName && method.Name.StartsWith("Register", StringComparison.Ordinal)));
    }

    static void PatchProgress(ModuleDefinition module, TypeDefinition hooks)
    {
        var operation = module.Types.Single(t => t.FullName == "GameLogic.OperationTimer");
        var start = operation.Methods.Single(m => m.Name == "ActionDownTime");
        var closure = operation.NestedTypes.Single(t => t.Methods.Any(m => m.Name == "<ActionDownTime>b__2"));
        var begin = module.ImportReference(hooks.Methods.Single(m => m.Name == "BeginProgress"));
        if (!Calls(start, "ThinkingTime::BeginProgress"))
        {
            start.Body.SimplifyMacros();
            var anchor = start.Body.Instructions.Single(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "TryShowTimerToPlayer");
            for (int i = 0; i < 4; i++) anchor = anchor.Previous;
            if (anchor.OpCode.Code != Code.Ldloc) throw new Exception("Unexpected local progress registration");
            var il = start.Body.GetILProcessor();
            var first = il.Create(OpCodes.Ldloc, start.Body.Variables.Single(v => v.VariableType.FullName == closure.FullName));
            il.InsertBefore(anchor, first);
            il.InsertBefore(anchor, il.Create(OpCodes.Ldsfld, operation.Fields.Single(f => f.Name == "operationTime")));
            il.InsertBefore(anchor, il.Create(OpCodes.Call, begin));
            foreach (var instruction in start.Body.Instructions)
                if (ReferenceEquals(instruction.Operand, anchor)) instruction.Operand = first;
            start.Body.OptimizeMacros();
        }
        foreach (var method in closure.Methods.Where(m => m.HasBody && m.Name.StartsWith("<ActionDownTime>b__")))
        {
            if (Calls(method, "ThinkingTime::UpdateProgress")) continue;
            var call = method.Body.Instructions.Single(i => i.Operand is MethodReference && ((MethodReference)i.Operand).Name == "UpdateOperationProgress");
            method.Body.SimplifyMacros();
            var il = method.Body.GetILProcessor(); var owner = il.Create(OpCodes.Ldarg_0); il.InsertBefore(call, owner);
            foreach (var instruction in method.Body.Instructions)
                if (ReferenceEquals(instruction.Operand, call)) instruction.Operand = owner;
            call.OpCode = OpCodes.Call; call.Operand = module.ImportReference(hooks.Methods.Single(m => m.Name == "UpdateProgress"));
            method.Body.OptimizeMacros();
        }
        var remote = module.Types.Single(t => t.FullName == "UI.OperateTimeWindow").Methods.Single(m => m.Name == "UpdateShowProgress");
        if (!Calls(remote, "ThinkingTime::AllowRemoteProgress"))
        {
            remote.Body.SimplifyMacros(); var il = remote.Body.GetILProcessor(); var first = remote.Body.Instructions[0];
            il.InsertBefore(first, il.Create(OpCodes.Call, module.ImportReference(hooks.Methods.Single(m => m.Name == "AllowRemoteProgress"))));
            il.InsertBefore(first, il.Create(OpCodes.Brtrue, first)); il.InsertBefore(first, il.Create(OpCodes.Ret));
            remote.Body.OptimizeMacros();
        }
        var dispose = operation.Methods.Single(m => m.Name == "Dispose");
        if (!Calls(dispose, "ThinkingTime::ClearProgress"))
            dispose.Body.GetILProcessor().InsertBefore(dispose.Body.Instructions[0], Instruction.Create(OpCodes.Call, module.ImportReference(hooks.Methods.Single(m => m.Name == "ClearProgress"))));
    }

    static bool Calls(MethodDefinition method, string name)
    {
        return method.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).FullName.Contains(name));
    }

    static void PatchObserver(ModuleDefinition module, TypeDefinition hooks, string owner, string methodName, string callback, bool checkError)
    {
        var method = module.Types.Single(t => t.FullName == owner).Methods.Single(m => m.Name == methodName);
        if (method.Body.Instructions.Any(i => i.Operand is MethodReference && ((MethodReference)i.Operand).FullName.Contains(hooks.FullName + "::" + callback))) return;
        var hook = module.ImportReference(hooks.Methods.Single(m => m.Name == callback));
        if (hook.ReturnType.FullName != "System.Void" || hook.Parameters.Count != 1 || method.Parameters.Count == 0 ||
            hook.Parameters[0].ParameterType.FullName != method.Parameters[0].ParameterType.FullName ||
            (checkError && (method.Parameters.Count < 2 || method.Parameters[1].ParameterType.FullName != "System.Int32")))
            throw new Exception("Unexpected observer signature: " + owner + "::" + methodName);
        var il = method.Body.GetILProcessor(); var first = method.Body.Instructions[0];
        if (checkError) { il.InsertBefore(first, il.Create(OpCodes.Ldarg, method.Parameters[1])); il.InsertBefore(first, il.Create(OpCodes.Brtrue, first)); }
        il.InsertBefore(first, il.Create(OpCodes.Ldarg, method.Parameters[0]));
        il.InsertBefore(first, il.Create(OpCodes.Call, hook));
    }
}
