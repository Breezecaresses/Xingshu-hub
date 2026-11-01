using System;
using System.Collections.Generic;
using UnityEngine;

namespace XingShuRuntime.Utilities
{
    // Pixel coordinates, with the origin at the top left, just like FairyGUI.
    // Each row gets its own bounds so the space between players stays clickable.
    internal struct HandRosterPlan
    {
        internal bool Valid;
        internal Rect Bounds;
        internal float Unit, Cell, Gap, ToggleWidth, ToggleHeight, GridX, GridY, GridWidth, Step, ContentWidth, MaxOffset;
        internal int Count;
    }

    internal static class HandRosterLayout
    {
        internal static HandRosterPlan Build(Rect head, Rect safe, float lane, int items, float size, bool expanded, bool mobile)
        {
            var result = new HandRosterPlan();
            if (!Finite(head.x) || !Finite(head.y) || !Finite(head.width) || !Finite(head.height) ||
                !Finite(safe.x) || !Finite(safe.y) || !Finite(safe.width) || !Finite(safe.height) ||
                head.width <= 0 || head.height <= 0 || safe.width < 240 || safe.height < 160) return result;
            float unit = Clamp(Math.Min(safe.width / 1600f, safe.height / 900f), .55f, 2f);
            // Native panels briefly scale almost to zero during battle transitions.
            if (head.width < 64 * unit || head.height < 24 * unit) return result;
            float gap = 4 * unit, toggleWidth = (mobile ? 56 : 48) * unit, toggleHeight = (mobile ? 48 : 40) * unit;
            float x = Math.Max(safe.xMin + gap, head.xMax + 6 * unit);
            // Leave the right-hand action controls and the player's own hand unobstructed.
            float right = Math.Min(safe.xMax - gap, safe.xMin + safe.width * .64f);
            if (x + toggleWidth > right) return result;
            if (!Finite(size)) size = 1;
            float cell = 58 * unit * Clamp(size, .6f, 1.5f);
            if (!Finite(lane) || lane < 1) lane = head.height;
            float maxHeight = Math.Min(safe.height, Math.Max(toggleHeight, lane - 8 * unit));
            cell = Math.Min(cell, Math.Max(24 * unit, maxHeight - 2 * gap));
            float available = right - x - toggleWidth - gap - 2 * gap;
            if (expanded && available < cell) return result;
            int count = Math.Max(1, items);
            float viewport = Math.Max(cell, Math.Min(available, 6 * cell + 5 * gap));
            float step = cell + gap;
            if (count > 1 && cell + (count - 1) * step > viewport)
                step = Math.Max(cell * .72f, (viewport - cell) / (count - 1));
            float contentWidth = cell + (count - 1) * step;
            float gridWidth = Math.Min(contentWidth, viewport) + 2 * gap;
            float height = expanded ? Math.Max(toggleHeight, cell + 2 * gap) : toggleHeight;
            float width = expanded ? toggleWidth + gap + gridWidth : toggleWidth;
            float y = Clamp(head.center.y - height * .5f, safe.yMin, safe.yMax - height);
            result.Valid = true;
            result.Bounds = new Rect(x, y, width, height);
            result.Unit = unit; result.Cell = cell; result.Gap = gap;
            result.ToggleWidth = toggleWidth; result.ToggleHeight = toggleHeight;
            result.GridX = toggleWidth + gap; result.GridY = (height - cell - 2 * gap) * .5f;
            result.GridWidth = gridWidth; result.Step = step; result.Count = count;
            result.ContentWidth = contentWidth; result.MaxOffset = Math.Max(0, contentWidth - viewport);
            return result;
        }

        internal static float CardX(HandRosterPlan plan, int index, float offset) => plan.Gap + index * plan.Step - Clamp(offset, 0, plan.MaxOffset);

        internal static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    // Expansion belongs to the player, not their current ranking/row or the old dock.
    internal sealed class HandRosterState
    {
        private string _session;
        private readonly HashSet<long> _expanded = new HashSet<long>();
        private readonly Dictionary<long, float> _offsets = new Dictionary<long, float>();
        internal bool BeginSession(string session)
        {
            if (_session == session) return false;
            _session = session; _expanded.Clear(); _offsets.Clear(); return true;
        }
        internal bool IsExpanded(long player) => player > 0 && _expanded.Contains(player);
        internal bool Toggle(long player)
        {
            if (player <= 0) return false;
            _offsets.Remove(player);
            if (_expanded.Remove(player)) return false;
            _expanded.Add(player); return true;
        }
        internal float Offset(long player, float maximum)
        {
            float offset; _offsets.TryGetValue(player, out offset);
            offset = HandRosterLayout.Clamp(offset, 0, Math.Max(0, maximum));
            _offsets[player] = offset; return offset;
        }
        internal void Scroll(long player, float delta, float maximum)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return;
            _offsets[player] = HandRosterLayout.Clamp(Offset(player, maximum) + delta, 0, Math.Max(0, maximum));
        }
    }
}
