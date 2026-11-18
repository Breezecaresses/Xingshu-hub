using System;
using System.Collections.Generic;
using System.IO;

namespace XingShuRuntime.MatchPort
{
    /// <summary>
    /// Pure path, filename, PNG validation, and once-per-match deduplication policy
    /// for automatic settlement match-result screenshots.
    /// </summary>
    internal static class SettlementScreenshotPolicy
    {
        internal const float SettleDelaySeconds = 1.2f;
        private const int MaxTrackedKeys = 64;
        private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        internal static string ResolveOutputFolder(
            string picturesFolder,
            string persistentDataPath,
            string temporaryCachePath,
            bool isAndroidPreview)
        {
            if (!isAndroidPreview)
            {
                if (!string.IsNullOrEmpty(picturesFolder))
                {
                    return Path.Combine(picturesFolder, "XingShu", "Settlements");
                }
                if (!string.IsNullOrEmpty(persistentDataPath))
                {
                    return Path.Combine(persistentDataPath, "XingShu", "Settlements");
                }
                return string.IsNullOrEmpty(temporaryCachePath)
                    ? null
                    : Path.Combine(temporaryCachePath, "XingShu", "Settlements");
            }

            if (!string.IsNullOrEmpty(persistentDataPath))
            {
                return Path.Combine(persistentDataPath, "XingShu", "Settlements");
            }
            return string.IsNullOrEmpty(temporaryCachePath)
                ? null
                : Path.Combine(temporaryCachePath, "XingShu", "Settlements");
        }

        internal static string BuildMatchKey(long roomId, long startTime, int singlePlayerEpoch)
        {
            if (roomId > 0)
            {
                return roomId + "/" + startTime;
            }
            return "single/" + Math.Max(0, singlePlayerEpoch);
        }

        internal static string BuildFileName(DateTime timestamp, long roomId)
        {
            long safeRoomId = roomId > 0 ? roomId : 0L;
            return string.Format(
                "XingShu_Settlement_{0:yyyyMMdd_HHmmss}_Room{1}.png",
                timestamp,
                safeRoomId);
        }

        internal static bool IsValidPng(byte[] bytes)
        {
            if (bytes == null || bytes.Length < PngMagic.Length) return false;
            for (int i = 0; i < PngMagic.Length; i++)
            {
                if (bytes[i] != PngMagic[i]) return false;
            }
            return true;
        }

        internal sealed class CaptureTracker
        {
            private readonly HashSet<string> _captured = new HashSet<string>(StringComparer.Ordinal);
            private readonly Queue<string> _order = new Queue<string>();

            internal bool HasCaptured(string key)
            {
                return !string.IsNullOrEmpty(key) && _captured.Contains(key);
            }

            internal void MarkCaptured(string key)
            {
                if (string.IsNullOrEmpty(key) || !_captured.Add(key)) return;
                _order.Enqueue(key);
                while (_order.Count > MaxTrackedKeys)
                {
                    string oldest = _order.Dequeue();
                    _captured.Remove(oldest);
                }
            }

            internal void Clear()
            {
                _captured.Clear();
                _order.Clear();
            }
        }
    }
}
