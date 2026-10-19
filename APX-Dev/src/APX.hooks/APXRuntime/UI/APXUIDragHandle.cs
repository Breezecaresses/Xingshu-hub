using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace APXRuntime.UI
{
    /// <summary>
    /// 功能中心标题栏拖动手柄 (Phase 2B-R4-R2a)
    /// 遵循 uGUI 事件驱动模型，与 APXOverlayInput 遮罩协同，支持屏幕安全区 Clamp 与显式排他区门禁
    /// </summary>
    internal sealed class APXUIDragHandle : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
    {
        [SerializeField]
        internal RectTransform targetRect;

        [SerializeField]
        internal RectTransform parentRect;

        private readonly List<RectTransform> _excludedRects = new List<RectTransform>();

        private int _pointerId = int.MinValue;
        private bool _isDragging;
        private bool _dragAllowedForCurrentPointer;
        private Vector2 _dragStartLocalPos;
        private Vector2 _dragStartFramePos;

        internal void AddExcludedRect(RectTransform rect)
        {
            if (rect != null && !_excludedRects.Contains(rect))
            {
                _excludedRects.Add(rect);
            }
        }

        internal void SetExcludedRects(params RectTransform[] rects)
        {
            _excludedRects.Clear();
            if (rects != null)
            {
                for (int i = 0; i < rects.Length; i++)
                {
                    if (rects[i] != null)
                    {
                        _excludedRects.Add(rects[i]);
                    }
                }
            }
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _dragAllowedForCurrentPointer = false;
            _isDragging = false;
            _pointerId = int.MinValue;

            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (targetRect == null || parentRect == null) return;

            // 显式排他区判定: 若点击命中任一排除控件 (如 Close 按钮、F1 徽章等)，立即阻断拖动
            if (IsPointerInExcludedZone(eventData))
            {
                return;
            }

            _dragAllowedForCurrentPointer = true;
            _pointerId = eventData.pointerId;
            _isDragging = true;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, eventData.position, eventData.pressEventCamera, out _dragStartLocalPos);
            _dragStartFramePos = targetRect.anchoredPosition;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!_dragAllowedForCurrentPointer || eventData.pointerId != _pointerId)
            {
                eventData.pointerDrag = null;
                return;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragAllowedForCurrentPointer || !_isDragging || eventData.pointerId != _pointerId) return;
            if (targetRect == null || parentRect == null) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, eventData.position, eventData.pressEventCamera, out Vector2 currentLocalPos);
            Vector2 delta = currentLocalPos - _dragStartLocalPos;
            targetRect.anchoredPosition = _dragStartFramePos + delta;
            ClampPosition();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == _pointerId)
            {
                _isDragging = false;
                _dragAllowedForCurrentPointer = false;
                _pointerId = int.MinValue;
                ClampPosition();
            }
        }

        private void OnDisable()
        {
            _isDragging = false;
            _dragAllowedForCurrentPointer = false;
            _pointerId = int.MinValue;
        }

        private bool IsPointerInExcludedZone(PointerEventData eventData)
        {
            for (int i = 0; i < _excludedRects.Count; i++)
            {
                RectTransform excluded = _excludedRects[i];
                if (excluded != null && excluded.gameObject.activeInHierarchy)
                {
                    if (RectTransformUtility.RectangleContainsScreenPoint(excluded, eventData.position, eventData.pressEventCamera))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        internal void ClampPosition()
        {
            if (targetRect == null || parentRect == null) return;

            float halfCanvasWidth = parentRect.rect.width * 0.5f;
            float halfCanvasHeight = parentRect.rect.height * 0.5f;
            float halfWinWidth = targetRect.rect.width * 0.5f;
            float halfWinHeight = targetRect.rect.height * 0.5f;

            // X 轴限制: 在宽屏下保证整窗在屏内；极端窄屏下保证至少 48px Header 在屏内
            float minX = (halfCanvasWidth > halfWinWidth) ? -(halfCanvasWidth - halfWinWidth) : -(halfWinWidth - 48f);
            float maxX = (halfCanvasWidth > halfWinWidth) ? +(halfCanvasWidth - halfWinWidth) : +(halfWinWidth - 48f);

            // Y 轴限制: 保证顶部标题栏在屏内且整窗在安全视口内
            float minY = (halfCanvasHeight > halfWinHeight) ? -(halfCanvasHeight - halfWinHeight) : -(halfWinHeight - 48f);
            float maxY = (halfCanvasHeight > halfWinHeight) ? +(halfCanvasHeight - halfWinHeight) : +(halfCanvasHeight - 64f);

            Vector2 pos = targetRect.anchoredPosition;
            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
            targetRect.anchoredPosition = pos;
        }
    }
}
