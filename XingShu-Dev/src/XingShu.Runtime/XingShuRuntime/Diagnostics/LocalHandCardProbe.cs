using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Core;
using Core.Net;
using Core.Scene;
using Cysharp.Threading.Tasks;
using GameLogic;
using party.model;
using party.protocol;
using Tools;
using UnityEngine;

namespace XingShuRuntime.Diagnostics
{
    /// <summary>
    /// XingShu 本地手牌对象生命周期与只读探针 (Track A - Phase 2D-6F)
    /// 纯只读数据采集器：对 Self/HumanRemote/Bot 执行运行时手牌抽样。
    /// 截获底层发牌与出牌 RPC 原始参数以探测是否存在任何短暂 pre-mask real CardId。
    /// 严格遵循 RPC 包装安全：保存原始委托、防重复/防嵌套装配、严格单次调用原委托、安全卸载恢复。
    /// 每次采样 Fresh Reacquire 对象引用，通过 RuntimeHelpers.GetHashCode 比较引用身份，
    /// 侦测 CardContainer 与 _HandCards List 是否在生命周期中发生替换。
    /// 只读监听 CardLogic 核心卡牌信号以捕获卡牌事件触发链。
    /// 维护纯诊断用 Initial - Play 重构状态机，严禁将重构数据反哺给正式 UI。
    /// 0 外部网络、0 HTTP、0 WebSocket、绝对不修改 HandCardDisplayManager 与 Bot Hand 0C。
    /// </summary>
#if XINGSHU_DIAGNOSTIC_PROBE || STANDALONE_PROBE
    [DisallowMultipleComponent]
    public sealed class LocalHandCardProbe : MonoBehaviour
    {
        private struct CachedObjectIdentity
        {
            public int BpdRef;
            public int ContainerRef;
            public int HandListRef;
            public int CardCount;
            public int ListCount;
            public string Cards;
        }

        private static LocalHandCardProbe _instance;

        private static RoomHeroCardChangeS2CRPC.OnRoomHeroCardChangeS2CServerDelegate _originalCardChangeCallback;
        private static BattleUseCardS2CRPC.OnBattleUseCardS2CServerDelegate _originalUseCardCallback;
        private static bool _rpcHooksRegistered;
        private static int _installCount;
        private static int _restoreCount;
        private static bool _nestedDetected;

        private float _lastSampleTime;
        private bool _humanRemoteObserved;
        private bool _signalsSubscribed;
        private long _lastCurPlayerId = -1;

        private readonly Dictionary<long, int> _trackedCardCounts = new Dictionary<long, int>();
        private readonly HashSet<long> _initialDealReported = new HashSet<long>();
        private readonly HashSet<string> _dedupCache = new HashSet<string>();
        private readonly Dictionary<long, CachedObjectIdentity> _lastObjectIdentities = new Dictionary<long, CachedObjectIdentity>();

        // 纯诊断用手牌重构追踪 (DIAGNOSTIC ONLY，绝不传递给正式 UI，绝对不猜牌)
        private readonly Dictionary<long, HashSet<int>> _initialKnownSets = new Dictionary<long, HashSet<int>>();
        private readonly Dictionary<long, List<int>> _observedPlayedCards = new Dictionary<long, List<int>>();
        private readonly Dictionary<long, int> _unknownDrawCounts = new Dictionary<long, int>();

        public static LocalHandCardProbe Instance => _instance;
        public static bool IsRpcHooksRegistered => _rpcHooksRegistered;
        public static int InstallCount => _installCount;
        public static int RestoreCount => _restoreCount;
        public static bool NestedDetected => _nestedDetected;

        private void Awake()
        {
            _instance = this;
        }

        private void Start()
        {
            if (DiagnosticProbeHost.IsEnabled)
            {
                EnsureRPCHooks();
                EnsureSignalSubscriptions();
            }
        }

        private void OnDestroy()
        {
            RestoreSignalSubscriptions();
            RestoreRPCHooks();
            CleanupSession();
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            if (!DiagnosticProbeHost.IsEnabled)
            {
                if (_trackedCardCounts.Count > 0)
                {
                    CleanupSession();
                }
                return;
            }

            // 严格环境守卫：仅对局中运行，大厅/登录/商城不采样
            if (SimpleSingletonProvider<SceneManager>.inst == null ||
                SimpleSingletonProvider<SceneManager>.inst.currentType.Value != SceneType.Battle)
            {
                if (_trackedCardCounts.Count > 0)
                {
                    CleanupSession();
                }
                return;
            }

            if (!_rpcHooksRegistered)
            {
                EnsureRPCHooks();
            }
            if (!_signalsSubscribed)
            {
                EnsureSignalSubscriptions();
            }
            DiagnosticProbeHost.EnsureSessionDirectory();

            // 限频采样：最大 2 Hz / 0.5s 一次
            if (Time.unscaledTime - _lastSampleTime >= 0.5f)
            {
                _lastSampleTime = Time.unscaledTime;
                SampleHandCards();
            }
        }

        private void SampleHandCards()
        {
            try
            {
                // 每次快照严格 Fresh Reacquire 顶层 runtime 引用，严禁依赖长期缓存
                BattleLogic battle = SimpleSingletonProvider<GameLogicManager>.inst?.battle;
                if (battle == null || battle.PlayerDatas == null)
                {
                    return;
                }

                RoomLogic room = SimpleSingletonProvider<GameLogicManager>.inst?.room;
                long roomId = room?.curRoomInfo?.Id ?? 0L;

                BattlePlayerData selfData = battle.GetSelfPlayerData();
                long selfId = (selfData != null && selfData.player != null) ? selfData.player.Id : 0L;
                bool hasAnyRemoteHumanInBattle = false;

                // 回合切换侦测 [HC_TURN_START]
                bool turnChanged = false;
                if (battle.curPlayerId != _lastCurPlayerId)
                {
                    _lastCurPlayerId = battle.curPlayerId;
                    turnChanged = true;
                }

                for (int i = 0; i < battle.PlayerDatas.Count; i++)
                {
                    BattlePlayerData bpd = battle.PlayerDatas[i];
                    if (bpd == null || bpd.player == null)
                    {
                        continue;
                    }

                    long playerId = bpd.player.Id;
                    int slot = bpd.player.Slot;
                    bool isSelf = (playerId == selfId);
                    bool isBot = bpd.player.IsBot;
                    if (!isSelf && !isBot)
                    {
                        hasAnyRemoteHumanInBattle = true;
                    }

                    string playerType = isSelf ? "Self" : (isBot ? "Bot" : "HumanRemote");

                    // Fresh Reacquire Container 与 _HandCards
                    global::Core.CardContainer container = bpd.cardContainer;
                    List<HandCardData> handList = container?._HandCards;

                    int bpdRef = RuntimeHelpers.GetHashCode(bpd);
                    int containerRef = container != null ? RuntimeHelpers.GetHashCode(container) : 0;
                    int handListRef = handList != null ? RuntimeHelpers.GetHashCode(handList) : 0;
                    int currentCount = container != null ? container.CardCount : 0;
                    int listCount = handList != null ? handList.Count : 0;

                    StringBuilder cardsBuilder = new StringBuilder();
                    int positiveCardCount = 0;
                    if (handList != null)
                    {
                        for (int k = 0; k < handList.Count; k++)
                        {
                            if (k > 0) cardsBuilder.Append(";");
                            int cid = handList[k] != null ? handList[k].CardId : -1;
                            if (cid > 0) positiveCardCount++;
                            cardsBuilder.Append(cid);
                        }
                    }
                    string cardsStr = cardsBuilder.ToString();

                    // 对象 Identity 变化探测与比对
                    bool hasCached = _lastObjectIdentities.TryGetValue(playerId, out CachedObjectIdentity prevIdentity);
                    bool sameContainer = hasCached && (prevIdentity.ContainerRef == containerRef);
                    bool sameList = hasCached && (prevIdentity.HandListRef == handListRef);
                    bool identityOrStateChanged = !hasCached ||
                                                  (prevIdentity.BpdRef != bpdRef) ||
                                                  !sameContainer ||
                                                  !sameList ||
                                                  (prevIdentity.CardCount != currentCount) ||
                                                  (prevIdentity.ListCount != listCount) ||
                                                  (prevIdentity.Cards != cardsStr);

                    if (identityOrStateChanged)
                    {
                        // 牌数变化侦测 [HC_COUNT_CHANGE]
                        if (hasCached && prevIdentity.CardCount != currentCount)
                        {
                            DiagnosticProbeHost.LogDiagnostic("handcard", "HC_COUNT_CHANGE",
                                $"RoomId={roomId} PlayerId={playerId} PlayerType={playerType} OldCount={prevIdentity.CardCount} NewCount={currentCount} CardsBefore=[{prevIdentity.Cards}] CardsAfter=[{cardsStr}] ContainerRefBefore={prevIdentity.ContainerRef:X8} ContainerRefAfter={containerRef:X8} ListRefBefore={prevIdentity.HandListRef:X8} ListRefAfter={handListRef:X8}");
                        }

                        _lastObjectIdentities[playerId] = new CachedObjectIdentity
                        {
                            BpdRef = bpdRef,
                            ContainerRef = containerRef,
                            HandListRef = handListRef,
                            CardCount = currentCount,
                            ListCount = listCount,
                            Cards = cardsStr
                        };

                        DiagnosticProbeHost.LogDiagnostic("handcard", "HC_OBJECT_ID",
                            $"RoomId={roomId} PlayerId={playerId} PlayerType={playerType} BpdRef={bpdRef:X8} ContainerRef={containerRef:X8} HandListRef={handListRef:X8} SameContainer={sameContainer} SameList={sameList} CardCount={currentCount} ListCount={listCount} Cards=[{cardsStr}]");
                    }

                    // 回合开始时记录 Bot 快照 [HC_TURN_START]
                    if (turnChanged && isBot)
                    {
                        DiagnosticProbeHost.LogDiagnostic("handcard", "HC_TURN_START",
                            $"RoomId={roomId} PlayerId={playerId} ActiveCurPlayerId={_lastCurPlayerId} CardCount={currentCount} ListCount={listCount} Cards=[{cardsStr}] ContainerRef={containerRef:X8} HandListRef={handListRef:X8}");
                    }

                    _trackedCardCounts.TryGetValue(playerId, out int prevCount);

                    // 1. Initial Deal 诊断与快照 [HC_INITIAL]
                    if (!_initialDealReported.Contains(playerId) && currentCount > 0)
                    {
                        _initialDealReported.Add(playerId);
                        if (isBot)
                        {
                            HashSet<int> initialSet = new HashSet<int>();
                            if (handList != null)
                            {
                                for (int k = 0; k < handList.Count; k++)
                                {
                                    if (handList[k] != null && handList[k].CardId > 0)
                                    {
                                        initialSet.Add(handList[k].CardId);
                                    }
                                }
                            }
                            _initialKnownSets[playerId] = initialSet;
                            _observedPlayedCards[playerId] = new List<int>();
                            _unknownDrawCounts[playerId] = 0;

                            DiagnosticProbeHost.LogDiagnostic("handcard", "HC_INITIAL",
                                $"RoomId={roomId} PlayerId={playerId} ContainerRef={containerRef:X8} HandListRef={handListRef:X8} CardCount={currentCount} Cards=[{cardsStr}] KnownCount={positiveCardCount}");
                        }
                        else if (!isSelf)
                        {
                            bool hasAnyRealId = HasAnyRealCardId(container);
                            if (!hasAnyRealId)
                            {
                                DiagnosticProbeHost.LogDiagnostic("handcard", "HC_DEAL",
                                    $"INITIAL_REAL_IDS_NOT_EXPOSED PlayerId={playerId} PlayerType={playerType} RawCount={currentCount}");
                            }
                        }
                    }

                    // 2. 摸牌变化诊断
                    if (currentCount > prevCount && prevCount > 0)
                    {
                        if (!isSelf && !HasAnyRealCardId(container))
                        {
                            DiagnosticProbeHost.LogDiagnostic("handcard", "HC_DRAW",
                                $"DRAW_REAL_ID_NOT_EXPOSED PlayerId={playerId} OldCount={prevCount} NewCount={currentCount}");
                        }
                    }

                    _trackedCardCounts[playerId] = currentCount;

                    // 3. 快照采样 (逐张卡牌分析)
                    if (isSelf)
                    {
                        SampleSelfHand(selfData, slot, currentCount);
                    }
                    else
                    {
                        SampleRemoteHand(bpd, slot, playerType, currentCount);
                    }
                }

                if (!hasAnyRemoteHumanInBattle && !_humanRemoteObserved)
                {
                    _humanRemoteObserved = true;
                    DiagnosticProbeHost.LogDiagnostic("handcard", "HC_INFO", "HUMAN_REMOTE_NOT_OBSERVED (PVE Match with Bot/Self only)");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XingShu LocalHandCardProbe] Sample error: " + ex.Message);
            }
        }

        private void SampleSelfHand(BattlePlayerData selfData, int slot, int count)
        {
            if (selfData.cardContainer == null || selfData.cardContainer._HandCards == null)
            {
                return;
            }

            List<HandCardData> cards = selfData.cardContainer._HandCards;
            for (int k = 0; k < cards.Count; k++)
            {
                HandCardData card = cards[k];
                int cardId = card != null ? card.CardId : -1;
                bool known = (cardId > 0);

                string key = $"SELF_{selfData.player.Id}_{k}_{cardId}";
                if (!_dedupCache.Contains(key))
                {
                    _dedupCache.Add(key);
                    DiagnosticProbeHost.LogDiagnostic("handcard", "HC_LOCAL_PROBE",
                        $"PlayerType=Self PlayerId={selfData.player.Id} Slot={slot} Source=CardContainer.HandCards Count={count} Index={k} CardId={cardId} Known={known}");
                }
            }
        }

        private void SampleRemoteHand(BattlePlayerData bpd, int slot, string playerType, int count)
        {
            if (bpd.cardContainer == null)
            {
                return;
            }

            List<HandCardData> cards = bpd.cardContainer._HandCards;
            int cardElementsCount = cards != null ? cards.Count : 0;

            int displayCount = Math.Max(count, cardElementsCount);
            for (int k = 0; k < displayCount; k++)
            {
                int cardId = -1;
                if (cards != null && k < cards.Count && cards[k] != null)
                {
                    cardId = cards[k].CardId;
                }
                bool known = (cardId > 0);

                string key = $"{playerType}_{bpd.player.Id}_{k}_{cardId}_{count}";
                if (!_dedupCache.Contains(key))
                {
                    _dedupCache.Add(key);
                    DiagnosticProbeHost.LogDiagnostic("handcard", "HC_LOCAL_PROBE",
                        $"PlayerType={playerType} PlayerId={bpd.player.Id} Slot={slot} Source=CardContainer Count={count} Index={k} CardId={cardId} Known={known}");
                }
            }
        }

        private static bool HasAnyRealCardId(global::Core.CardContainer container)
        {
            if (container == null || container._HandCards == null) return false;
            for (int i = 0; i < container._HandCards.Count; i++)
            {
                if (container._HandCards[i] != null && container._HandCards[i].CardId > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void EnsureRPCHooks()
        {
            try
            {
                NetManager inst = MonoSingletonProvider<NetManager>.inst;
                if (inst == null || inst.RPC == null)
                {
                    return;
                }

                // 1. RoomHeroCardChangeS2C
                if (inst.RPC.RoomHeroCardChangeS2C != null)
                {
                    RoomHeroCardChangeS2CRPC.OnRoomHeroCardChangeS2CServerDelegate cur1 = inst.RPC.RoomHeroCardChangeS2C.OnRoomHeroCardChangeS2CServerCallBackAsync;
                    if (cur1 == InterceptedCardChangeCallback)
                    {
                        _nestedDetected = true;
                    }
                    else
                    {
                        _originalCardChangeCallback = cur1;
                        inst.RPC.RoomHeroCardChangeS2C.OnRoomHeroCardChangeS2CServerCallBackAsync = InterceptedCardChangeCallback;
                        _installCount++;
                    }
                }

                // 2. BattleUseCardS2C
                if (inst.RPC.BattleUseCardS2C != null)
                {
                    BattleUseCardS2CRPC.OnBattleUseCardS2CServerDelegate cur2 = inst.RPC.BattleUseCardS2C.OnBattleUseCardS2CServerCallBackAsync;
                    if (cur2 == InterceptedUseCardCallback)
                    {
                        _nestedDetected = true;
                    }
                    else
                    {
                        _originalUseCardCallback = cur2;
                        inst.RPC.BattleUseCardS2C.OnBattleUseCardS2CServerCallBackAsync = InterceptedUseCardCallback;
                    }
                }

                _rpcHooksRegistered = true;
                DiagnosticProbeHost.UpdateHookStateFile();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XingShu LocalHandCardProbe] Ensure RPC hooks error: " + ex.Message);
            }
        }

        private static void RestoreRPCHooks()
        {
            try
            {
                NetManager inst = MonoSingletonProvider<NetManager>.inst;
                if (inst != null && inst.RPC != null)
                {
                    if (inst.RPC.RoomHeroCardChangeS2C != null)
                    {
                        if (inst.RPC.RoomHeroCardChangeS2C.OnRoomHeroCardChangeS2CServerCallBackAsync == InterceptedCardChangeCallback)
                        {
                            inst.RPC.RoomHeroCardChangeS2C.OnRoomHeroCardChangeS2CServerCallBackAsync = _originalCardChangeCallback;
                            _restoreCount++;
                        }
                        else
                        {
                            DiagnosticProbeHost.LogDiagnostic("handcard", "PROBE_HOOK", "HOOK_RESTORE_SKIPPED_CHANGED_OWNER RoomHeroCardChangeS2C");
                        }
                    }

                    if (inst.RPC.BattleUseCardS2C != null)
                    {
                        if (inst.RPC.BattleUseCardS2C.OnBattleUseCardS2CServerCallBackAsync == InterceptedUseCardCallback)
                        {
                            inst.RPC.BattleUseCardS2C.OnBattleUseCardS2CServerCallBackAsync = _originalUseCardCallback;
                        }
                        else
                        {
                            DiagnosticProbeHost.LogDiagnostic("handcard", "PROBE_HOOK", "HOOK_RESTORE_SKIPPED_CHANGED_OWNER BattleUseCardS2C");
                        }
                    }
                }
                _rpcHooksRegistered = false;
                DiagnosticProbeHost.UpdateHookStateFile();
            }
            catch
            {
            }
        }

        private void EnsureSignalSubscriptions()
        {
            if (_signalsSubscribed) return;
            try
            {
                CardLogic cardLogic = SimpleSingletonProvider<GameLogicManager>.inst?.card;
                if (cardLogic != null && cardLogic.signal != null)
                {
                    cardLogic.signal.resetSelfCardList.AddListener(OnSignalResetSelfCardList);
                    cardLogic.signal.resetCardList.AddListener(OnSignalResetCardList);
                    cardLogic.signal.recycleCard.AddListener(OnSignalRecycleCard);
                    cardLogic.signal.cancelUse.AddListener(OnSignalCancelUse);
                    _signalsSubscribed = true;
                    DiagnosticProbeHost.LogDiagnostic("handcard", "SIGNAL_SUBSCRIBE", "CardLogic signals subscribed successfully.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XingShu LocalHandCardProbe] Signal subscribe error: " + ex.Message);
            }
        }

        private void RestoreSignalSubscriptions()
        {
            if (!_signalsSubscribed) return;
            try
            {
                CardLogic cardLogic = SimpleSingletonProvider<GameLogicManager>.inst?.card;
                if (cardLogic != null && cardLogic.signal != null)
                {
                    cardLogic.signal.resetSelfCardList.RemoveListener(OnSignalResetSelfCardList);
                    cardLogic.signal.resetCardList.RemoveListener(OnSignalResetCardList);
                    cardLogic.signal.recycleCard.RemoveListener(OnSignalRecycleCard);
                    cardLogic.signal.cancelUse.RemoveListener(OnSignalCancelUse);
                }
                _signalsSubscribed = false;
            }
            catch
            {
            }
        }

        private void OnSignalResetSelfCardList(long playerId)
        {
            if (!DiagnosticProbeHost.IsEnabled) return;
            try
            {
                BattleLogic battle = SimpleSingletonProvider<GameLogicManager>.inst?.battle;
                BattlePlayerData targetBpd = battle?.GetPlayerDataById(playerId);
                global::Core.CardContainer container = targetBpd?.cardContainer;
                List<HandCardData> cards = container?._HandCards;

                int cRef = container != null ? RuntimeHelpers.GetHashCode(container) : 0;
                int lRef = cards != null ? RuntimeHelpers.GetHashCode(cards) : 0;
                int count = container != null ? container.CardCount : 0;

                StringBuilder sb = new StringBuilder();
                if (cards != null)
                {
                    for (int i = 0; i < cards.Count; i++)
                    {
                        if (i > 0) sb.Append(";");
                        sb.Append(cards[i] != null ? cards[i].CardId : -1);
                    }
                }
                DiagnosticProbeHost.LogDiagnostic("handcard", "SIGNAL_CARD",
                    $"SignalName=resetSelfCardList PlayerId={playerId} ContainerRef={cRef:X8} HandListRef={lRef:X8} CardCount={count} ListCount={(cards != null ? cards.Count : 0)} Cards=[{sb}]");
            }
            catch
            {
            }
        }

        private void OnSignalResetCardList()
        {
            if (!DiagnosticProbeHost.IsEnabled) return;
            DiagnosticProbeHost.LogDiagnostic("handcard", "SIGNAL_CARD", "SignalName=resetCardList");
        }

        private void OnSignalRecycleCard()
        {
            if (!DiagnosticProbeHost.IsEnabled) return;
            DiagnosticProbeHost.LogDiagnostic("handcard", "SIGNAL_CARD", "SignalName=recycleCard");
        }

        private void OnSignalCancelUse()
        {
            if (!DiagnosticProbeHost.IsEnabled) return;
            DiagnosticProbeHost.LogDiagnostic("handcard", "SIGNAL_CARD", "SignalName=cancelUse");
        }

        private static async UniTask InterceptedCardChangeCallback(RoomHeroCardChangeS2C model, int errId, bool isDispatch)
        {
            int beforeCRef = 0;
            int beforeLRef = 0;
            int containerBeforeCount = 0;
            bool isTargetBot = false;
            int rawFirstId = -1;

            if (DiagnosticProbeHost.IsEnabled && model != null)
            {
                try
                {
                    StringBuilder idsBuilder = new StringBuilder();
                    int knownCount = 0;
                    if (model.Cards != null)
                    {
                        for (int i = 0; i < model.Cards.Count; i++)
                        {
                            CardInfo info = model.Cards[i];
                            int cid = info != null ? info.CardId : -1;
                            if (cid > 0) knownCount++;
                            if (i > 0) idsBuilder.Append(";");
                            idsBuilder.Append(cid);
                        }
                        if (model.Cards.Count > 0 && model.Cards[0] != null)
                        {
                            rawFirstId = model.Cards[0].CardId;
                        }
                    }

                    DiagnosticProbeHost.LogDiagnostic("handcard", "HC_CALLBACK_RAW",
                        $"PlayerId={model.PlayerId} ChangeType=RoomHeroCardChangeS2C Count={(model.Cards != null ? model.Cards.Count : 0)} RawCardIds=[{idsBuilder}] CardIdKnownCount={knownCount}");

                    // Fresh Reacquire
                    BattleLogic battle = SimpleSingletonProvider<GameLogicManager>.inst?.battle;
                    BattlePlayerData targetBpd = battle?.GetPlayerDataById(model.PlayerId);
                    isTargetBot = (targetBpd != null && targetBpd.player != null && targetBpd.player.IsBot);

                    global::Core.CardContainer container = targetBpd?.cardContainer;
                    List<HandCardData> handCards = container?._HandCards;

                    beforeCRef = container != null ? RuntimeHelpers.GetHashCode(container) : 0;
                    beforeLRef = handCards != null ? RuntimeHelpers.GetHashCode(handCards) : 0;
                    containerBeforeCount = container != null ? container.CardCount : 0;

                    StringBuilder beforeCards = new StringBuilder();
                    if (handCards != null)
                    {
                        for (int j = 0; j < handCards.Count; j++)
                        {
                            if (j > 0) beforeCards.Append(";");
                            beforeCards.Append(handCards[j] != null ? handCards[j].CardId : -1);
                        }
                    }
                    DiagnosticProbeHost.LogDiagnostic("handcard", "HC_CONTAINER_BEFORE",
                        $"PlayerId={model.PlayerId} ContainerRef={beforeCRef:X8} HandListRef={beforeLRef:X8} Count={containerBeforeCount} HandCards=[{beforeCards}]");

                    // Bot 抽牌前抓取 [HC_DRAW_RAW]
                    if (isTargetBot)
                    {
                        DiagnosticProbeHost.LogDiagnostic("handcard", "HC_DRAW_RAW",
                            $"PlayerId={model.PlayerId} RawCardId={rawFirstId} CardCountBefore={containerBeforeCount}");
                    }
                }
                catch
                {
                }
            }

            // 严格单次调用原委托，保持返回值与异常语义不变
            if (_originalCardChangeCallback != null)
            {
                await _originalCardChangeCallback(model, errId, isDispatch);
            }

            if (DiagnosticProbeHost.IsEnabled && model != null)
            {
                try
                {
                    // 立即 Fresh Reacquire，侦测是否有实例替换
                    BattleLogic battle = SimpleSingletonProvider<GameLogicManager>.inst?.battle;
                    BattlePlayerData targetBpd = battle?.GetPlayerDataById(model.PlayerId);
                    global::Core.CardContainer container = targetBpd?.cardContainer;
                    List<HandCardData> handCards = container?._HandCards;

                    int afterCRef = container != null ? RuntimeHelpers.GetHashCode(container) : 0;
                    int afterLRef = handCards != null ? RuntimeHelpers.GetHashCode(handCards) : 0;
                    int containerCount = container != null ? container.CardCount : 0;
                    bool sameContainer = (beforeCRef != 0 && beforeCRef == afterCRef);
                    bool sameList = (beforeLRef != 0 && beforeLRef == afterLRef);

                    StringBuilder containerCards = new StringBuilder();
                    if (handCards != null)
                    {
                        for (int j = 0; j < handCards.Count; j++)
                        {
                            if (j > 0) containerCards.Append(";");
                            containerCards.Append(handCards[j] != null ? handCards[j].CardId : -1);
                        }
                    }
                    DiagnosticProbeHost.LogDiagnostic("handcard", "HC_CONTAINER_AFTER",
                        $"PlayerId={model.PlayerId} ContainerRef={afterCRef:X8} HandListRef={afterLRef:X8} SameContainer={sameContainer} SameList={sameList} Count={containerCount} HandCards=[{containerCards}]");

                    // Bot 抽牌后抓取 [HC_DRAW_AFTER] 与判定
                    if (isTargetBot)
                    {
                        DiagnosticProbeHost.LogDiagnostic("handcard", "HC_DRAW_AFTER",
                            $"PlayerId={model.PlayerId} CardCountAfter={containerCount} CardsAfter=[{containerCards}]");

                        if (rawFirstId <= 0 && containerCount > containerBeforeCount)
                        {
                            DiagnosticProbeHost.LogDiagnostic("handcard", "HC_DRAW_CONFIRM",
                                $"BOT_DRAW_ID_HIDDEN_CONFIRMED PlayerId={model.PlayerId} RawCardId={rawFirstId} OldCount={containerBeforeCount} NewCount={containerCount}");

                            if (_instance != null)
                            {
                                _instance._unknownDrawCounts.TryGetValue(model.PlayerId, out int draws);
                                _instance._unknownDrawCounts[model.PlayerId] = draws + (containerCount - containerBeforeCount);
                                _instance.LogReconstruction(model.PlayerId);
                            }
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private static async UniTask InterceptedUseCardCallback(BattleUseCardS2C model, int errId, bool isDispatch)
        {
            string beforeKnown = "";
            int beforeCRef = 0;
            int beforeLRef = 0;
            int beforeCardCount = 0;
            bool isTargetBot = false;
            HashSet<int> beforeIds = new HashSet<int>();

            if (DiagnosticProbeHost.IsEnabled && model != null)
            {
                try
                {
                    BattleLogic battle = SimpleSingletonProvider<GameLogicManager>.inst?.battle;
                    BattlePlayerData targetBpd = battle?.GetPlayerDataById(model.PlayerId);
                    isTargetBot = (targetBpd != null && targetBpd.player != null && targetBpd.player.IsBot);

                    global::Core.CardContainer container = targetBpd?.cardContainer;
                    List<HandCardData> handCards = container?._HandCards;

                    beforeCRef = container != null ? RuntimeHelpers.GetHashCode(container) : 0;
                    beforeLRef = handCards != null ? RuntimeHelpers.GetHashCode(handCards) : 0;
                    beforeCardCount = container != null ? container.CardCount : 0;

                    if (handCards != null)
                    {
                        StringBuilder sb = new StringBuilder();
                        for (int k = 0; k < handCards.Count; k++)
                        {
                            int cid = handCards[k] != null ? handCards[k].CardId : -1;
                            if (cid > 0) beforeIds.Add(cid);
                            if (k > 0) sb.Append(";");
                            sb.Append(cid);
                        }
                        beforeKnown = sb.ToString();
                    }

                    // Bot 出牌前抓取 [HC_PLAY_RAW]
                    DiagnosticProbeHost.LogDiagnostic("handcard", "HC_PLAY_RAW",
                        $"PlayerId={model.PlayerId} CardId={model.CardId} CardCountBefore={beforeCardCount} FreshCardsBefore=[{beforeKnown}]");
                }
                catch
                {
                }
            }

            // 严格单次调用原委托
            if (_originalUseCardCallback != null)
            {
                await _originalUseCardCallback(model, errId, isDispatch);
            }

            if (DiagnosticProbeHost.IsEnabled && model != null)
            {
                try
                {
                    BattleLogic battle = SimpleSingletonProvider<GameLogicManager>.inst?.battle;
                    BattlePlayerData targetBpd = battle?.GetPlayerDataById(model.PlayerId);
                    global::Core.CardContainer container = targetBpd?.cardContainer;
                    List<HandCardData> handCards = container?._HandCards;

                    int afterCRef = container != null ? RuntimeHelpers.GetHashCode(container) : 0;
                    int afterLRef = handCards != null ? RuntimeHelpers.GetHashCode(handCards) : 0;
                    int afterCardCount = container != null ? container.CardCount : 0;
                    bool sameContainer = (beforeCRef != 0 && beforeCRef == afterCRef);
                    bool sameList = (beforeLRef != 0 && beforeLRef == afterLRef);

                    string afterKnown = "";
                    HashSet<int> afterIds = new HashSet<int>();
                    if (handCards != null)
                    {
                        StringBuilder sb = new StringBuilder();
                        for (int k = 0; k < handCards.Count; k++)
                        {
                            int cid = handCards[k] != null ? handCards[k].CardId : -1;
                            if (cid > 0) afterIds.Add(cid);
                            if (k > 0) sb.Append(";");
                            sb.Append(cid);
                        }
                        afterKnown = sb.ToString();
                    }

                    // Bot 出牌后抓取 [HC_PLAY_AFTER]
                    DiagnosticProbeHost.LogDiagnostic("handcard", "HC_PLAY_AFTER",
                        $"PlayerId={model.PlayerId} CardId={model.CardId} CardCountAfter={afterCardCount} FreshCardsAfter=[{afterKnown}] ContainerRefAfter={afterCRef:X8} ListRefAfter={afterLRef:X8}");

                    DiagnosticProbeHost.LogDiagnostic("handcard", "HC_PLAY_REVEAL",
                        $"PlayerId={model.PlayerId} CardId={model.CardId} ContainerRef={afterCRef:X8} HandListRef={afterLRef:X8} SameContainer={sameContainer} SameList={sameList} BeforeKnownCards=[{beforeKnown}] AfterKnownCards=[{afterKnown}]");

                    // 关键判定：如果 Bot 出牌 CardId 在出牌前包含且出牌后仍包含且 CardCount 减少，判 STALE_INITIAL_SNAPSHOT_CONFIRMED
                    if (isTargetBot && beforeIds.Contains(model.CardId) && afterIds.Contains(model.CardId) && afterCardCount == beforeCardCount - 1)
                    {
                        DiagnosticProbeHost.LogDiagnostic("handcard", "HC_STALE_CONFIRM",
                            $"STALE_INITIAL_SNAPSHOT_CONFIRMED PlayerId={model.PlayerId} CardId={model.CardId} ContainerRef={afterCRef:X8} HandListRef={afterLRef:X8}");
                    }

                    // 纯诊断用事件重构追踪
                    if (isTargetBot && _instance != null)
                    {
                        if (!_instance._observedPlayedCards.ContainsKey(model.PlayerId))
                        {
                            _instance._observedPlayedCards[model.PlayerId] = new List<int>();
                        }
                        _instance._observedPlayedCards[model.PlayerId].Add(model.CardId);
                        _instance.LogReconstruction(model.PlayerId);
                    }
                }
                catch
                {
                }
            }
        }

        private void LogReconstruction(long playerId)
        {
            try
            {
                _initialKnownSets.TryGetValue(playerId, out HashSet<int> initialSet);
                _observedPlayedCards.TryGetValue(playerId, out List<int> playedList);
                _unknownDrawCounts.TryGetValue(playerId, out int unknownDraws);

                HashSet<int> remainingInitial = new HashSet<int>();
                if (initialSet != null)
                {
                    foreach (int cid in initialSet)
                    {
                        remainingInitial.Add(cid);
                    }
                }
                if (playedList != null)
                {
                    for (int i = 0; i < playedList.Count; i++)
                    {
                        remainingInitial.Remove(playedList[i]);
                    }
                }

                StringBuilder remSb = new StringBuilder();
                foreach (int cid in remainingInitial)
                {
                    if (remSb.Length > 0) remSb.Append(";");
                    remSb.Append(cid);
                }
                string remStr = remSb.ToString();
                string completeness = (unknownDraws == 0 && remainingInitial.Count > 0) ? "FULL" : "PARTIAL";

                DiagnosticProbeHost.LogDiagnostic("handcard", "HC_RECON",
                    $"PlayerId={playerId} KnownRemainingFromInitial=[{remStr}] UnknownDrawCount={unknownDraws} Completeness={completeness}");
            }
            catch
            {
            }
        }

        private void CleanupSession()
        {
            _humanRemoteObserved = false;
            _lastCurPlayerId = -1;
            _trackedCardCounts.Clear();
            _initialDealReported.Clear();
            _dedupCache.Clear();
            _lastObjectIdentities.Clear();
            _initialKnownSets.Clear();
            _observedPlayedCards.Clear();
            _unknownDrawCounts.Clear();
        }
    }
#endif
}
