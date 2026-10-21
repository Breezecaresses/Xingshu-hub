using System;
using System.Collections.Generic;
using XingShuRuntime.Core;

namespace UnityEngine {
    internal static class PlayerPrefs {
        static readonly Dictionary<string,object> values = new Dictionary<string,object>();
        internal static bool HasKey(string key) { return values.ContainsKey(key); }
        internal static int GetInt(string key,int fallback) { return HasKey(key) && values[key] is int ? (int)values[key] : fallback; }
        internal static float GetFloat(string key,float fallback) { return HasKey(key) && values[key] is float ? (float)values[key] : fallback; }
        internal static string GetString(string key,string fallback) { return HasKey(key) && values[key] is string ? (string)values[key] : fallback; }
        internal static void SetInt(string key,int value) { values[key]=value; }
        internal static void SetFloat(string key,float value) { values[key]=value; }
        internal static void SetString(string key,string value) { values[key]=value; }
        internal static void DeleteKey(string key) { values.Remove(key); }
        internal static void Save() { }
    }
}
internal static class PreferenceMigrationTests {
    static void Check(bool value,string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS "+message); }
    static void Main() {
        string oldInt=LegacyNames.Preference("XINGSHU_test_int"), oldFloat=LegacyNames.Preference("XINGSHU_test_float"), oldString=LegacyNames.Preference("xingshu_test_string");
        UnityEngine.PlayerPrefs.SetInt(oldInt,5); UnityEngine.PlayerPrefs.SetFloat(oldFloat,0.6f); UnityEngine.PlayerPrefs.SetString(oldString,"saved-mod");
        Check(XingShuPreferences.HasKey("XINGSHU_test_int") && XingShuPreferences.GetInt("XINGSHU_test_int",0)==5,"legacy integer setting is retained");
        Check(XingShuPreferences.GetFloat("XINGSHU_test_float",1f)==0.6f,"legacy widget scale is retained");
        Check(XingShuPreferences.GetString("xingshu_test_string","")=="saved-mod","legacy string setting is retained");
        XingShuPreferences.SetInt("XINGSHU_test_int",8);
        Check(XingShuPreferences.GetInt("XINGSHU_test_int",0)==8 && UnityEngine.PlayerPrefs.GetInt(oldInt,0)==5,"new key takes priority and writes use the new name");
        XingShuPreferences.DeleteKey("XINGSHU_test_int");
        Check(!UnityEngine.PlayerPrefs.HasKey(oldInt) && !XingShuPreferences.HasKey("XINGSHU_test_int"),"reset deletes both keys so the old value cannot return");
        Check(XingShuPreferences.GetString("XINGSHU_missing","fallback")=="fallback","missing settings keep defaults");
    }
}
