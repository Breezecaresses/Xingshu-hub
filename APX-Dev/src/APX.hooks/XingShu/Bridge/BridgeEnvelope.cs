using System;

namespace XingShu.Bridge
{
    /// <summary>
    /// 标准网桥 JSON 消息信封 V0.1
    /// Standard XingShu Bridge JSON Message Envelope V0.1
    /// </summary>
    public sealed class BridgeEnvelope
    {
        public string protocolVersion { get; set; } = BridgeProtocol.ProtocolVersion;
        public string messageType { get; set; }
        public string requestId { get; set; } = Guid.NewGuid().ToString();
        public long timestamp { get; set; } = DateTimeOffsetUtcNowMs();
        public object payload { get; set; }

        public static long DateTimeOffsetUtcNowMs()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        public static BridgeEnvelope Create(string messageType, object payload, string requestId = null)
        {
            return new BridgeEnvelope
            {
                protocolVersion = BridgeProtocol.ProtocolVersion,
                messageType = messageType,
                requestId = string.IsNullOrEmpty(requestId) ? Guid.NewGuid().ToString() : requestId,
                timestamp = DateTimeOffsetUtcNowMs(),
                payload = payload
            };
        }

        public static string Serialize(BridgeEnvelope envelope)
        {
            return BridgeJson.Serialize(envelope);
        }

        public string Serialize()
        {
            return BridgeJson.Serialize(this);
        }
    }
}
