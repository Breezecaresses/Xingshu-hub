using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Core.Scene;
using Core.Unit;
using GameLogic;
using party.model;
using UnityEngine;

namespace XingShuRuntime.Targeting
{
    /// <summary>
    /// Pure idempotent scale calculation for monster ground movement arrows.
    /// Keeps rotation/forward/position completely untouched.
    /// </summary>
    internal static class MonsterWalkArrowScaleMath
    {
        internal const float DefaultBoostFactor = 2.2f;
        internal const float MinBoostFactor = 1.0f;
        internal const float MaxBoostFactor = 3.0f;
        private const float ScaleEpsilon = 0.001f;

        internal static float ClampFactor(float factor)
        {
            if (float.IsNaN(factor) || float.IsInfinity(factor)) return DefaultBoostFactor;
            if (factor < MinBoostFactor) return MinBoostFactor;
            if (factor > MaxBoostFactor) return MaxBoostFactor;
            return factor;
        }

        internal static bool Approximately(Vector3 a, Vector3 b)
        {
            return Math.Abs(a.x - b.x) <= ScaleEpsilon &&
                   Math.Abs(a.y - b.y) <= ScaleEpsilon &&
                   Math.Abs(a.z - b.z) <= ScaleEpsilon;
        }

        internal static bool IsValidPositiveScale(Vector3 scale)
        {
            return !float.IsNaN(scale.x) && !float.IsInfinity(scale.x) && scale.x > ScaleEpsilon &&
                   !float.IsNaN(scale.y) && !float.IsInfinity(scale.y) && scale.y > ScaleEpsilon &&
                   !float.IsNaN(scale.z) && !float.IsInfinity(scale.z) && scale.z > ScaleEpsilon;
        }

        internal static void ComputeBoostedScale(
            Vector3 currentScale,
            bool hasTracked,
            Vector3 trackedBaseScale,
            Vector3 trackedAppliedScale,
            float factor,
            out Vector3 baseScale,
            out Vector3 appliedScale,
            out bool changed)
        {
            float clamped = ClampFactor(factor);
            Vector3 safeCurrent = IsValidPositiveScale(currentScale) ? currentScale : Vector3.one;

            if (hasTracked && IsValidPositiveScale(trackedBaseScale) && Approximately(safeCurrent, trackedAppliedScale))
            {
                baseScale = trackedBaseScale;
                appliedScale = trackedAppliedScale;
                changed = false;
                return;
            }

            baseScale = safeCurrent;
            appliedScale = new Vector3(
                baseScale.x * clamped,
                baseScale.y * clamped,
                baseScale.z * clamped);
            changed = !Approximately(safeCurrent, appliedScale);
        }

        internal static bool TryComputeRestoreScale(
            Vector3 currentScale,
            Vector3 trackedBaseScale,
            Vector3 trackedAppliedScale,
            out Vector3 restoreScale)
        {
            if (IsValidPositiveScale(trackedBaseScale) && Approximately(currentScale, trackedAppliedScale))
            {
                restoreScale = trackedBaseScale;
                return true;
            }

            restoreScale = currentScale;
            return false;
        }
    }

    /// <summary>
    /// Boosts the visual scale of active hostile monsters' ground movement-path arrows
    /// (_walkDirections) during PVE Battle without altering transform.forward or position.
    /// </summary>
    internal sealed class MonsterWalkArrowBoost
    {
        private struct TrackedArrow
        {
            public Component Effect;
            public Vector3 BaseScale;
            public Vector3 AppliedScale;
        }

        private static readonly FieldInfo CharacterMovement = typeof(global::Core.Unit.Character).GetField(
            "move", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo WalkDirections = typeof(CharacterMove).GetField(
            "_walkDirections", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly Dictionary<int, TrackedArrow> _tracked = new Dictionary<int, TrackedArrow>();
        private readonly HashSet<int> _activeIds = new HashSet<int>();
        private readonly List<int> _removeIds = new List<int>();

        internal void Sync(bool enabled, GameLogicManager logic, SceneType sceneType, bool isPve, float factor = MonsterWalkArrowScaleMath.DefaultBoostFactor)
        {
            _activeIds.Clear();

            if (enabled && isPve && sceneType == SceneType.Battle &&
                CharacterMovement != null && WalkDirections != null &&
                logic?.room?.curRoomInfo != null && logic.battle?.PlayerDatas != null)
            {
                var players = logic.battle.PlayerDatas;
                for (int i = 0; i < players.Count; i++)
                {
                    var monster = players[i];
                    if (monster?.player == null ||
                        monster.characterType != CharacterType.Monster ||
                        monster.player.Hero == null ||
                        monster.Property?.HP == null ||
                        monster.Property.HP.Value <= 0 ||
                        monster.player.Hero.MonsterType == Hero.Types.MonsterType.PveAllyMonster)
                    {
                        continue;
                    }

                    var character = monster.CharacterInst;
                    if (character == null) continue;

                    var movement = CharacterMovement.GetValue(character);
                    if (movement == null) continue;

                    var drawn = WalkDirections.GetValue(movement) as IList;
                    if (drawn == null || drawn.Count == 0) continue;

                    for (int j = 0; j < drawn.Count; j++)
                    {
                        var effect = drawn[j] as Component;
                        if (effect == null || !effect.gameObject.activeInHierarchy) continue;

                        int id = effect.GetInstanceID();
                        _activeIds.Add(id);

                        TrackedArrow existing;
                        bool hasTracked = _tracked.TryGetValue(id, out existing);
                        Vector3 currentScale = effect.transform.localScale;
                        Vector3 baseScale;
                        Vector3 appliedScale;
                        bool changed;

                        MonsterWalkArrowScaleMath.ComputeBoostedScale(
                            currentScale,
                            hasTracked,
                            existing.BaseScale,
                            existing.AppliedScale,
                            factor,
                            out baseScale,
                            out appliedScale,
                            out changed);

                        if (changed)
                        {
                            effect.transform.localScale = appliedScale;
                        }

                        _tracked[id] = new TrackedArrow
                        {
                            Effect = effect,
                            BaseScale = baseScale,
                            AppliedScale = appliedScale
                        };
                    }
                }
            }

            if (_tracked.Count == 0) return;

            _removeIds.Clear();
            foreach (var pair in _tracked)
            {
                if (_activeIds.Contains(pair.Key)) continue;

                var entry = pair.Value;
                if (entry.Effect != null && entry.Effect.gameObject.activeInHierarchy)
                {
                    Vector3 restoreScale;
                    if (MonsterWalkArrowScaleMath.TryComputeRestoreScale(
                        entry.Effect.transform.localScale,
                        entry.BaseScale,
                        entry.AppliedScale,
                        out restoreScale))
                    {
                        entry.Effect.transform.localScale = restoreScale;
                    }
                }

                _removeIds.Add(pair.Key);
            }

            for (int i = 0; i < _removeIds.Count; i++)
            {
                _tracked.Remove(_removeIds[i]);
            }
            _removeIds.Clear();
        }

        internal void RestoreAll()
        {
            if (_tracked.Count == 0) return;
            Sync(false, null, SceneType.None, false);
        }
    }
}
