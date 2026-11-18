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
        internal float Unit, Cell, Gap, ToggleWidth, ToggleHeight, ToggleX, ToggleY, GridX, GridY, GridWidth, Step, ContentWidth, MaxOffset;
        internal int Count;
    }

    internal static class HandRosterLayout
    {
        internal static HandRosterPlan Build(Rect head, Rect safe, float lane, int items, float size, bool expanded, bool mobile,
            float toggleOffsetX = 0f, float toggleOffsetY = 0f, float gridOffsetX = 0f, float gridOffsetY = 0f)
        {
            var result = new HandRosterPlan();
            if (!Finite(head.x) || !Finite(head.y) || !Finite(head.width) || !Finite(head.height) ||
                !Finite(safe.x) || !Finite(safe.y) || !Finite(safe.width) || !Finite(safe.height) ||
                head.width <= 0 || head.height <= 0 || safe.width < 240 || safe.height < 160) return result;
            float unit = Clamp(Math.Min(safe.width / 1600f, safe.height / 900f), .55f, 2f);
            // Native panels briefly scale almost to zero during battle transitions.
            if (head.width < 64 * unit || head.height < 24 * unit) return result;
            float gap = 4 * unit, toggleWidth = (mobile ? 40 : 34) * unit, toggleHeight = (mobile ? 58 : 50) * unit;
            if (!Finite(toggleOffsetX)) toggleOffsetX = 0f;
            if (!Finite(toggleOffsetY)) toggleOffsetY = 0f;
            if (!Finite(gridOffsetX)) gridOffsetX = 0f;
            if (!Finite(gridOffsetY)) gridOffsetY = 0f;
            float baseLeft = Math.Max(safe.xMin + gap, head.xMin - 6 * unit - toggleWidth);
            // Leave the right-hand action controls and the player's own hand unobstructed.
            float right = Math.Min(safe.xMax - gap, safe.xMin + safe.width * .64f);
            float toggleX = Clamp(baseLeft + toggleOffsetX * unit, safe.xMin + gap, Math.Max(safe.xMin + gap, right - toggleWidth));
            if (toggleX + toggleWidth > right) return result;
            float baseGridLeft = baseLeft + toggleWidth + gap;
            if (!Finite(size)) size = 1;
            float cell = 58 * unit * Clamp(size, .6f, 1.5f);
            if (!Finite(lane) || lane < 1) lane = head.height;
            float maxHeight = Math.Min(safe.height, Math.Max(toggleHeight, lane - 8 * unit));
            cell = Math.Min(cell, Math.Max(24 * unit, maxHeight - 2 * gap));
            if (right - baseGridLeft - 2 * gap < cell) return result;
            float gridLeft = Clamp(baseGridLeft + gridOffsetX * unit, safe.xMin + gap, right - cell - 2 * gap);
            float available = right - gridLeft - 2 * gap;
            if (available < cell) return result;
            int count = Math.Max(1, items);
            float viewport = Math.Max(cell, Math.Min(available, 6 * cell + 5 * gap));
            float step = cell + gap;
            if (count > 1 && cell + (count - 1) * step > viewport)
                step = Math.Max(cell * .72f, (viewport - cell) / (count - 1));
            float contentWidth = cell + (count - 1) * step;
            float gridWidth = Math.Min(contentWidth, viewport) + 2 * gap;
            float gridHeight = cell + 2 * gap;
            float toggleY = Clamp(head.center.y - toggleHeight * .5f + toggleOffsetY * unit, safe.yMin, safe.yMax - toggleHeight);
            float gridY = Clamp(head.center.y - gridHeight * .5f + gridOffsetY * unit, safe.yMin, safe.yMax - gridHeight);
            float minX = expanded ? Math.Min(toggleX, gridLeft) : toggleX;
            float maxX = expanded ? Math.Max(toggleX + toggleWidth, gridLeft + gridWidth) : toggleX + toggleWidth;
            float minY = expanded ? Math.Min(toggleY, gridY) : toggleY;
            float maxY = expanded ? Math.Max(toggleY + toggleHeight, gridY + gridHeight) : toggleY + toggleHeight;
            result.Valid = true;
            result.Bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
            result.Unit = unit; result.Cell = cell; result.Gap = gap;
            result.ToggleWidth = toggleWidth; result.ToggleHeight = toggleHeight;
            result.ToggleX = toggleX - minX; result.ToggleY = toggleY - minY;
            result.GridX = gridLeft - minX; result.GridY = gridY - minY;
            result.GridWidth = gridWidth; result.Step = step; result.Count = count;
            result.ContentWidth = contentWidth; result.MaxOffset = Math.Max(0, contentWidth - viewport);
            return result;
        }

        internal static float CardX(HandRosterPlan plan, int index, float offset) => plan.Gap + index * plan.Step - Clamp(offset, 0, plan.MaxOffset);

        internal static Rect StabilizeAnchor(Rect candidate, Rect baseline, Rect safe, Rect lastSafe, float lane, float unit)
        {
            if (!FiniteRect(candidate) || candidate.width <= 0 || candidate.height <= 0)
                return FiniteRect(baseline) && baseline.width > 0 && baseline.height > 0 ? baseline : new Rect();
            if (!FiniteRect(baseline) || baseline.width <= 0 || baseline.height <= 0 || SafeChanged(safe, lastSafe))
                return candidate;
            float safeUnit = Finite(unit) && unit > 0 ? unit : 1f;
            float safeLane = Finite(lane) && lane > 0 ? lane : baseline.height;
            float yThreshold = Math.Max(16f * safeUnit, safeLane * .35f);
            float xThreshold = 24f * safeUnit;
            if (Math.Abs(candidate.center.y - baseline.center.y) >= yThreshold ||
                Math.Abs(candidate.x - baseline.x) >= xThreshold)
                return candidate;
            if (candidate.width > baseline.width + 8f * safeUnit ||
                candidate.height > baseline.height + 8f * safeUnit)
                return candidate;
            return baseline;
        }

        internal static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool FiniteRect(Rect r) => Finite(r.x) && Finite(r.y) && Finite(r.width) && Finite(r.height);
        private static bool SafeChanged(Rect a, Rect b) =>
            !FiniteRect(b) || b.width <= 0 || b.height <= 0 ||
            Math.Abs(a.x - b.x) > 1f || Math.Abs(a.y - b.y) > 1f ||
            Math.Abs(a.width - b.width) > 1f || Math.Abs(a.height - b.height) > 1f;
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
