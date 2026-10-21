using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using XingShu.Bridge;

namespace XingShu.Platform
{
    /// <summary>
    /// Platform identifier enumeration.
    /// </summary>
    public enum PlatformId
    {
        STEAM_CN,
        STEAM_GLOBAL,
        TAPTAP,
        BILIBILI,
        WEGAME
    }

    /// <summary>
    /// Audit compatibility status enumeration.
    /// </summary>
    public enum CompatibilityStatus
    {
        VERIFIED,
        PARTIALLY_AUDITED,
        NOT_YET_AUDITED,
        INCOMPATIBLE
    }

    /// <summary>
    /// Managed C# representation of the formal PlatformProfile schema with 17 fields.
    /// Conforms to status-aware specification with strict validation and zero-dependency serialization.
    /// </summary>
    [Serializable]
    public class PlatformProfile
    {
        // 1. Platform identifier
        public string platformId;

        // 2. Channel code
        public string channel;

        // 3. Geographic region
        public string region;

        // Steam Application ID
        public int? steamAppId { get; set; }

        // 4. Game version
        public string gameVersion;

        // 5. Game build identifier
        public string gameBuild;

        // 6. Process executable filename
        public string processExecutable;

        // 7. Installation root path
        public string installRoot;

        // 8. Runtime assembly DLL path
        public string gameRuntimeDllPath;

        // 9. SHA-256 hash of original GameAssembly.dll
        public string gameRuntimeDllSha256;

        // 10. SHA-256 hash of XingShu runtime DLL
        public string xingshuRuntimeDllSha256;

        // 11. Bundle relative path to __data
        public string bundleRelativePath;

        // 12. Asset bundle 32-hex identifier
        public string bundleId;

        // 13. SHA-256 hash of bundle container
        public string containerSha256;

        // 14. Injected TextAsset PathID in Unity bundle
        public long? hookTextAssetPathId;

        // 15. Signatures of injected hook routines
        public List<string> hookSignatures = new List<string>();

        // 16. Audit compatibility status
        public string compatibilityStatus;

        // 17. ISO-8601 timestamp of last successful verification
        public string lastVerifiedAt;

        #region Helper Properties

        public PlatformId PlatformIdEnum
        {
            get => ParsePlatformId(platformId);
            set => platformId = value.ToString();
        }

        public CompatibilityStatus CompatibilityStatusEnum
        {
            get => ParseCompatibilityStatus(compatibilityStatus);
            set => compatibilityStatus = value.ToString();
        }

        #endregion

        #region Strict Validation

        /// <summary>
        /// Validates the platform profile against generic schema and field completeness requirements.
        /// Note: Baseline governance (only STEAM_CN is VERIFIED) is decoupled into VerifyHs4ABaselineGovernance.
        /// </summary>
        public bool Validate(out string errorMessage)
        {
            errorMessage = null;

            // 0. Empty platformId check
            if (string.IsNullOrEmpty(platformId) || platformId.Trim().Length == 0)
            {
                errorMessage = "platformId cannot be empty";
                return false;
            }

            bool isVerified = string.Equals(compatibilityStatus, CompatibilityStatus.VERIFIED.ToString(), StringComparison.OrdinalIgnoreCase);
            bool isSteamCn = string.Equals(platformId, PlatformId.STEAM_CN.ToString(), StringComparison.OrdinalIgnoreCase);

            // 1. Channel and region consistency
            if (isSteamCn)
            {
                if (channel != "CN_STEAM") { errorMessage = "STEAM_CN must have channel 'CN_STEAM', found '" + channel + "'"; return false; }
                if (region != "CN") { errorMessage = "STEAM_CN must have region 'CN', found '" + region + "'"; return false; }
            }
            else if (string.Equals(platformId, PlatformId.STEAM_GLOBAL.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (channel != "INT_STEAM") { errorMessage = "STEAM_GLOBAL must have channel 'INT_STEAM', found '" + channel + "'"; return false; }
                if (region != "GLOBAL") { errorMessage = "STEAM_GLOBAL must have region 'GLOBAL', found '" + region + "'"; return false; }
            }
            else if (string.Equals(platformId, PlatformId.TAPTAP.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (channel != "CN_TAPTAP") { errorMessage = "TAPTAP must have channel 'CN_TAPTAP', found '" + channel + "'"; return false; }
                if (region != "CN") { errorMessage = "TAPTAP must have region 'CN', found '" + region + "'"; return false; }
            }
            else if (string.Equals(platformId, PlatformId.BILIBILI.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (channel != "CN_BILIBILI") { errorMessage = "BILIBILI must have channel 'CN_BILIBILI', found '" + channel + "'"; return false; }
                if (region != "CN") { errorMessage = "BILIBILI must have region 'CN', found '" + region + "'"; return false; }
            }
            else if (string.Equals(platformId, PlatformId.WEGAME.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (channel != "CN_WEGAME") { errorMessage = "WEGAME must have channel 'CN_WEGAME', found '" + channel + "'"; return false; }
                if (region != "CN") { errorMessage = "WEGAME must have region 'CN', found '" + region + "'"; return false; }
            }
            else
            {
                errorMessage = "Unknown platformId: " + platformId;
                return false;
            }

            // 3. String non-empty validations on basic fields
            if (processExecutable == null || processExecutable.Trim().Length == 0) { errorMessage = "processExecutable cannot be empty"; return false; }
            if (gameVersion != null && gameVersion.Trim().Length == 0) { errorMessage = "gameVersion cannot be empty if specified"; return false; }
            if (gameBuild != null && gameBuild.Trim().Length == 0) { errorMessage = "gameBuild cannot be empty if specified"; return false; }
            if (gameRuntimeDllPath != null && gameRuntimeDllPath.Trim().Length == 0) { errorMessage = "gameRuntimeDllPath cannot be empty if specified"; return false; }
            if (installRoot != null && installRoot.Trim().Length == 0) { errorMessage = "installRoot cannot be empty if specified"; return false; }
            if (bundleRelativePath != null && bundleRelativePath.Trim().Length == 0) { errorMessage = "bundleRelativePath cannot be empty if specified"; return false; }

            // 4. Bundle ID format: must be 32-character hex Addressables identifier
            if (!string.IsNullOrEmpty(bundleId))
            {
                if (bundleId.Length != 32 || !IsHexString(bundleId))
                {
                    errorMessage = "bundleId '" + bundleId + "' is not a valid 32-character hex Addressables identifier";
                    return false;
                }
            }

            // 5. Verified status integrity: all 17 fields must be present and valid
            if (isVerified)
            {
                if (string.IsNullOrEmpty(gameVersion) || gameVersion.Trim().Length == 0)
                {
                    errorMessage = "gameVersion must be set for VERIFIED profile";
                    return false;
                }
                if (string.IsNullOrEmpty(gameBuild) || gameBuild.Trim().Length == 0)
                {
                    errorMessage = "gameBuild must be set for VERIFIED profile";
                    return false;
                }
                if (string.IsNullOrEmpty(processExecutable) || processExecutable.Trim().Length == 0)
                {
                    errorMessage = "processExecutable must be set for VERIFIED profile";
                    return false;
                }
                if (string.IsNullOrEmpty(installRoot) || installRoot.Trim().Length == 0)
                {
                    errorMessage = "installRoot must be set for VERIFIED profile";
                    return false;
                }
                if (string.IsNullOrEmpty(gameRuntimeDllPath) || gameRuntimeDllPath.Trim().Length == 0)
                {
                    errorMessage = "gameRuntimeDllPath must be set for VERIFIED profile";
                    return false;
                }
                if (string.IsNullOrEmpty(gameRuntimeDllSha256))
                {
                    errorMessage = "gameRuntimeDllSha256 must be set for VERIFIED profile";
                    return false;
                }
                if (string.IsNullOrEmpty(xingshuRuntimeDllSha256))
                {
                    errorMessage = "xingshuRuntimeDllSha256 must be set for VERIFIED profile";
                    return false;
                }
                if (string.IsNullOrEmpty(bundleRelativePath) || bundleRelativePath.Trim().Length == 0)
                {
                    errorMessage = "bundleRelativePath must be set for VERIFIED profile";
                    return false;
                }
                if (string.IsNullOrEmpty(bundleId))
                {
                    errorMessage = "bundleId must be set for VERIFIED profile";
                    return false;
                }
                if (string.IsNullOrEmpty(containerSha256))
                {
                    errorMessage = "containerSha256 must be set for VERIFIED profile";
                    return false;
                }
                if (!hookTextAssetPathId.HasValue)
                {
                    errorMessage = "hookTextAssetPathId must be set for VERIFIED profile";
                    return false;
                }
                if (hookSignatures == null || hookSignatures.Count == 0)
                {
                    errorMessage = "hookSignatures cannot be empty for VERIFIED profile";
                    return false;
                }
                if (string.IsNullOrEmpty(lastVerifiedAt) || lastVerifiedAt.Trim().Length == 0)
                {
                    errorMessage = "lastVerifiedAt must be specified for VERIFIED profile";
                    return false;
                }
            }

            // 6. SHA-256 format checks if values are present
            if (!ValidateSha256(gameRuntimeDllSha256, "gameRuntimeDllSha256", out errorMessage)) return false;
            if (!ValidateSha256(xingshuRuntimeDllSha256, "xingshuRuntimeDllSha256", out errorMessage)) return false;
            if (!ValidateSha256(containerSha256, "containerSha256", out errorMessage)) return false;

            // 7. ISO-8601 timestamp check if present
            if (!string.IsNullOrEmpty(lastVerifiedAt))
            {
                if (!DateTime.TryParse(lastVerifiedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) &&
                    !DateTime.TryParse(lastVerifiedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    errorMessage = "lastVerifiedAt must be a valid ISO-8601 string if specified";
                    return false;
                }
            }

            return true;
        }

        private static bool IsHexString(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex) return false;
            }
            return true;
        }

        private static bool ValidateSha256(string val, string name, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(val)) return true;
            if (val.Length != 64)
            {
                error = name + " must be a 64-character hex string if specified";
                return false;
            }
            if (!IsHexString(val))
            {
                error = name + " must be a valid hex string";
                return false;
            }
            return true;
        }

        #endregion

        #region Baseline Governance

        /// <summary>
        /// Baseline governance verification rule for HS4-A:
        /// Exactly one platform profile (STEAM_CN) must be VERIFIED.
        /// If any other platform profile is marked VERIFIED without audit, governance fails closed.
        /// </summary>
        public static bool VerifyHs4ABaselineGovernance(List<PlatformProfile> profiles, out string error)
        {
            error = null;
            if (profiles == null)
            {
                error = "Profiles list is null";
                return false;
            }
            int verifiedCount = 0;
            for (int i = 0; i < profiles.Count; i++)
            {
                var p = profiles[i];
                if (string.Equals(p.compatibilityStatus, CompatibilityStatus.VERIFIED.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(p.platformId, PlatformId.STEAM_CN.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Governance violation: platform " + p.platformId + " cannot be marked VERIFIED in HS4-A baseline; only STEAM_CN is authorized";
                        return false;
                    }
                    verifiedCount++;
                }
            }
            if (verifiedCount != 1)
            {
                error = "Governance violation: exactly one platform profile must be VERIFIED in HS4-A baseline, found " + verifiedCount;
                return false;
            }
            return true;
        }

        #endregion

        #region Defaults

        /// <summary>
        /// Returns the 5 official platform profiles parsed from canonical embedded JSON.
        /// STRICT: STEAM_CN is VERIFIED; STEAM_GLOBAL is PARTIALLY_AUDITED; all others are NOT_YET_AUDITED.
        /// </summary>
        public static List<PlatformProfile> GetDefaultProfiles()
        {
            return FromJsonArray(PlatformProfilesGenerated.CANONICAL_PROFILES_JSON);
        }

        public static PlatformProfile GetProfile(PlatformId id)
        {
            string idStr = id.ToString();
            var list = GetDefaultProfiles();
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].platformId, idStr, StringComparison.OrdinalIgnoreCase))
                {
                    return list[i];
                }
            }
            return null;
        }

        #endregion

        #region JSON Serialization

        /// <summary>
        /// Serializes this PlatformProfile instance to canonical JSON matching the 17 schema fields.
        /// </summary>
        public string ToJson()
        {
            StringBuilder sb = new StringBuilder(512);
            Serialize(this, sb);
            return sb.ToString();
        }

        public static void Serialize(PlatformProfile p, StringBuilder sb)
        {
            if (p == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append("{");
            sb.Append("\"platformId\":"); BridgeJson.EscapeString(p.platformId, sb);
            sb.Append(",\"channel\":"); BridgeJson.EscapeString(p.channel, sb);
            sb.Append(",\"region\":"); BridgeJson.EscapeString(p.region, sb);
            sb.Append(",\"steamAppId\":");
            if (p.steamAppId.HasValue) sb.Append(p.steamAppId.Value.ToString(CultureInfo.InvariantCulture)); else sb.Append("null");
            sb.Append(",\"gameVersion\":"); BridgeJson.EscapeString(p.gameVersion, sb);
            sb.Append(",\"gameBuild\":"); BridgeJson.EscapeString(p.gameBuild, sb);
            sb.Append(",\"processExecutable\":");
            if (p.processExecutable != null) BridgeJson.EscapeString(p.processExecutable, sb); else sb.Append("null");
            sb.Append(",\"installRoot\":");
            if (p.installRoot != null) BridgeJson.EscapeString(p.installRoot, sb); else sb.Append("null");
            sb.Append(",\"gameRuntimeDllPath\":");
            if (p.gameRuntimeDllPath != null) BridgeJson.EscapeString(p.gameRuntimeDllPath, sb); else sb.Append("null");
            sb.Append(",\"gameRuntimeDllSha256\":");
            if (p.gameRuntimeDllSha256 != null) BridgeJson.EscapeString(p.gameRuntimeDllSha256, sb); else sb.Append("null");
            sb.Append(",\"xingshuRuntimeDllSha256\":");
            if (p.xingshuRuntimeDllSha256 != null) BridgeJson.EscapeString(p.xingshuRuntimeDllSha256, sb); else sb.Append("null");
            sb.Append(",\"bundleRelativePath\":");
            if (p.bundleRelativePath != null) BridgeJson.EscapeString(p.bundleRelativePath, sb); else sb.Append("null");
            sb.Append(",\"bundleId\":");
            if (p.bundleId != null) BridgeJson.EscapeString(p.bundleId, sb); else sb.Append("null");
            sb.Append(",\"containerSha256\":");
            if (p.containerSha256 != null) BridgeJson.EscapeString(p.containerSha256, sb); else sb.Append("null");
            sb.Append(",\"hookTextAssetPathId\":");
            if (p.hookTextAssetPathId.HasValue) sb.Append(p.hookTextAssetPathId.Value.ToString(CultureInfo.InvariantCulture)); else sb.Append("null");
            sb.Append(",\"hookSignatures\":[");
            if (p.hookSignatures != null)
            {
                for (int i = 0; i < p.hookSignatures.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    BridgeJson.EscapeString(p.hookSignatures[i], sb);
                }
            }
            sb.Append("]");
            sb.Append(",\"compatibilityStatus\":"); BridgeJson.EscapeString(p.compatibilityStatus, sb);
            sb.Append(",\"lastVerifiedAt\":");
            if (p.lastVerifiedAt != null) BridgeJson.EscapeString(p.lastVerifiedAt, sb); else sb.Append("null");
            sb.Append("}");
        }

        /// <summary>
        /// Deserializes a PlatformProfile from a JSON string.
        /// </summary>
        public static PlatformProfile FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            object parsed = BridgeJson.Parse(json);
            return FromDictionary(parsed as Dictionary<string, object>);
        }

        /// <summary>
        /// Deserializes an array of PlatformProfile objects from a JSON string.
        /// </summary>
        public static List<PlatformProfile> FromJsonArray(string json)
        {
            if (string.IsNullOrEmpty(json)) return new List<PlatformProfile>();
            object parsed = BridgeJson.Parse(json);
            var list = new List<PlatformProfile>();
            if (parsed is List<object> array)
            {
                foreach (var item in array)
                {
                    if (item is Dictionary<string, object> dict)
                    {
                        var prof = FromDictionary(dict);
                        if (prof != null) list.Add(prof);
                    }
                }
            }
            return list;
        }

        public static PlatformProfile FromDictionary(Dictionary<string, object> dict)
        {
            if (dict == null) return null;
            var p = new PlatformProfile();
            if (dict.TryGetValue("platformId", out var pid) && pid != null) p.platformId = pid.ToString();
            if (dict.TryGetValue("channel", out var ch) && ch != null) p.channel = ch.ToString();
            if (dict.TryGetValue("region", out var reg) && reg != null) p.region = reg.ToString();
            if (dict.TryGetValue("steamAppId", out var sid) && sid != null) p.steamAppId = Convert.ToInt32(sid);
            if (dict.TryGetValue("gameVersion", out var gv) && gv != null) p.gameVersion = gv.ToString();
            if (dict.TryGetValue("gameBuild", out var gb) && gb != null) p.gameBuild = gb.ToString();
            if (dict.TryGetValue("processExecutable", out var pe) && pe != null) p.processExecutable = pe.ToString();
            if (dict.TryGetValue("installRoot", out var ir) && ir != null) p.installRoot = ir.ToString();
            if (dict.TryGetValue("gameRuntimeDllPath", out var gp) && gp != null) p.gameRuntimeDllPath = gp.ToString();
            if (dict.TryGetValue("gameRuntimeDllSha256", out var gs) && gs != null) p.gameRuntimeDllSha256 = gs.ToString();
            if (dict.TryGetValue("xingshuRuntimeDllSha256", out var xs) && xs != null) p.xingshuRuntimeDllSha256 = xs.ToString();
            if (dict.TryGetValue("bundleRelativePath", out var brp) && brp != null) p.bundleRelativePath = brp.ToString();
            if (dict.TryGetValue("bundleId", out var bid) && bid != null) p.bundleId = bid.ToString();
            if (dict.TryGetValue("containerSha256", out var cs) && cs != null) p.containerSha256 = cs.ToString();
            if (dict.TryGetValue("hookTextAssetPathId", out var hta) && hta != null) p.hookTextAssetPathId = Convert.ToInt64(hta);
            if (dict.TryGetValue("hookSignatures", out var hs) && hs is List<object> sigList)
            {
                p.hookSignatures = new List<string>();
                foreach (var item in sigList)
                {
                    if (item != null) p.hookSignatures.Add(item.ToString());
                }
            }
            if (dict.TryGetValue("compatibilityStatus", out var cStat) && cStat != null) p.compatibilityStatus = cStat.ToString();
            if (dict.TryGetValue("lastVerifiedAt", out var lva) && lva != null) p.lastVerifiedAt = lva.ToString();

            return p;
        }

        #endregion

        #region Enum Parsing Utilities

        public static PlatformId ParsePlatformId(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Trim().Length == 0) throw new ArgumentException("PlatformId cannot be empty");
            switch (s.Trim().ToUpperInvariant())
            {
                case "STEAM_CN": return PlatformId.STEAM_CN;
                case "STEAM_GLOBAL": return PlatformId.STEAM_GLOBAL;
                case "TAPTAP": return PlatformId.TAPTAP;
                case "BILIBILI": return PlatformId.BILIBILI;
                case "WEGAME": return PlatformId.WEGAME;
                default: throw new ArgumentException("Unknown PlatformId: " + s);
            }
        }

        public static CompatibilityStatus ParseCompatibilityStatus(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Trim().Length == 0) throw new ArgumentException("CompatibilityStatus cannot be empty");
            switch (s.Trim().ToUpperInvariant())
            {
                case "VERIFIED": return CompatibilityStatus.VERIFIED;
                case "PARTIALLY_AUDITED": return CompatibilityStatus.PARTIALLY_AUDITED;
                case "NOT_YET_AUDITED": return CompatibilityStatus.NOT_YET_AUDITED;
                case "INCOMPATIBLE": return CompatibilityStatus.INCOMPATIBLE;
                default: throw new ArgumentException("Unknown CompatibilityStatus: " + s);
            }
        }

        #endregion
    }
}
