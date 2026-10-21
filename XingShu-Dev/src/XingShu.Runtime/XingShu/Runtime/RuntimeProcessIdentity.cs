using System;
using System.IO;

namespace XingShu.Runtime
{
    // Android IL2CPP strips System.Diagnostics.Process; even a try/catch around
    // GetCurrentProcess can fail during method loading before the catch executes.
    internal static class RuntimeProcessIdentity
    {
        internal static int CurrentId
        {
            get
            {
                try
                {
#if XINGSHU_ANDROID_PREVIEW
                    string stat = File.ReadAllText("/proc/self/stat");
                    int end = stat.IndexOf(' ');
                    int pid;
                    return end > 0 && int.TryParse(stat.Substring(0, end), out pid) ? pid : 0;
#else
                    using (var process = System.Diagnostics.Process.GetCurrentProcess())
                        return process.Id;
#endif
                }
                catch { return 0; }
            }
        }

        internal static string CurrentName
        {
            get
            {
                try
                {
#if XINGSHU_ANDROID_PREVIEW
                    string command = File.ReadAllText("/proc/self/cmdline");
                    int end = command.IndexOf('\0');
                    return end >= 0 ? command.Substring(0, end) : command;
#else
                    using (var process = System.Diagnostics.Process.GetCurrentProcess())
                        return process.ProcessName;
#endif
                }
                catch { return null; }
            }
        }
    }
}
