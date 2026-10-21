using System;
using UnityEngine;

namespace XingShuRuntime.Core;

// Old names are restricted to upgrade readers, never used for new files or UI.
internal static class LegacyNames
{
    internal const string NativeIpcDirectory = "APXNativeSpeed";
    internal static string Preference(string key)
    {
        if (key.StartsWith("XINGSHU_", StringComparison.Ordinal)) return "APX_" + key.Substring(8);
        if (key.StartsWith("xingshu_", StringComparison.Ordinal)) return "apx_" + key.Substring(8);
        return key;
    }
}

internal static class XingShuPreferences
{
    private static string ReadKey(string key)
    {
        if (PlayerPrefs.HasKey(key)) return key;
        string old = LegacyNames.Preference(key);
        return old != key && PlayerPrefs.HasKey(old) ? old : key;
    }
    internal static bool HasKey(string key) { return PlayerPrefs.HasKey(ReadKey(key)); }
    internal static int GetInt(string key, int fallback) { return PlayerPrefs.GetInt(ReadKey(key), fallback); }
    internal static float GetFloat(string key, float fallback) { return PlayerPrefs.GetFloat(ReadKey(key), fallback); }
    internal static string GetString(string key, string fallback) { return PlayerPrefs.GetString(ReadKey(key), fallback); }
    internal static void SetInt(string key, int value) { PlayerPrefs.SetInt(key, value); }
    internal static void SetFloat(string key, float value) { PlayerPrefs.SetFloat(key, value); }
    internal static void SetString(string key, string value) { PlayerPrefs.SetString(key, value); }
    internal static void DeleteKey(string key) { PlayerPrefs.DeleteKey(key); PlayerPrefs.DeleteKey(LegacyNames.Preference(key)); }
    internal static void Save() { PlayerPrefs.Save(); }
}
