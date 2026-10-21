using System;
using UnityEngine;
using UnityEngine.EventSystems;
using XingShuRuntime.Core;

namespace XingShuRuntime.UI
{
    /// <summary>
    /// InputField 文本输入上下文桥接器 (Phase 2C-1A)
    /// 确保 UGUI InputField 在获得/失去焦点以及被禁用时，精确与 XingShuCentralInputManager 的 TextEntry 门禁联动
    /// </summary>
    internal sealed class XingShuUITextEntryBridge : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        internal string ContextId { get; set; } = "";

        private bool _hasActiveEntry;

        public void OnSelect(BaseEventData eventData)
        {
            Acquire();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            Release();
        }

        private void OnDisable()
        {
            Release();
        }

        private void OnDestroy()
        {
            Release();
        }

        internal void Acquire()
        {
            if (!string.IsNullOrEmpty(ContextId) && !_hasActiveEntry)
            {
                _hasActiveEntry = true;
                XingShuCentralInputManager.BeginTextEntry(ContextId);
            }
        }

        internal void Release()
        {
            if (_hasActiveEntry && !string.IsNullOrEmpty(ContextId))
            {
                _hasActiveEntry = false;
                XingShuCentralInputManager.EndTextEntry(ContextId);
            }
        }
    }
}
