using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using XingShu.Spectator;

namespace XingShu.Bridge
{
    /// <summary>
    /// 针对星枢网桥通讯的零依赖、线程安全、高性能 JSON 序列化与反序列化器 (.NET 4.0 兼容)
    /// </summary>
    public static class BridgeJson
    {
        // ==========================================
        // 序列化
        // ==========================================

        public static string Serialize(object obj)
        {
            if (obj == null) return "null";
            StringBuilder sb = new StringBuilder(256);
            SerializeValue(obj, sb);
            return sb.ToString();
        }

        private static void SerializeValue(object val, StringBuilder sb)
        {
            if (val == null)
            {
                sb.Append("null");
                return;
            }

            if (val is string str)
            {
                EscapeString(str, sb);
                return;
            }

            if (val is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }

            if (val is byte || val is sbyte || val is short || val is ushort ||
                val is int || val is uint || val is long || val is ulong)
            {
                sb.Append(Convert.ToString(val, CultureInfo.InvariantCulture));
                return;
            }

            if (val is float f)
            {
                sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            if (val is double d)
            {
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            if (val is decimal dec)
            {
                sb.Append(dec.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (val is BridgeEnvelope env)
            {
                sb.Append("{\"protocolVersion\":");
                EscapeString(env.protocolVersion ?? BridgeProtocol.ProtocolVersion, sb);
                sb.Append(",\"messageType\":");
                EscapeString(env.messageType ?? string.Empty, sb);
                sb.Append(",\"requestId\":");
                EscapeString(env.requestId ?? string.Empty, sb);
                sb.Append(",\"timestamp\":");
                sb.Append(env.timestamp.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"payload\":");
                SerializeValue(env.payload, sb);
                sb.Append("}");
                return;
            }

            if (val is HelloPayload hello)
            {
                sb.Append("{\"component\":");
                EscapeString(hello.component ?? "XingShu_Runtime", sb);
                sb.Append(",\"runtimeVersion\":");
                EscapeString(hello.runtimeVersion ?? string.Empty, sb);
                sb.Append(",\"gamePid\":");
                sb.Append(hello.gamePid.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"gameVersion\":");
                EscapeString(hello.gameVersion ?? string.Empty, sb);
                sb.Append(",\"capabilities\":");
                SerializeValue(hello.capabilities, sb);
                if (hello.sessionChallenge != null)
                {
                    sb.Append(",\"sessionChallenge\":");
                    EscapeString(hello.sessionChallenge, sb);
                }
                sb.Append("}");
                return;
            }

            if (val is HelloAckPayload helloAck)
            {
                sb.Append("{\"status\":");
                EscapeString(helloAck.status ?? "ACCEPTED", sb);
                sb.Append(",\"hubVersion\":");
                EscapeString(helloAck.hubVersion ?? string.Empty, sb);
                sb.Append(",\"protocolVersion\":");
                EscapeString(helloAck.protocolVersion ?? BridgeProtocol.ProtocolVersion, sb);
                sb.Append(",\"heartbeatIntervalMs\":");
                sb.Append(helloAck.heartbeatIntervalMs.ToString(CultureInfo.InvariantCulture));
                if (helloAck.sessionChallenge != null)
                {
                    sb.Append(",\"sessionChallenge\":");
                    EscapeString(helloAck.sessionChallenge, sb);
                }
                if (helloAck.sessionId != null)
                {
                    sb.Append(",\"sessionId\":");
                    EscapeString(helloAck.sessionId, sb);
                }
                sb.Append("}");
                return;
            }

            if (val is HeartbeatPayload hb)
            {
                sb.Append("{\"sequence\":");
                sb.Append(hb.sequence.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"clientTimestamp\":");
                sb.Append(hb.clientTimestamp.ToString(CultureInfo.InvariantCulture));
                if (hb.serverTimestamp.HasValue)
                {
                    sb.Append(",\"serverTimestamp\":");
                    sb.Append(hb.serverTimestamp.Value.ToString(CultureInfo.InvariantCulture));
                }
                if (hb.echoTimestamp.HasValue)
                {
                    sb.Append(",\"echoTimestamp\":");
                    sb.Append(hb.echoTimestamp.Value.ToString(CultureInfo.InvariantCulture));
                }
                sb.Append("}");
                return;
            }

            if (val is RuntimeStatusPayload rs)
            {
                sb.Append("{\"gamePid\":");
                sb.Append(rs.gamePid.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"gameVersion\":");
                EscapeString(rs.gameVersion ?? string.Empty, sb);
                sb.Append(",\"runtimeVersion\":");
                EscapeString(rs.runtimeVersion ?? string.Empty, sb);
                sb.Append(",\"isInitialized\":");
                sb.Append(rs.isInitialized ? "true" : "false");
                sb.Append(",\"inMatch\":");
                sb.Append(rs.inMatch ? "true" : "false");
                sb.Append("}");
                return;
            }

            if (val is TestEventPayload te)
            {
                sb.Append("{\"eventName\":");
                EscapeString(te.eventName ?? string.Empty, sb);
                sb.Append(",\"sender\":");
                EscapeString(te.sender ?? "RUNTIME", sb);
                sb.Append(",\"message\":");
                EscapeString(te.message ?? string.Empty, sb);
                if (te.extra != null)
                {
                    sb.Append(",\"extra\":");
                    SerializeValue(te.extra, sb);
                }
                sb.Append("}");
                return;
            }

            if (val is CardCountEntry cce)
            {
                sb.Append("{\"cardId\":");
                sb.Append(cce.cardId.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"count\":");
                sb.Append(cce.count.ToString(CultureInfo.InvariantCulture));
                sb.Append("}");
                return;
            }

            if (val is PlayerHandSummary phs)
            {
                sb.Append("{\"playerId\":");
                sb.Append(phs.playerId.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"handSize\":");
                sb.Append(phs.handSize.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"cardIds\":");
                SerializeValue(phs.cardIds ?? new List<int>(), sb);
                sb.Append(",\"cards\":");
                SerializeValue(phs.cards ?? new List<CardCountEntry>(), sb);
                sb.Append(",\"drawCountTotal\":");
                sb.Append(phs.drawCountTotal.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"playCountTotal\":");
                sb.Append(phs.playCountTotal.ToString(CultureInfo.InvariantCulture));
                sb.Append("}");
                return;
            }

            if (val is SpectatorHandPayload shp)
            {
                sb.Append("{\"schemaVersion\":");
                EscapeString(shp.schemaVersion ?? SpectatorHandPayload.CurrentSchemaVersion, sb);
                sb.Append(",\"roomId\":");
                sb.Append(shp.roomId.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"spectatorSessionEpoch\":");
                sb.Append(shp.spectatorSessionEpoch.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"spectatorAccountPlayerId\":");
                sb.Append(shp.spectatorAccountPlayerId.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"sequenceNumber\":");
                sb.Append(shp.sequenceNumber.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"snapshotTimestamp\":");
                EscapeString(shp.snapshotTimestamp ?? string.Empty, sb);
                sb.Append(",\"playerId\":");
                sb.Append(shp.playerId.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"cardIds\":");
                SerializeValue(shp.cardIds ?? new List<int>(), sb);
                sb.Append(",\"cards\":");
                SerializeValue(shp.cards ?? new List<CardCountEntry>(), sb);
                sb.Append(",\"handSize\":");
                sb.Append(shp.handSize.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"drawCountTotal\":");
                sb.Append(shp.drawCountTotal.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"playCountTotal\":");
                sb.Append(shp.playCountTotal.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"roomPlayers\":");
                SerializeValue(shp.roomPlayers ?? new Dictionary<string, PlayerHandSummary>(), sb);
                sb.Append(",\"sourcePlatform\":");
                EscapeString(shp.sourcePlatform ?? "TAPTAP", sb);
                sb.Append("}");
                return;
            }

            if (val is RuntimeMatchContextPayload rmc)
            {
                sb.Append("{\"roomId\":");
                sb.Append(rmc.roomId.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"inMatch\":");
                sb.Append(rmc.inMatch ? "true" : "false");
                if (!string.IsNullOrEmpty(rmc.connectionId))
                {
                    sb.Append(",\"connectionId\":");
                    EscapeString(rmc.connectionId, sb);
                }
                if (!string.IsNullOrEmpty(rmc.platformId))
                {
                    sb.Append(",\"platformId\":");
                    EscapeString(rmc.platformId, sb);
                }
                if (rmc.processId.HasValue)
                {
                    sb.Append(",\"processId\":");
                    sb.Append(rmc.processId.Value.ToString(CultureInfo.InvariantCulture));
                }
                if (rmc.localPlayerId.HasValue)
                {
                    sb.Append(",\"localPlayerId\":");
                    sb.Append(rmc.localPlayerId.Value.ToString(CultureInfo.InvariantCulture));
                }
                if (!string.IsNullOrEmpty(rmc.matchState))
                {
                    sb.Append(",\"matchState\":");
                    EscapeString(rmc.matchState, sb);
                }
                if (!string.IsNullOrEmpty(rmc.matchKind))
                {
                    sb.Append(",\"matchKind\":");
                    EscapeString(rmc.matchKind, sb);
                }
                if (rmc.internalRoomId.HasValue)
                {
                    sb.Append(",\"internalRoomId\":");
                    sb.Append(rmc.internalRoomId.Value.ToString(CultureInfo.InvariantCulture));
                }
                if (!string.IsNullOrEmpty(rmc.spectatorCode))
                {
                    sb.Append(",\"spectatorCode\":");
                    EscapeString(rmc.spectatorCode, sb);
                }
                sb.Append(",\"playerCount\":");
                sb.Append(rmc.playerCount.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"rosterPlayerIds\":");
                SerializeValue(rmc.rosterPlayerIds ?? new List<long>(), sb);
                if (!string.IsNullOrEmpty(rmc.scene))
                {
                    sb.Append(",\"scene\":");
                    EscapeString(rmc.scene, sb);
                }
                if (rmc.sequence.HasValue)
                {
                    sb.Append(",\"sequence\":");
                    sb.Append(rmc.sequence.Value.ToString(CultureInfo.InvariantCulture));
                }
                if (rmc.isPve.HasValue)
                {
                    sb.Append(",\"isPve\":");
                    sb.Append(rmc.isPve.Value ? "true" : "false");
                }
                if (rmc.timestamp.HasValue)
                {
                    sb.Append(",\"timestamp\":");
                    sb.Append(rmc.timestamp.Value.ToString(CultureInfo.InvariantCulture));
                }
                if (rmc.generation.HasValue)
                {
                    sb.Append(",\"generation\":");
                    sb.Append(rmc.generation.Value.ToString(CultureInfo.InvariantCulture));
                }
                sb.Append("}");
                return;
            }

            if (val is RuntimeIdentityUpdatePayload riu)
            {
                sb.Append("{\"platformId\":");
                EscapeString(riu.platformId ?? string.Empty, sb);
                sb.Append(",\"identitySource\":");
                EscapeString(riu.identitySource ?? string.Empty, sb);
                sb.Append(",\"playerId\":");
                sb.Append(riu.playerId.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"gamePid\":");
                sb.Append(riu.gamePid.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"isSelf\":");
                sb.Append(riu.isSelf ? "true" : "false");
                if (riu.accountId.HasValue)
                {
                    sb.Append(",\"accountId\":");
                    sb.Append(riu.accountId.Value.ToString(CultureInfo.InvariantCulture));
                }
                if (riu.displayName != null)
                {
                    sb.Append(",\"displayName\":");
                    EscapeString(riu.displayName, sb);
                }
                sb.Append("}");
                return;
            }

            if (val is XingShu.Platform.PlatformProfile pp)
            {
                XingShu.Platform.PlatformProfile.Serialize(pp, sb);
                return;
            }

            if (val is IDictionary dict)
            {
                sb.Append("{");
                bool first = true;
                foreach (DictionaryEntry kv in dict)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    EscapeString(Convert.ToString(kv.Key, CultureInfo.InvariantCulture), sb);
                    sb.Append(":");
                    SerializeValue(kv.Value, sb);
                }
                sb.Append("}");
                return;
            }

            if (val is IEnumerable list)
            {
                sb.Append("[");
                bool first = true;
                foreach (object item in list)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    SerializeValue(item, sb);
                }
                sb.Append("]");
                return;
            }

            // Fallback: reflect public properties
            sb.Append("{");
            var props = val.GetType().GetProperties();
            bool pFirst = true;
            for (int i = 0; i < props.Length; i++)
            {
                var p = props[i];
                if (!p.CanRead) continue;
                object pVal = null;
                try { pVal = p.GetValue(val, null); } catch { continue; }
                if (!pFirst) sb.Append(",");
                pFirst = false;
                EscapeString(p.Name, sb);
                sb.Append(":");
                SerializeValue(pVal, sb);
            }
            sb.Append("}");
        }

        public static void EscapeString(string s, StringBuilder sb)
        {
            if (s == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32 || c == 127)
                        {
                            sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)c);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        // ==========================================
        // 反序列化
        // ==========================================

        public static BridgeEnvelope DeserializeEnvelope(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            object parsed = Parse(json);
            if (parsed is Dictionary<string, object> dict)
            {
                var env = new BridgeEnvelope();
                if (dict.TryGetValue("protocolVersion", out var pv)) env.protocolVersion = Convert.ToString(pv);
                if (dict.TryGetValue("messageType", out var mt)) env.messageType = Convert.ToString(mt);
                if (dict.TryGetValue("requestId", out var ri)) env.requestId = Convert.ToString(ri);
                if (dict.TryGetValue("timestamp", out var ts))
                {
                    if (ts is long l) env.timestamp = l;
                    else if (ts is int it) env.timestamp = it;
                    else if (ts is double db) env.timestamp = (long)db;
                    else if (long.TryParse(Convert.ToString(ts), out var parsedTs)) env.timestamp = parsedTs;
                }
                if (dict.TryGetValue("payload", out var pl)) env.payload = pl;
                return env;
            }
            return null;
        }

        public static HelloPayload DeserializeHelloPayload(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            object parsed = Parse(json);
            return ToHelloPayload(parsed);
        }

        public static HelloPayload ToHelloPayload(object obj)
        {
            if (obj is Dictionary<string, object> dict)
            {
                var hello = new HelloPayload();
                if (dict.TryGetValue("component", out var c)) hello.component = Convert.ToString(c);
                if (dict.TryGetValue("runtimeVersion", out var rv)) hello.runtimeVersion = Convert.ToString(rv);
                if (dict.TryGetValue("gamePid", out var gp)) hello.gamePid = Convert.ToInt32(gp);
                if (dict.TryGetValue("gameVersion", out var gv)) hello.gameVersion = Convert.ToString(gv);
                if (dict.TryGetValue("sessionChallenge", out var sc)) hello.sessionChallenge = Convert.ToString(sc);
                if (dict.TryGetValue("capabilities", out var caps) && caps is List<object> capList)
                {
                    hello.capabilities = new List<string>();
                    foreach (var item in capList)
                    {
                        if (item != null) hello.capabilities.Add(item.ToString());
                    }
                }
                return hello;
            }
            return null;
        }

        public static SpectatorHandPayload DeserializeSpectatorHandPayload(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            object parsed = Parse(json);
            return ToSpectatorHandPayload(parsed);
        }

        public static SpectatorHandPayload ToSpectatorHandPayload(object obj)
        {
            if (obj == null) return null;
            if (obj is SpectatorHandPayload direct) return direct;
            if (obj is string str) return DeserializeSpectatorHandPayload(str);
            if (obj is Dictionary<string, object> dict)
            {
                var p = new SpectatorHandPayload();
                if (dict.TryGetValue("schemaVersion", out var sv) && sv != null) p.schemaVersion = Convert.ToString(sv);
                if ((dict.TryGetValue("sourcePlatform", out var sp) || dict.TryGetValue("platformId", out sp) || dict.TryGetValue("source_platform", out sp) || dict.TryGetValue("platform_id", out sp)) && sp != null) p.sourcePlatform = Convert.ToString(sp);
                if (dict.TryGetValue("roomId", out var rm) && rm != null) p.roomId = Convert.ToInt64(rm);
                if (dict.TryGetValue("spectatorSessionEpoch", out var ep) && ep != null) p.spectatorSessionEpoch = Convert.ToInt64(ep);
                if (dict.TryGetValue("spectatorAccountPlayerId", out var sap) && sap != null) p.spectatorAccountPlayerId = Convert.ToInt64(sap);
                if (dict.TryGetValue("sequenceNumber", out var sn) && sn != null) p.sequenceNumber = Convert.ToInt64(sn);
                if (dict.TryGetValue("snapshotTimestamp", out var st) && st != null) p.snapshotTimestamp = Convert.ToString(st);
                if (dict.TryGetValue("playerId", out var pid) && pid != null) p.playerId = Convert.ToInt64(pid);
                if (dict.TryGetValue("handSize", out var hs) && hs != null) p.handSize = Convert.ToInt32(hs);
                if (dict.TryGetValue("drawCountTotal", out var dc) && dc != null) p.drawCountTotal = Convert.ToInt32(dc);
                if (dict.TryGetValue("playCountTotal", out var pc) && pc != null) p.playCountTotal = Convert.ToInt32(pc);

                if (dict.TryGetValue("cardIds", out var cids) && cids is List<object> cidList)
                {
                    p.cardIds = new List<int>();
                    foreach (var item in cidList)
                    {
                        if (item != null) p.cardIds.Add(Convert.ToInt32(item));
                    }
                }

                if (dict.TryGetValue("cards", out var cardsObj) && cardsObj is List<object> cardsList)
                {
                    p.cards = new List<CardCountEntry>();
                    foreach (var item in cardsList)
                    {
                        var entry = ToCardCountEntry(item);
                        if (entry != null) p.cards.Add(entry);
                    }
                }

                if (dict.TryGetValue("roomPlayers", out var rpObj) && rpObj is Dictionary<string, object> rpDict)
                {
                    p.roomPlayers = new Dictionary<string, PlayerHandSummary>();
                    foreach (var kvp in rpDict)
                    {
                        var summary = ToPlayerHandSummary(kvp.Value);
                        if (summary != null) p.roomPlayers[kvp.Key] = summary;
                    }
                }

                return p;
            }
            return null;
        }

        public static PlayerHandSummary ToPlayerHandSummary(object obj)
        {
            if (obj is Dictionary<string, object> dict)
            {
                var s = new PlayerHandSummary();
                if (dict.TryGetValue("playerId", out var pid) && pid != null) s.playerId = Convert.ToInt64(pid);
                if (dict.TryGetValue("handSize", out var hs) && hs != null) s.handSize = Convert.ToInt32(hs);
                if (dict.TryGetValue("drawCountTotal", out var dc) && dc != null) s.drawCountTotal = Convert.ToInt32(dc);
                if (dict.TryGetValue("playCountTotal", out var pc) && pc != null) s.playCountTotal = Convert.ToInt32(pc);

                if (dict.TryGetValue("cardIds", out var cids) && cids is List<object> cidList)
                {
                    s.cardIds = new List<int>();
                    foreach (var item in cidList)
                    {
                        if (item != null) s.cardIds.Add(Convert.ToInt32(item));
                    }
                }

                if (dict.TryGetValue("cards", out var cardsObj) && cardsObj is List<object> cardsList)
                {
                    s.cards = new List<CardCountEntry>();
                    foreach (var item in cardsList)
                    {
                        var entry = ToCardCountEntry(item);
                        if (entry != null) s.cards.Add(entry);
                    }
                }

                return s;
            }
            return null;
        }

        public static CardCountEntry ToCardCountEntry(object obj)
        {
            if (obj is Dictionary<string, object> dict)
            {
                var entry = new CardCountEntry();
                if (dict.TryGetValue("cardId", out var cid) && cid != null) entry.cardId = Convert.ToInt32(cid);
                if (dict.TryGetValue("count", out var cnt) && cnt != null) entry.count = Convert.ToInt32(cnt);
                return entry;
            }
            return null;
        }

        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int index = 0;
            return ParseValue(json, ref index);
        }

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) return null;

            char c = json[index];
            if (c == '{') return ParseObject(json, ref index);
            if (c == '[') return ParseArray(json, ref index);
            if (c == '"') return ParseString(json, ref index);
            if (c == 't' || c == 'f') return ParseBool(json, ref index);
            if (c == 'n') return ParseNull(json, ref index);
            if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(json, ref index);

            return null;
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            index++; // skip '{'

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length) break;
                if (json[index] == '}')
                {
                    index++;
                    return dict;
                }

                if (json[index] == ',')
                {
                    index++;
                    SkipWhitespace(json, ref index);
                }

                if (index >= json.Length || json[index] == '}')
                {
                    if (index < json.Length) index++;
                    return dict;
                }

                string key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ':') index++;
                SkipWhitespace(json, ref index);
                object val = ParseValue(json, ref index);
                if (key != null) dict[key] = val;
            }
            return dict;
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var list = new List<object>();
            index++; // skip '['

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length) break;
                if (json[index] == ']')
                {
                    index++;
                    return list;
                }

                if (json[index] == ',')
                {
                    index++;
                    SkipWhitespace(json, ref index);
                }

                if (index >= json.Length || json[index] == ']')
                {
                    if (index < json.Length) index++;
                    return list;
                }

                object val = ParseValue(json, ref index);
                list.Add(val);
            }
            return list;
        }

        private static string ParseString(string json, ref int index)
        {
            if (index >= json.Length || json[index] != '"') return null;
            index++; // skip opening '"'
            StringBuilder sb = new StringBuilder();

            while (index < json.Length)
            {
                char c = json[index++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (index >= json.Length) break;
                    char esc = json[index++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (index + 4 <= json.Length)
                            {
                                string hex = json.Substring(index, 4);
                                index += 4;
                                if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                {
                                    sb.Append((char)code);
                                }
                            }
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static object ParseNumber(string json, ref int index)
        {
            int start = index;
            bool isFloat = false;
            if (json[index] == '-') index++;
            while (index < json.Length)
            {
                char c = json[index];
                if (c >= '0' && c <= '9') { index++; }
                else if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                {
                    isFloat = true;
                    index++;
                }
                else { break; }
            }

            string numStr = json.Substring(start, index - start);
            if (isFloat)
            {
                if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    return d;
            }
            else
            {
                if (long.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                    return l;
            }
            return 0;
        }

        private static bool ParseBool(string json, ref int index)
        {
            if (json.Length - index >= 4 && json.Substring(index, 4) == "true")
            {
                index += 4;
                return true;
            }
            if (json.Length - index >= 5 && json.Substring(index, 5) == "false")
            {
                index += 5;
                return false;
            }
            index++;
            return false;
        }

        private static object ParseNull(string json, ref int index)
        {
            if (json.Length - index >= 4 && json.Substring(index, 4) == "null")
            {
                index += 4;
            }
            return null;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length)
            {
                char c = json[index];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') index++;
                else break;
            }
        }
    }
}
