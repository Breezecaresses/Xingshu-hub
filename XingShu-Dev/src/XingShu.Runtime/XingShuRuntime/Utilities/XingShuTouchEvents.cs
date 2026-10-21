using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace XingShuRuntime.Utilities
{
    // Built-in EventTrigger dispatch survives the HybridCLR/AOT boundary on
    // clients which cannot discover interpreted IEventSystemHandler interfaces.
    internal static class XingShuTouchEvents
    {
        internal static void Add(GameObject target, EventTriggerType type, UnityAction<BaseEventData> callback)
        {
            var trigger = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(callback);
            trigger.triggers.Add(entry);
        }
    }
}
