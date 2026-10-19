using System;
using GameLogic;
using Tools;
using UI;
using UnityEngine;
using party.model;
using APXRuntime.Core;
using APXRuntime.Utilities;

namespace APXRuntime.Features
{
    /// <summary>
    /// 变速暂停原因枚举 (Phase 2D-2B4F 自身本回合原速简化版)
    /// 位掩码聚合暂停原因。V1 唯一起效原因为 OwnTurn；CharacterSelection 等保留为预留位
    /// </summary>
    [Flags]
    internal enum AccelerationSuspendReason
    {
        None = 0,
        CharacterSelection = 1 << 0, // V1 预留 / 延后 (非活跃挂起源)
        OwnTurn = 1 << 1,            // 自身回合活跃挂起源
        LocalActionSelection = 1 << 2, // 本地主行动/抽选阶段挂起源 (Phase 2D-8C-R6B-P1B)
        LocalDefenseInteraction = 1 << 3, // 本地防御交互阶段挂起源 (Phase 2D-8C-R6B-P1B)
        UserManualPause = 1 << 4
    }

    /// <summary>
    /// 变速挂起策略判定器 (Phase 2D-8C-R6B-P1B)
    /// 集中管理 PVE 自身回合及自身交互期间（主行动选牌、防御响应）的原速挂起、防抖与状态机。
    /// 严格只读判定，绝不直接执行 IPC 写入或篡改用户持久化配置
    /// </summary>
    internal static class APXAccelerationSuspensionPolicy
    {
        private static AccelerationSuspendReason _activeReasons = AccelerationSuspendReason.None;

        private static long _lastOwnTurnActiveTimeMs = 0;
        private static long _lastActionActiveTimeMs = 0;
        private static long _lastDefenseActiveTimeMs = 0;

        private const long OWN_TURN_EXIT_DEBOUNCE_MS = 200;

        internal static AccelerationSuspendReason ActiveReasons => _activeReasons;
        internal static bool IsSuspended => _activeReasons != AccelerationSuspendReason.None;

        private static long GetCurrentTimeMs()
        {
            return DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
        }

        /// <summary>
        /// 重置所有挂起原因与计时（用于场景切换或停机清理，彻底杜绝上一场景状态残留）
        /// </summary>
        internal static void Reset()
        {
            _activeReasons = AccelerationSuspendReason.None;
            _lastOwnTurnActiveTimeMs = 0;
            _lastActionActiveTimeMs = 0;
            _lastDefenseActiveTimeMs = 0;
        }

        /// <summary>
        /// 评估当前挂起状态 (支持 OwnTurn, LocalActionSelection, LocalDefenseInteraction)
        /// </summary>
        internal static AccelerationSuspendReason Evaluate()
        {
            long now = GetCurrentTimeMs();
            AccelerationSuspendReason raw = EvaluateRawSignals();

            // 1. 处理轮到自己出牌 (OwnTurn)
            bool rawOwnTurn = (raw & AccelerationSuspendReason.OwnTurn) != 0;
            if (rawOwnTurn)
            {
                _activeReasons |= AccelerationSuspendReason.OwnTurn;
                _lastOwnTurnActiveTimeMs = now;
            }
            else
            {
                if ((_activeReasons & AccelerationSuspendReason.OwnTurn) != 0)
                {
                    if (now - _lastOwnTurnActiveTimeMs >= OWN_TURN_EXIT_DEBOUNCE_MS)
                    {
                        _activeReasons &= ~AccelerationSuspendReason.OwnTurn;
                    }
                }
            }

            // 2. 处理本地行动/抽选阶段 (LocalActionSelection)
            bool rawAction = (raw & AccelerationSuspendReason.LocalActionSelection) != 0;
            if (rawAction)
            {
                _activeReasons |= AccelerationSuspendReason.LocalActionSelection;
                _lastActionActiveTimeMs = now;
            }
            else
            {
                if ((_activeReasons & AccelerationSuspendReason.LocalActionSelection) != 0)
                {
                    if (now - _lastActionActiveTimeMs >= OWN_TURN_EXIT_DEBOUNCE_MS)
                    {
                        _activeReasons &= ~AccelerationSuspendReason.LocalActionSelection;
                    }
                }
            }

            // 3. 处理本地防御交互阶段 (LocalDefenseInteraction)
            bool rawDefense = (raw & AccelerationSuspendReason.LocalDefenseInteraction) != 0;
            if (rawDefense)
            {
                _activeReasons |= AccelerationSuspendReason.LocalDefenseInteraction;
                _lastDefenseActiveTimeMs = now;
            }
            else
            {
                if ((_activeReasons & AccelerationSuspendReason.LocalDefenseInteraction) != 0)
                {
                    if (now - _lastDefenseActiveTimeMs >= OWN_TURN_EXIT_DEBOUNCE_MS)
                    {
                        _activeReasons &= ~AccelerationSuspendReason.LocalDefenseInteraction;
                    }
                }
            }

            const AccelerationSuspendReason ALLOWED_MASK =
                AccelerationSuspendReason.OwnTurn |
                AccelerationSuspendReason.LocalActionSelection |
                AccelerationSuspendReason.LocalDefenseInteraction;

            _activeReasons &= ALLOWED_MASK;

            return _activeReasons;
        }

        private static AccelerationSuspendReason EvaluateRawSignals()
        {
            AccelerationSuspendReason reasons = AccelerationSuspendReason.None;

            if (CheckOwnTurnSignal())
            {
                reasons |= AccelerationSuspendReason.OwnTurn;
            }

            if (CheckLocalActionSelectionSignal())
            {
                reasons |= AccelerationSuspendReason.LocalActionSelection;
            }

            if (CheckLocalDefenseInteractionSignal())
            {
                reasons |= AccelerationSuspendReason.LocalDefenseInteraction;
            }

            return reasons;
        }

        private static bool CheckOwnTurnSignal()
        {
            try
            {
                BattleLogic battle = SimpleSingletonProvider<GameLogicManager>.inst?.battle;
                if (battle == null || battle.PlayerDatas == null || battle.PlayerDatas.Count == 0)
                {
                    return false;
                }

                RoomInfo roomInfo = SimpleSingletonProvider<GameLogicManager>.inst?.room?.curRoomInfo;
                if (!HandCardDisplayManager.IsBattleRunning(roomInfo))
                {
                    return false;
                }

                if (!HandCardDisplayManager.IsPVEMode())
                {
                    return false;
                }

                long curId = PveTurnContext.CurrentPlayerId();
                if (curId <= 0)
                {
                    return false;
                }

                var account = SimpleSingletonProvider<GameLogicManager>.inst?.account;
                if (account != null && account.IsSelf(curId))
                {
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private static bool CheckLocalActionSelectionSignal()
        {
            try
            {
                if (!HandCardDisplayManager.IsPVEMode())
                {
                    return false;
                }

                RoomInfo roomInfo = SimpleSingletonProvider<GameLogicManager>.inst?.room?.curRoomInfo;
                if (!HandCardDisplayManager.IsBattleRunning(roomInfo))
                {
                    return false;
                }

                ActionLogic action = SimpleSingletonProvider<GameLogicManager>.inst?.action;
                if (action != null && action.playerAction != PlayerActionEnum.NONE)
                {
                    return true;
                }

                ChooseRoundCardWindow chooseCardWin = SimpleSingletonProvider<UIManager>.inst?.ChooseRoundCard;
                if (chooseCardWin != null && chooseCardWin.isShowing)
                {
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private static bool CheckLocalDefenseInteractionSignal()
        {
            try
            {
                if (!HandCardDisplayManager.IsPVEMode())
                {
                    return false;
                }

                RoomInfo roomInfo = SimpleSingletonProvider<GameLogicManager>.inst?.room?.curRoomInfo;
                if (!HandCardDisplayManager.IsBattleRunning(roomInfo))
                {
                    return false;
                }

                var account = SimpleSingletonProvider<GameLogicManager>.inst?.account;
                if (account == null)
                {
                    return false;
                }

                FightLogic fight = SimpleSingletonProvider<GameLogicManager>.inst?.fight;
                FightWindow fightWin = SimpleSingletonProvider<UIManager>.inst?.Fight;

                bool fightActive = (fight != null && fight.fightStatus) || (fightWin != null && fightWin.isShowing);
                if (!fightActive)
                {
                    return false;
                }

                long defenderId = 0;
                if (fight?.defendData != null && fight.defendData.PlayerId > 0)
                {
                    defenderId = fight.defendData.PlayerId;
                }
                else if (fight?.battleFightData?.defenderInfo != null && fight.battleFightData.defenderInfo.PlayerId > 0)
                {
                    defenderId = fight.battleFightData.defenderInfo.PlayerId;
                }

                if (defenderId > 0 && account.IsSelf(defenderId))
                {
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }
    }
}
