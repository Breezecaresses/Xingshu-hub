using System;
using System.Reflection;
using APXRuntime.Core;
using UnityEngine;
using XingShu.Runtime;

namespace APXRuntime.Utilities;

public static class UtilityRuntime
{
	public const string RuntimeVersion = "2026-09-14-utility-v2";

	private static bool _assemblyResolveBound;

	private static Assembly OnResolveAssembly(object sender, ResolveEventArgs args)
	{
		try
		{
			if (args != null)
			{
				PropertyInfo property = typeof(ResolveEventArgs).GetProperty("Name");
				string text = ((property != null) ? (property.GetValue(args, null) as string) : null);
				if (!string.IsNullOrEmpty(text))
				{
					string name = new AssemblyName(text).Name;
					Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
					foreach (Assembly assembly in assemblies)
					{
						if (assembly != null && string.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
						{
							return assembly;
						}
					}
				}
			}
		}
		catch
		{
		}
		return null;
	}

	public static void Install()
	{
		if (!_assemblyResolveBound)
		{
			try
			{
				MethodInfo method = typeof(AppDomain).GetMethod("add_AssemblyResolve", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (method != null)
				{
					ParameterInfo[] parameters = method.GetParameters();
					if (parameters.Length != 0 && parameters[0].ParameterType != null)
					{
						MethodInfo method2 = typeof(UtilityRuntime).GetMethod("OnResolveAssembly", BindingFlags.Static | BindingFlags.NonPublic);
						if (method2 != null)
						{
							Delegate @delegate = Delegate.CreateDelegate(parameters[0].ParameterType, method2);
							method.Invoke(AppDomain.CurrentDomain, new object[1] { @delegate });
							_assemblyResolveBound = true;
						}
					}
				}
			}
			catch
			{
			}
		}

		// 委托星枢运行时桥接器完成核心宿主创建与初始化
		GameObject gameObject = XSRuntimeBridge.EnsureInitialized();

		if (gameObject.GetComponent<APXSceneWatcher>() == null)
		{
			gameObject.AddComponent<APXSceneWatcher>();
		}
		if (gameObject.GetComponent<APXCentralInputManager>() == null)
		{
			gameObject.AddComponent<APXCentralInputManager>();
		}
		if (gameObject.GetComponent<CharacterAnimProbe>() == null)
		{
			gameObject.AddComponent<CharacterAnimProbe>();
		}
		try
		{
			Assembly assembly = typeof(UtilityRuntime).Assembly;
			int num = 0;
			Type[] types = assembly.GetTypes();
			foreach (Type type in types)
			{
				if (type == typeof(UtilityRuntime))
				{
					continue;
				}
				try
				{
					MethodInfo method3 = type.GetMethod("Install", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(GameObject) }, null);
					if (method3 != null)
					{
						method3.Invoke(null, new object[1] { gameObject });
						num++;
						XSRuntimeBridge.RegisterPlugin(type.FullName, type);
						Debug.Log("[APX Plugin Discovery] 已装配插件: " + type.FullName);
						continue;
					}
					MethodInfo method4 = type.GetMethod("Install", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
					if (method4 != null)
					{
						method4.Invoke(null, null);
						num++;
						XSRuntimeBridge.RegisterPlugin(type.FullName, type);
						Debug.Log("[APX Plugin Discovery] 已装配插件: " + type.FullName);
					}
				}
				catch (Exception ex)
				{
					Debug.LogWarning("[APX Plugin Discovery] 插件装配失败 (" + type.FullName + "): " + ex.Message);
				}
			}
			Debug.Log("[APX Plugin Discovery] 插件扫描装配完成，共装配 " + num + " 个业务插件。");
		}
		catch (Exception ex2)
		{
			Debug.LogWarning("[APX Plugin Discovery] 扫描程序集插件异常: " + ex2);
		}
		Debug.Log("[XingShu Runtime] Canonical runtime host activated (bridged to APX 3.3.0)");
		Debug.Log("[APX] Utility runtime installed version=2026-09-14-utility-v2");
	}
}
