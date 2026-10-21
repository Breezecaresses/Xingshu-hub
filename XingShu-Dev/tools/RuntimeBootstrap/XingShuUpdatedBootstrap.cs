using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace XingShu
{
    public static class XingShuUpdatedBootstrap
    {
        private static int _installState = 0;

        private static void LogStage(string stage)
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "xingshu_boot_330_status.txt");
                string line = stage + "\n";
                File.AppendAllText(path, line);
            }
            catch
            {
            }
        }

        public static void InstallOnce()
        {
            if (Interlocked.CompareExchange(ref _installState, 1, 0) != 0)
            {
                return;
            }

            LogStage("ENTER");

            AssetBundle hostBundle = null;
            try
            {
                IEnumerable<AssetBundle> loadedBundles = AssetBundle.GetAllLoadedAssetBundles();
                if (loadedBundles != null)
                {
                    foreach (AssetBundle b in loadedBundles)
                    {
                        if (b != null && b.Contains("XingShu.Runtime.dll"))
                        {
                            hostBundle = b;
                            break;
                        }
                    }
                }

                if (hostBundle == null)
                {
                    _installState = 3;
                    LogStage("CORE_FAILED:System.Exception:Payload host bundle containing XingShu.Runtime.dll not found");
                    Debug.LogError("[XINGSHU_BOOT_330_ERROR] Payload host bundle containing XingShu.Runtime.dll not found among loaded AssetBundles");
                    return;
                }

                LogStage("PAYLOAD_BUNDLE_FOUND");

                // ==========================================
                // CORE PHASE: XingShu.Runtime.dll
                // ==========================================
                TextAsset hooksAsset = hostBundle.LoadAsset("XingShu.Runtime.dll") as TextAsset;
                if (hooksAsset == null || hooksAsset.bytes == null || hooksAsset.bytes.Length == 0)
                {
                    _installState = 3;
                    LogStage("CORE_FAILED:System.Exception:XingShu.Runtime.dll TextAsset missing or empty in host bundle");
                    Debug.LogError("[XINGSHU_BOOT_330_ERROR] XingShu.Runtime.dll TextAsset missing or empty in host bundle");
                    return;
                }

                LogStage("XINGSHU_HOOKS_ASSET_FOUND");

                Assembly hooksAsm = Assembly.Load(hooksAsset.bytes);
                if (hooksAsm == null)
                {
                    _installState = 3;
                    LogStage("CORE_FAILED:System.Exception:Assembly.Load returned null for XingShu.Runtime.dll");
                    Debug.LogError("[XINGSHU_BOOT_330_ERROR] Assembly.Load returned null for XingShu.Runtime.dll");
                    return;
                }

                LogStage("XINGSHU_HOOKS_LOADED");

                // Identity Logging
                LogStage("HOOKS_FULLNAME:" + hooksAsm.FullName);
                Module[] mods = hooksAsm.GetModules(false);
                if (mods != null && mods.Length > 0)
                {
                    LogStage("HOOKS_MODULE:" + mods[0].ScopeName);
                    LogStage("HOOKS_MVID:" + mods[0].ModuleVersionId.ToString());
                }
                else
                {
                    LogStage("HOOKS_MODULE:none");
                    LogStage("HOOKS_MVID:none");
                }

                // Image Identity Validation Gate
                if (hooksAsm.FullName == null || !hooksAsm.FullName.StartsWith("XingShu.Runtime"))
                {
                    _installState = 3;
                    LogStage("CORE_FAILED:WRONG_HOOKS_IMAGE:" + hooksAsm.FullName);
                    Debug.LogError("[XINGSHU_BOOT_330_ERROR] Loaded image is not XingShu.Runtime: " + hooksAsm.FullName);
                    return;
                }

                Type utilityRuntimeType = hooksAsm.GetType("XingShuRuntime.Utilities.UtilityRuntime");
                if (utilityRuntimeType == null)
                {
                    _installState = 3;
                    LogStage("CORE_FAILED:System.TypeLoadException:XingShuRuntime.Utilities.UtilityRuntime not found in XingShu.Runtime");
                    Debug.LogError("[XINGSHU_BOOT_330_ERROR] XingShuRuntime.Utilities.UtilityRuntime not found in XingShu.Runtime");
                    return;
                }

                MethodInfo installMethod = utilityRuntimeType.GetMethod("Install", BindingFlags.Public | BindingFlags.Static);
                if (installMethod == null)
                {
                    _installState = 3;
                    LogStage("CORE_FAILED:System.MissingMethodException:UtilityRuntime.Install method not found");
                    Debug.LogError("[XINGSHU_BOOT_330_ERROR] UtilityRuntime.Install method not found");
                    return;
                }

                LogStage("UTILITY_INSTALL_BEGIN");
                installMethod.Invoke(null, null);
                LogStage("UTILITY_INSTALL_OK");
                _installState = 2;
                LogStage("CORE_SUCCESS");

                // ==========================================
                // OPTIONAL DRIVER 1: Live2D Cubism
                // ==========================================
                try
                {
                    LogStage("LIVE2D_LOAD_BEGIN");
                    TextAsset live2dAsset = hostBundle.LoadAsset("XingShu.live2d.cubism.dll") as TextAsset;
                    if (live2dAsset != null && live2dAsset.bytes != null && live2dAsset.bytes.Length > 0)
                    {
                        Assembly live2dAsm = Assembly.Load(live2dAsset.bytes);
                        if (live2dAsm != null)
                        {
                            LogStage("LIVE2D_LOAD_OK");
                        }
                        else
                        {
                            LogStage("LIVE2D_LOAD_FAILED:System.Exception:Assembly.Load returned null");
                        }
                    }
                    else
                    {
                        LogStage("LIVE2D_LOAD_FAILED:System.Exception:Asset missing or empty");
                    }
                }
                catch (Exception ex)
                {
                    LogStage("LIVE2D_LOAD_FAILED:" + ex.GetType().FullName + ":" + ex.Message);
                    Debug.LogError("[XINGSHU_OPTIONAL_LIVE2D_ERROR] Live2D driver failed: " + ex);
                }

                // ==========================================
                // OPTIONAL DRIVER 2: Spine 4.2
                // ==========================================
                try
                {
                    LogStage("SPINE_LOAD_BEGIN");
                    TextAsset spineAsset = hostBundle.LoadAsset("XingShu.spine.4.2.dll") as TextAsset;
                    if (spineAsset != null && spineAsset.bytes != null && spineAsset.bytes.Length > 0)
                    {
                        Assembly spineAsm = Assembly.Load(spineAsset.bytes);
                        if (spineAsm != null)
                        {
                            LogStage("SPINE_LOAD_OK");
                        }
                        else
                        {
                            LogStage("SPINE_LOAD_FAILED:System.Exception:Assembly.Load returned null");
                        }
                    }
                    else
                    {
                        LogStage("SPINE_LOAD_FAILED:System.Exception:Asset missing or empty");
                    }
                }
                catch (Exception ex)
                {
                    LogStage("SPINE_LOAD_FAILED:" + ex.GetType().FullName + ":" + ex.Message);
                    Debug.LogError("[XINGSHU_OPTIONAL_SPINE_ERROR] Spine driver failed: " + ex);
                }

                // ==========================================
                // OPTIONAL DRIVER 3: Dynamic Portraits
                // ==========================================
                try
                {
                    LogStage("DYNAMIC_LOAD_BEGIN");
                    TextAsset dynAsset = hostBundle.LoadAsset("XingShu.dynamic.portraits.dll") as TextAsset;
                    if (dynAsset != null && dynAsset.bytes != null && dynAsset.bytes.Length > 0)
                    {
                        Assembly dynAsm = Assembly.Load(dynAsset.bytes);
                        if (dynAsm != null)
                        {
                            LogStage("DYNAMIC_LOAD_OK");
                            Type driverType = dynAsm.GetType("XingShuRuntime.DynamicPortraitDriver");
                            if (driverType != null)
                            {
                                MethodInfo dynInstall = null;
                                MethodInfo[] methods = driverType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                                for (int i = 0; i < methods.Length; i++)
                                {
                                    if (methods[i].Name == "Install" && methods[i].GetParameters().Length == 0)
                                    {
                                        dynInstall = methods[i];
                                        break;
                                    }
                                }
                                if (dynInstall != null)
                                {
                                    LogStage("DYNAMIC_INSTALL_BEGIN");
                                    dynInstall.Invoke(null, null);
                                    LogStage("DYNAMIC_PORTRAIT_OK");
                                }
                                else
                                {
                                    LogStage("DYNAMIC_PORTRAIT_FAILED:MethodNotFound:DynamicPortraitDriver.Install");
                                }
                            }
                            else
                            {
                                LogStage("DYNAMIC_PORTRAIT_FAILED:TypeNotFound:DynamicPortraitDriver");
                            }
                        }
                        else
                        {
                            LogStage("DYNAMIC_LOAD_FAILED:System.Exception:Assembly.Load returned null");
                        }
                    }
                    else
                    {
                        LogStage("DYNAMIC_LOAD_FAILED:System.Exception:Asset missing or empty");
                    }
                }
                catch (Exception ex)
                {
                    LogStage("DYNAMIC_PORTRAIT_FAILED:" + ex.GetType().FullName + ":" + ex.Message);
                    Debug.LogError("[XINGSHU_OPTIONAL_DYNAMIC_ERROR] Dynamic portrait driver failed: " + ex);
                }

                // Append terminal marker for completed bootstrap pipeline
                LogStage("FINAL");
            }
            catch (Exception ex)
            {
                _installState = 3;
                LogStage("CORE_FAILED:" + ex.GetType().FullName + ":" + ex.Message);
                Debug.LogError("[XINGSHU_BOOT_330_ERROR] XingShu Bootstrap unhandled exception: " + ex);
            }
        }
    }
}
