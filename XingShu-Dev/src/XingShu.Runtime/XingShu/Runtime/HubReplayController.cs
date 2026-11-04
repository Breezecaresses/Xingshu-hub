using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic;
using Core.Scene;
using Tools;
using UI;
using XingShu.Bridge;
using XingShuRuntime.Utilities;

namespace XingShu.Runtime
{
    /// <summary>One user-requested replay in the selected game's lobby. Never cancels a match or room.</summary>
    internal static class HubReplayController
    {
        private static bool _busy;

        private static bool InLobby(GameLogicManager logic, long playerId)
        {
            return logic != null && logic.account != null && logic.account.online &&
                logic.account.GetPlayerID() == playerId && SimpleSingletonProvider<SceneManager>.hasInstance &&
                SimpleSingletonProvider<SceneManager>.inst.currentType != null &&
                SimpleSingletonProvider<SceneManager>.inst.currentType.Value == SceneType.Home &&
                (logic.room == null || logic.room.roomController == null ||
                 logic.room.roomController.roomStateType == RoomStateType.NONE ||
                 logic.room.roomController.roomStateType == RoomStateType.WAIT);
        }

        private static void Report(string requestId, string status, string message)
        {
            XingShuBridgeClient.Instance.SendEnvelope("GAME_REPLAY_STATUS", new { requestId, status, message });
        }

        internal static async void Execute(BridgeEnvelope envelope)
        {
            bool accepted = false;
            var data = envelope == null ? null : envelope.payload as Dictionary<string, object>;
            object value;
            string requestId = data != null && data.TryGetValue("requestId", out value) ? Convert.ToString(value) : null;
            if (string.IsNullOrEmpty(requestId) || requestId.Length > 128) return;
            try
            {
                string replayId = data.TryGetValue("replayId", out value) ? Convert.ToString(value) : "";
                long playerId = data.TryGetValue("playerId", out value) ? Convert.ToInt64(value) : 0;
                if (string.IsNullOrEmpty(replayId) || replayId.Length > 30 || replayId.Any(c => c < '0' || c > '9') || playerId <= 0)
                { Report(requestId, "error", "回放码格式无效，请输入数字回放码。"); return; }
                if (_busy || ReplayPlayerGUI.IsBusy)
                { Report(requestId, "error", "游戏正在读取另一份回放，请稍后重试。"); return; }
                var logic = SimpleSingletonProvider<GameLogicManager>.hasInstance ? SimpleSingletonProvider<GameLogicManager>.inst : null;
                if (!XingShuBridgeClient.Instance.IsHandshakeComplete || !InLobby(logic, playerId) || logic.replay == null)
                { Report(requestId, "error", "请先结束当前对局并返回所选账号的游戏大厅。"); return; }
                if (logic.match != null && !logic.match.CheckOperateForMatch())
                { Report(requestId, "error", "请先在游戏内取消匹配，再播放回放。"); return; }
                _busy = true;
                accepted = true;
                Report(requestId, "loading", "游戏正在加载官方回放文件…");
                var (bytes, status) = await logic.replay.TryLoadReplayFile(replayId, logic.replay.IsReplayCached(replayId));
                if (!InLobby(logic, playerId) || (logic.match != null && !logic.match.CheckOperateForMatch()))
                { Report(requestId, "error", "游戏状态已变化，已停止回放请求。请返回大厅后重试。"); return; }
                if (status != ReplayLogic.ReplayLoadStatus.Success || bytes == null || bytes.Length == 0 || bytes.Length > 32 * 1024 * 1024)
                { Report(requestId, "error", "游戏未能加载此回放，可能已过期或与当前版本不兼容。"); return; }
                if (!logic.replay.Session.LoadBytesAndRebuildRoom(bytes))
                { Report(requestId, "error", "游戏无法解析此回放，请核对回放码和游戏版本。"); return; }
                await logic.replay.StartPlaybackAsync();
                XingShuRuntime.UI.XingShuUICenter.Close();
                Report(requestId, "ready", "已在所选游戏中开始播放回放。");
            }
            catch (Exception)
            { Report(requestId, "error", "游戏回放启动未完成，请返回大厅后重试。"); }
            finally { if (accepted) _busy = false; }
        }
    }
}
