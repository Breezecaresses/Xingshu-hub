using System.Collections;
using System.Reflection;
using UI;

namespace XingShuRuntime.Utilities;

/// <summary>Observe windows that the game has already shown; never invoke lazy window factories.</summary>
internal static class ShownNativeWindows
{
    // The native Stack<T> is emitted against a different Unity System facade.
    // Enumerate its public field without binding that facade into the plugin.
    private static readonly FieldInfo Windows = typeof(UIManager).GetField("propUpWindows", BindingFlags.Instance | BindingFlags.Public);
    internal static T Find<T>(UIManager manager) where T : BaseWindow
    {
        if (manager == null) return null;
        var windows = Windows?.GetValue(manager) as IEnumerable;
        if (windows == null) return null;
        foreach (var item in windows)
            if (item is T window && !window.isDisposed && window.isShowing && window.contentPane != null)
                return window;
        return null;
    }
}
