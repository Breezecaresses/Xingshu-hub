use serde::{Deserialize, Serialize};
use std::fmt;
use std::str::FromStr;

/// Platform identifier enum for all supported game release targets.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum PlatformId {
    #[serde(rename = "STEAM_CN")]
    SteamCn,
    #[serde(rename = "STEAM_GLOBAL")]
    SteamGlobal,
    #[serde(rename = "TAPTAP")]
    TapTap,
    #[serde(rename = "BILIBILI")]
    Bilibili,
    #[serde(rename = "WEGAME")]
    WeGame,
}

impl fmt::Display for PlatformId {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            PlatformId::SteamCn => write!(f, "STEAM_CN"),
            PlatformId::SteamGlobal => write!(f, "STEAM_GLOBAL"),
            PlatformId::TapTap => write!(f, "TAPTAP"),
            PlatformId::Bilibili => write!(f, "BILIBILI"),
            PlatformId::WeGame => write!(f, "WEGAME"),
        }
    }
}

impl FromStr for PlatformId {
    type Err = String;

    fn from_str(s: &str) -> Result<Self, Self::Err> {
        match s.trim().to_uppercase().as_str() {
            "STEAM_CN" => Ok(PlatformId::SteamCn),
            "STEAM_GLOBAL" => Ok(PlatformId::SteamGlobal),
            "TAPTAP" => Ok(PlatformId::TapTap),
            "BILIBILI" => Ok(PlatformId::Bilibili),
            "WEGAME" => Ok(PlatformId::WeGame),
            other => Err(format!("Unknown platform ID: '{other}'")),
        }
    }
}

/// Audit compatibility status for a platform profile.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum CompatibilityStatus {
    #[serde(rename = "VERIFIED")]
    Verified,
    #[serde(rename = "PARTIALLY_AUDITED")]
    PartiallyAudited,
    #[serde(rename = "NOT_YET_AUDITED")]
    NotYetAudited,
    #[serde(rename = "INCOMPATIBLE")]
    Incompatible,
}

impl fmt::Display for CompatibilityStatus {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            CompatibilityStatus::Verified => write!(f, "VERIFIED"),
            CompatibilityStatus::PartiallyAudited => write!(f, "PARTIALLY_AUDITED"),
            CompatibilityStatus::NotYetAudited => write!(f, "NOT_YET_AUDITED"),
            CompatibilityStatus::Incompatible => write!(f, "INCOMPATIBLE"),
        }
    }
}

impl FromStr for CompatibilityStatus {
    type Err = String;

    fn from_str(s: &str) -> Result<Self, Self::Err> {
        match s.trim().to_uppercase().as_str() {
            "VERIFIED" => Ok(CompatibilityStatus::Verified),
            "PARTIALLY_AUDITED" => Ok(CompatibilityStatus::PartiallyAudited),
            "NOT_YET_AUDITED" => Ok(CompatibilityStatus::NotYetAudited),
            "INCOMPATIBLE" => Ok(CompatibilityStatus::Incompatible),
            other => Err(format!("Unknown compatibility status: '{other}'")),
        }
    }
}

/// Formal PlatformProfile schema with 17 fields.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlatformProfile {
    /// 1. Platform identifier
    pub platform_id: PlatformId,
    /// 2. Distribution channel code
    pub channel: String,
    /// 3. Geographic region code (e.g. "CN", "GLOBAL")
    pub region: String,
    /// Steam Application ID (canonical 2622000 for Steam platforms, None for others)
    #[serde(default)]
    pub steam_app_id: Option<u32>,
    /// 4. Game version string (e.g. "3.3.0")
    #[serde(default)]
    pub game_version: Option<String>,
    /// 5. Game build identifier (e.g. "3.3.0")
    #[serde(default)]
    pub game_build: Option<String>,
    /// 6. Game process executable filename
    #[serde(default)]
    pub process_executable: Option<String>,
    /// 7. Installation root path if discovered/configured
    #[serde(default)]
    pub install_root: Option<String>,
    /// 8. Path to primary game runtime assembly DLL
    #[serde(default)]
    pub game_runtime_dll_path: Option<String>,
    /// 9. SHA-256 digest of original GameAssembly.dll
    #[serde(default)]
    pub game_runtime_dll_sha256: Option<String>,
    /// 10. SHA-256 digest of injected XingShu runtime DLL
    #[serde(default)]
    pub xingshu_runtime_dll_sha256: Option<String>,
    /// 11. Relative path to injected container asset bundle
    #[serde(default)]
    pub bundle_relative_path: Option<String>,
    /// 12. Asset bundle GUID / Addressables 32-hex identifier
    #[serde(default)]
    pub bundle_id: Option<String>,
    /// 13. SHA-256 digest of bundle container
    #[serde(default)]
    pub container_sha256: Option<String>,
    /// 14. Injected TextAsset PathID in Unity bundle
    #[serde(default)]
    pub hook_text_asset_path_id: Option<i64>,
    /// 15. Signatures of injected hook routines
    #[serde(default)]
    pub hook_signatures: Vec<String>,
    /// 16. Audit compatibility status
    pub compatibility_status: CompatibilityStatus,
    /// 17. ISO-8601 timestamp of last successful verification
    #[serde(default)]
    pub last_verified_at: Option<String>,
}

fn check_placeholder(val: &Option<String>, name: &str) -> Result<(), String> {
    if let Some(s) = val {
        let trimmed = s.trim();
        if trimmed.is_empty() {
            return Err(format!("{name} cannot be empty or whitespace if specified"));
        }
        let lower = trimmed.to_lowercase();
        if matches!(lower.as_str(), "todo" | "placeholder" | "fake" | "n/a" | "null" | "undefined") {
            return Err(format!("{name} contains prohibited placeholder value: '{s}'"));
        }
    }
    Ok(())
}

fn check_bundle_id(val: &Option<String>) -> Result<(), String> {
    if let Some(s) = val {
        if s.len() != 32 || !s.chars().all(|c| c.is_ascii_hexdigit()) {
            return Err(format!(
                "bundleId must be a 32-character hexadecimal Addressables bundle identifier, found '{s}'"
            ));
        }
    }
    Ok(())
}

fn check_sha256(val: &Option<String>, name: &str) -> Result<(), String> {
    if let Some(s) = val {
        if s.len() != 64 || !s.chars().all(|c| c.is_ascii_hexdigit()) {
            return Err(format!("{name} must be a 64-character hex string if specified, found '{s}'"));
        }
    }
    Ok(())
}

fn check_iso8601(val: &Option<String>, name: &str) -> Result<(), String> {
    if let Some(s) = val {
        if chrono::DateTime::parse_from_rfc3339(s).is_err() {
            return Err(format!("{name} must be a valid ISO-8601 string, found '{s}'"));
        }
    }
    Ok(())
}

impl PlatformProfile {
    /// Validates field constraints and status-aware integrity requirements.
    pub fn validate_profile(&self) -> Result<(), String> {
        // 1. Channel and region consistency checks
        match self.platform_id {
            PlatformId::SteamCn => {
                if self.channel != "CN_STEAM" {
                    return Err(format!("STEAM_CN must have channel 'CN_STEAM', found '{}'", self.channel));
                }
                if self.region != "CN" {
                    return Err(format!("STEAM_CN must have region 'CN', found '{}'", self.region));
                }
            }
            PlatformId::SteamGlobal => {
                if self.channel != "INT_STEAM" {
                    return Err(format!("STEAM_GLOBAL must have channel 'INT_STEAM', found '{}'", self.channel));
                }
                if self.region != "GLOBAL" {
                    return Err(format!("STEAM_GLOBAL must have region 'GLOBAL', found '{}'", self.region));
                }
            }
            PlatformId::TapTap => {
                if self.channel != "CN_TAPTAP" {
                    return Err(format!("TAPTAP must have channel 'CN_TAPTAP', found '{}'", self.channel));
                }
                if self.region != "CN" {
                    return Err(format!("TAPTAP must have region 'CN', found '{}'", self.region));
                }
            }
            PlatformId::Bilibili => {
                if self.channel != "CN_BILIBILI" {
                    return Err(format!("BILIBILI must have channel 'CN_BILIBILI', found '{}'", self.channel));
                }
                if self.region != "CN" {
                    return Err(format!("BILIBILI must have region 'CN', found '{}'", self.region));
                }
            }
            PlatformId::WeGame => {
                if self.channel != "CN_WEGAME" {
                    return Err(format!("WEGAME must have channel 'CN_WEGAME', found '{}'", self.channel));
                }
                if self.region != "CN" {
                    return Err(format!("WEGAME must have region 'CN', found '{}'", self.region));
                }
            }
        }

        // 2. Prohibit empty or fake placeholders on present strings
        check_placeholder(&self.game_version, "gameVersion")?;
        check_placeholder(&self.game_build, "gameBuild")?;
        check_placeholder(&self.process_executable, "processExecutable")?;
        check_placeholder(&self.install_root, "installRoot")?;
        check_placeholder(&self.game_runtime_dll_path, "gameRuntimeDllPath")?;
        check_placeholder(&self.bundle_relative_path, "bundleRelativePath")?;

        // 3. Addressables 32-hex bundle identifier validation
        check_bundle_id(&self.bundle_id)?;

        // 4. SHA-256 format checks if values are present
        check_sha256(&self.game_runtime_dll_sha256, "gameRuntimeDllSha256")?;
        check_sha256(&self.xingshu_runtime_dll_sha256, "xingshuRuntimeDllSha256")?;
        check_sha256(&self.container_sha256, "containerSha256")?;

        // 5. ISO-8601 timestamp check if present
        check_iso8601(&self.last_verified_at, "lastVerifiedAt")?;

        // 6. Status-aware validation:
        if self.compatibility_status == CompatibilityStatus::Verified {
            if self.install_root.as_ref().map(|s| s.trim().is_empty()).unwrap_or(true) {
                return Err("installRoot must be specified for VERIFIED profile".into());
            }
            if self.game_version.as_ref().map(|s| s.trim().is_empty()).unwrap_or(true) {
                return Err("gameVersion must be specified for VERIFIED profile".into());
            }
            if self.game_build.as_ref().map(|s| s.trim().is_empty()).unwrap_or(true) {
                return Err("gameBuild must be specified for VERIFIED profile".into());
            }
            if self.process_executable.as_ref().map(|s| s.trim().is_empty()).unwrap_or(true) {
                return Err("processExecutable must be specified for VERIFIED profile".into());
            }
            if self.game_runtime_dll_path.as_ref().map(|s| s.trim().is_empty()).unwrap_or(true) {
                return Err("gameRuntimeDllPath must be specified for VERIFIED profile".into());
            }
            if self.game_runtime_dll_sha256.is_none() {
                return Err("gameRuntimeDllSha256 must be specified for VERIFIED profile".into());
            }
            if self.xingshu_runtime_dll_sha256.is_none() {
                return Err("xingshuRuntimeDllSha256 must be specified for VERIFIED profile".into());
            }
            if self.bundle_relative_path.as_ref().map(|s| s.trim().is_empty()).unwrap_or(true) {
                return Err("bundleRelativePath must be specified for VERIFIED profile".into());
            }
            if self.bundle_id.is_none() {
                return Err("bundleId must be specified for VERIFIED profile".into());
            }
            if self.container_sha256.is_none() {
                return Err("containerSha256 must be specified for VERIFIED profile".into());
            }
            if self.hook_text_asset_path_id.is_none() {
                return Err("hookTextAssetPathId must be set for VERIFIED profile".into());
            }
            if self.hook_signatures.is_empty() {
                return Err("hookSignatures cannot be empty for VERIFIED profile".into());
            }
            if self.last_verified_at.as_ref().map(|s| s.trim().is_empty()).unwrap_or(true) {
                return Err("lastVerifiedAt must be specified for VERIFIED profile".into());
            }
        }

        Ok(())
    }
}

/// Enforces HS4-A baseline governance: in the current baseline, ONLY STEAM_CN is allowed to be VERIFIED,
/// and all other profiles must NOT be VERIFIED.
pub fn verify_hs4_a_baseline_governance(profiles: &[PlatformProfile]) -> Result<(), String> {
    for profile in profiles {
        if profile.compatibility_status == CompatibilityStatus::Verified
            && profile.platform_id != PlatformId::SteamCn
        {
            return Err(format!(
                "HS4-A baseline governance violation: platform '{}' cannot be marked VERIFIED in current baseline; only STEAM_CN is permitted",
                profile.platform_id
            ));
        }
    }
    Ok(())
}

/// Embedded canonical JSON profile data.
pub const EMBEDDED_PROFILES_JSON: &str = include_str!("platform_profiles.json");

/// Returns the 5 official platform profiles.
pub fn get_default_profiles() -> Vec<PlatformProfile> {
    serde_json::from_str(EMBEDDED_PROFILES_JSON)
        .expect("embedded platform_profiles.json must be valid")
}

/// Retrieves the default profile for a specific platform ID.
pub fn get_profile(id: PlatformId) -> Option<PlatformProfile> {
    get_default_profiles().into_iter().find(|p| p.platform_id == id)
}

/// Parses a list of platform profiles from JSON.
pub fn load_profiles_from_json(json_str: &str) -> Result<Vec<PlatformProfile>, String> {
    serde_json::from_str::<Vec<PlatformProfile>>(json_str).map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_platform_id_display_and_from_str() {
        let cases = [
            (PlatformId::SteamCn, "STEAM_CN"),
            (PlatformId::SteamGlobal, "STEAM_GLOBAL"),
            (PlatformId::TapTap, "TAPTAP"),
            (PlatformId::Bilibili, "BILIBILI"),
            (PlatformId::WeGame, "WEGAME"),
        ];

        for (id, s) in cases {
            assert_eq!(id.to_string(), s);
            assert_eq!(PlatformId::from_str(s).unwrap(), id);
            assert_eq!(PlatformId::from_str(&s.to_lowercase()).unwrap(), id);
        }

        assert!(PlatformId::from_str("UNKNOWN_PLATFORM").is_err());
        assert!(PlatformId::from_str("").is_err());
    }

    #[test]
    fn test_compatibility_status_display_and_from_str() {
        let cases = [
            (CompatibilityStatus::Verified, "VERIFIED"),
            (CompatibilityStatus::PartiallyAudited, "PARTIALLY_AUDITED"),
            (CompatibilityStatus::NotYetAudited, "NOT_YET_AUDITED"),
            (CompatibilityStatus::Incompatible, "INCOMPATIBLE"),
        ];

        for (status, s) in cases {
            assert_eq!(status.to_string(), s);
            assert_eq!(CompatibilityStatus::from_str(s).unwrap(), status);
            assert_eq!(CompatibilityStatus::from_str(&s.to_lowercase()).unwrap(), status);
        }

        assert!(CompatibilityStatus::from_str("UNKNOWN_STATUS").is_err());
        assert!(CompatibilityStatus::from_str("").is_err());
    }

    #[test]
    fn test_default_profiles_integrity_and_validation() {
        let profiles = get_default_profiles();
        assert_eq!(profiles.len(), 5);

        for profile in &profiles {
            assert!(
                profile.validate_profile().is_ok(),
                "Validation failed for profile {}: {:?}",
                profile.platform_id,
                profile.validate_profile().err()
            );
        }

        // Governance baseline check passes for default profiles
        assert!(verify_hs4_a_baseline_governance(&profiles).is_ok());

        let steam_cn = profiles.iter().find(|p| p.platform_id == PlatformId::SteamCn).unwrap();
        assert_eq!(steam_cn.compatibility_status, CompatibilityStatus::PartiallyAudited);
        assert!(steam_cn.hook_text_asset_path_id.is_some());
        assert!(steam_cn.last_verified_at.is_none());
        assert!(!steam_cn.hook_signatures.is_empty());
        assert!(steam_cn.bundle_id.is_some());
        assert_eq!(steam_cn.bundle_id.as_deref(), Some("3486f99276eba77677d4880fd8d32918"));

        let steam_global = profiles.iter().find(|p| p.platform_id == PlatformId::SteamGlobal).unwrap();
        assert_eq!(steam_global.compatibility_status, CompatibilityStatus::PartiallyAudited);
        assert_eq!(steam_global.bundle_id.as_deref(), Some("8cc684d4b3b979e86b5788b7f1814132"));
    }

    #[test]
    fn test_get_profile() {
        let steam_cn = get_profile(PlatformId::SteamCn).expect("STEAM_CN profile");
        assert_eq!(steam_cn.channel, "CN_STEAM");
        assert_eq!(steam_cn.compatibility_status, CompatibilityStatus::PartiallyAudited);

        let steam_global = get_profile(PlatformId::SteamGlobal).expect("STEAM_GLOBAL profile");
        assert_eq!(steam_global.channel, "INT_STEAM");
        assert_eq!(steam_global.compatibility_status, CompatibilityStatus::PartiallyAudited);

        let taptap = get_profile(PlatformId::TapTap).expect("TAPTAP profile");
        assert_eq!(taptap.channel, "CN_TAPTAP");
        assert_eq!(taptap.compatibility_status, CompatibilityStatus::PartiallyAudited);

        let bilibili = get_profile(PlatformId::Bilibili).expect("BILIBILI profile");
        assert_eq!(bilibili.channel, "CN_BILIBILI");
        assert_eq!(bilibili.compatibility_status, CompatibilityStatus::PartiallyAudited);

        let wegame = get_profile(PlatformId::WeGame).expect("WEGAME profile");
        assert_eq!(wegame.channel, "CN_WEGAME");
        assert_eq!(wegame.compatibility_status, CompatibilityStatus::PartiallyAudited);
    }

    #[test]
    fn test_embedded_json_matches_defaults() {
        let loaded = load_profiles_from_json(EMBEDDED_PROFILES_JSON).expect("Parse embedded json");
        let defaults = get_default_profiles();
        assert_eq!(loaded.len(), defaults.len());
        for (a, b) in loaded.iter().zip(defaults.iter()) {
            assert_eq!(a, b);
        }
    }

    #[test]
    fn test_cross_platform_exact_five_profiles_order() {
        let profiles = get_default_profiles();
        assert_eq!(profiles.len(), 5);
        assert_eq!(profiles[0].platform_id, PlatformId::SteamCn);
        assert_eq!(profiles[1].platform_id, PlatformId::SteamGlobal);
        assert_eq!(profiles[2].platform_id, PlatformId::TapTap);
        assert_eq!(profiles[3].platform_id, PlatformId::Bilibili);
        assert_eq!(profiles[4].platform_id, PlatformId::WeGame);
    }

    #[test]
    fn test_decoupled_baseline_governance_and_schema_promotion() {
        // Core schema validation succeeds for promoted STEAM_GLOBAL if all verified fields are present
        let mut promoted_global = get_profile(PlatformId::SteamGlobal).unwrap();
        promoted_global.compatibility_status = CompatibilityStatus::Verified;
        promoted_global.bundle_id = Some("11223344556677889900aabbccddeeff".to_string());
        promoted_global.bundle_relative_path = Some(r"AstralParty_INT_Data\__data".to_string());
        promoted_global.xingshu_runtime_dll_sha256 = Some("105E39705202C5DC125063D2A9412FB0573E61213906C731827F14660C996C10".to_string());
        promoted_global.container_sha256 = Some("B8CC3264CC36B33DA6B28930F7854223160B39B09991F17917DEDD6A41D7927F".to_string());
        promoted_global.hook_text_asset_path_id = Some(99992000);
        promoted_global.hook_signatures = vec!["XingShuBootstrap.EnsureRuntimeHost".to_string()];
        promoted_global.last_verified_at = Some("2026-10-17T00:00:00Z".to_string());

        // Individual profile schema validation MUST SUCCEED (decoupled from baseline governance)
        assert!(promoted_global.validate_profile().is_ok());

        // Baseline governance MUST REJECT non-STEAM_CN VERIFIED
        let gov_res = verify_hs4_a_baseline_governance(&[promoted_global]);
        assert!(gov_res.is_err());
        assert!(gov_res.unwrap_err().contains("HS4-A baseline governance violation"));
    }

    #[test]
    fn test_validation_channel_and_region_mismatch() {
        let mut profile = get_profile(PlatformId::SteamCn).unwrap();
        profile.channel = "WRONG_CHANNEL".into();
        assert!(profile.validate_profile().is_err());

        let mut profile2 = get_profile(PlatformId::SteamCn).unwrap();
        profile2.region = "WRONG_REGION".into();
        assert!(profile2.validate_profile().is_err());
    }

    #[test]
    fn test_validation_bundle_id_format_and_prohibit_uuid() {
        let mut profile = get_profile(PlatformId::SteamCn).unwrap();
        // Hyphenated UUID must be rejected
        profile.bundle_id = Some("c2a8f8e1-5b74-4b57-9d7a-18f4a9b6c001".into());
        assert!(profile.validate_profile().is_err());

        // Non-hex characters rejected
        profile.bundle_id = Some("6cc68f014947b66dea74130802768a3z".into());
        assert!(profile.validate_profile().is_err());

        // Length != 32 rejected
        profile.bundle_id = Some("6cc68f014947b66dea74130802768a".into());
        assert!(profile.validate_profile().is_err());

        // Valid 32-char hex accepted
        profile.bundle_id = Some("6cc68f014947b66dea74130802768a37".into());
        assert!(profile.validate_profile().is_ok());
    }

    #[test]
    fn test_prohibit_placeholders() {
        let mut profile = get_profile(PlatformId::SteamGlobal).unwrap();
        profile.install_root = Some("TODO".into());
        assert!(profile.validate_profile().is_err());

        profile.install_root = Some("   ".into());
        assert!(profile.validate_profile().is_err());

        profile.install_root = Some("placeholder".into());
        assert!(profile.validate_profile().is_err());
    }

    #[test]
    fn test_json_camel_case_field_names() {
        let profile = get_profile(PlatformId::SteamCn).unwrap();
        let json = serde_json::to_string(&profile).expect("Serialize to json");

        let mandatory_json_keys = [
            "platformId",
            "channel",
            "region",
            "steamAppId",
            "gameVersion",
            "gameBuild",
            "processExecutable",
            "installRoot",
            "gameRuntimeDllPath",
            "gameRuntimeDllSha256",
            "xingshuRuntimeDllSha256",
            "bundleRelativePath",
            "bundleId",
            "containerSha256",
            "hookTextAssetPathId",
            "hookSignatures",
            "compatibilityStatus",
            "lastVerifiedAt",
        ];

        for key in mandatory_json_keys {
            assert!(
                json.contains(&format!("\"{key}\":")),
                "JSON must contain camelCase key: {key}"
            );
        }

        // Roundtrip test
        let roundtrip: PlatformProfile = serde_json::from_str(&json).expect("Deserialize from json");
        assert_eq!(roundtrip, profile);
    }

    // =========================================================================
    // 12 SPECIFICATION CONFORMANCE SCENARIOS (PROJECT XINGSHU HS4-A.1)
    // =========================================================================

    // Scenario 1: Resource 026 selects the new cache and does not inherit old runtime verification.
    #[test]
    fn test_scenario_1_resource_026_profile_requires_new_verification() {
        let profile = get_profile(PlatformId::SteamCn).expect("STEAM_CN profile must exist");
        assert_eq!(profile.platform_id, PlatformId::SteamCn);
        assert_eq!(profile.channel, "CN_STEAM");
        assert_eq!(profile.region, "CN");
        assert_eq!(profile.steam_app_id, Some(2622000));
        assert_eq!(profile.game_version.as_deref(), Some("3.3.0"));
        assert_eq!(profile.game_build.as_deref(), Some("3.3.0"));
        assert_eq!(profile.process_executable.as_deref(), Some("AstralParty_CN.exe"));
        assert!(profile.install_root.is_some());
        assert_eq!(profile.game_runtime_dll_path.as_deref(), Some("GameAssembly.dll"));
        assert_eq!(
            profile.game_runtime_dll_sha256.as_deref(),
            Some("6279B4A1814E9B0DEF32779EBC2DFD5810373F112F0B12EE02F5C97CAA4F46FA")
        );
        assert_eq!(
            profile.xingshu_runtime_dll_sha256.as_deref(),
            None
        );
        assert!(profile.bundle_relative_path.is_some());
        assert_eq!(profile.bundle_id.as_deref(), Some("3486f99276eba77677d4880fd8d32918"));
        assert_eq!(
            profile.container_sha256.as_deref(),
            Some("420FBB1DEE1CEFE7298213EBF5F292B4722D0CB08A2A5E3F96EE94AB4AABDDEF")
        );
        assert_eq!(profile.hook_text_asset_path_id, Some(99992000));
        assert_eq!(
            profile.hook_signatures,
            vec![
                "XingShuBootstrap.EnsureRuntimeHost",
                "SpectatorEventInterceptor.Initialize",
                "LoopbackTcpTransport.Connect",
                "SpectatorHandStateReducer.ProcessPayload"
            ]
        );
        assert_eq!(profile.compatibility_status, CompatibilityStatus::PartiallyAudited);
        assert_eq!(profile.last_verified_at.as_deref(), None);

        assert!(
            profile.validate_profile().is_ok(),
            "Scenario 1: STEAM_CN must pass validate_profile()"
        );
    }

    // Scenario 2: VERIFIED missing hash -> reject: set game_runtime_dll_sha256 = None on VERIFIED profile, assert error.
    #[test]
    fn test_scenario_2_verified_missing_hash_reject() {
        let mut profile = get_profile(PlatformId::SteamCn).expect("STEAM_CN profile");
        profile.compatibility_status = CompatibilityStatus::Verified;
        profile.game_runtime_dll_sha256 = None;
        let result = profile.validate_profile();
        assert!(result.is_err(), "Scenario 2: Missing gameRuntimeDllSha256 must be rejected");
        assert!(
            result.unwrap_err().contains("gameRuntimeDllSha256"),
            "Error message must specify gameRuntimeDllSha256"
        );
    }

    // Retain unknown-profile schema coverage independently of audited channel defaults.
    #[test]
    fn test_scenario_3_not_yet_audited_null_unknown_fields_pass() {
        let unverified_ids = [PlatformId::Bilibili, PlatformId::WeGame];
        for id in unverified_ids {
            let mut profile = get_profile(id).unwrap_or_else(|| panic!("{id} profile must exist"));
            assert_eq!(profile.compatibility_status, CompatibilityStatus::PartiallyAudited);
            assert!(profile.install_root.is_some() && profile.bundle_relative_path.is_some());
            assert!(profile.game_runtime_dll_sha256.as_ref().is_some_and(|h| h.len() == 64));
            assert!(profile.validate_profile().is_ok());
            profile.compatibility_status = CompatibilityStatus::NotYetAudited;
            profile.install_root = None;
            profile.game_version = None;
            profile.game_build = None;
            profile.game_runtime_dll_path = None;
            profile.game_runtime_dll_sha256 = None;
            profile.xingshu_runtime_dll_sha256 = None;
            profile.bundle_relative_path = None;
            profile.bundle_id = None;
            profile.container_sha256 = None;
            profile.hook_text_asset_path_id = None;
            profile.hook_signatures.clear();
            profile.last_verified_at = None;
            assert_eq!(profile.compatibility_status, CompatibilityStatus::NotYetAudited);
            assert_eq!(profile.steam_app_id, None);
            assert!(profile.install_root.is_none());
            assert!(profile.game_version.is_none());
            assert!(profile.game_build.is_none());
            assert!(profile.game_runtime_dll_path.is_none());
            assert!(profile.game_runtime_dll_sha256.is_none());
            assert!(profile.xingshu_runtime_dll_sha256.is_none());
            assert!(profile.bundle_relative_path.is_none());
            assert!(profile.bundle_id.is_none());
            assert!(profile.container_sha256.is_none());
            assert!(profile.hook_text_asset_path_id.is_none());
            assert!(profile.hook_signatures.is_empty());
            assert!(profile.last_verified_at.is_none());
            assert!(
                profile.validate_profile().is_ok(),
                "Scenario 3: NOT_YET_AUDITED {id} with null fields must pass validation"
            );
        }
    }

    // Scenario 4: PARTIALLY_AUDITED partial fields -> PASS: Steam Global with partial disk fields passes validation.
    #[test]
    fn test_scenario_4_partially_audited_partial_fields_pass() {
        let profile = get_profile(PlatformId::SteamGlobal).expect("STEAM_GLOBAL profile");
        assert_eq!(profile.compatibility_status, CompatibilityStatus::PartiallyAudited);
        assert_eq!(profile.steam_app_id, Some(2622000));
        assert!(profile.install_root.is_some());
        assert!(profile.game_runtime_dll_sha256.is_some());
        assert_eq!(profile.bundle_id.as_deref(), Some("8cc684d4b3b979e86b5788b7f1814132"));
        assert_eq!(
            profile.bundle_relative_path.as_deref(),
            Some("AstralParty_INT_Data\\com.unity.addressables\\AssetBundles\\57a198703b66364d5420d39c0d251747\\8cc684d4b3b979e86b5788b7f1814132\\__data")
        );
        assert_eq!(
            profile.container_sha256.as_deref(),
            Some("20846D23A75E2F6CE78AE2FFFB661AA4CB02364C45AA27D99FA85FD4F6F3C103")
        );
        assert_eq!(profile.hook_text_asset_path_id, Some(99992000));
        assert_eq!(profile.hook_signatures.len(), 4);
        assert!(profile.xingshu_runtime_dll_sha256.is_none());
        assert!(profile.last_verified_at.is_none());
        assert!(
            profile.validate_profile().is_ok(),
            "Scenario 4: PARTIALLY_AUDITED STEAM_GLOBAL with partial fields must pass validation"
        );
    }

    // Scenario 5: empty PlatformId -> reject: "" fails closed.
    #[test]
    fn test_scenario_5_empty_platform_id_reject() {
        let result = PlatformId::from_str("");
        assert!(result.is_err(), "Scenario 5: Empty PlatformId must fail closed");
        let result_ws = PlatformId::from_str("   ");
        assert!(result_ws.is_err(), "Scenario 5: Whitespace PlatformId must fail closed");
    }

    // Scenario 6: unknown PlatformId -> reject: "EPIC_GAMES" fails closed.
    #[test]
    fn test_scenario_6_unknown_platform_id_reject() {
        let result = PlatformId::from_str("EPIC_GAMES");
        assert!(result.is_err(), "Scenario 6: Unknown PlatformId 'EPIC_GAMES' must fail closed");
        assert!(result.unwrap_err().contains("Unknown platform ID"));
    }

    // Scenario 7: real 32-hex bundleId -> PASS: "6cc68f014947b66dea74130802768a37" passes.
    #[test]
    fn test_scenario_7_real_32_hex_bundle_id_pass() {
        let mut profile = get_profile(PlatformId::SteamCn).expect("STEAM_CN profile");
        profile.bundle_id = Some("6cc68f014947b66dea74130802768a37".to_string());
        assert!(
            profile.validate_profile().is_ok(),
            "Scenario 7: Real 32-hex Addressables bundleId must pass"
        );
    }

    // Scenario 8: fake UUID placeholder no longer needed: 32-hex accepted, non-32-hex rejected.
    #[test]
    fn test_scenario_8_fake_uuid_placeholder_no_longer_needed_and_non_32_hex_rejected() {
        let mut profile = get_profile(PlatformId::SteamCn).expect("STEAM_CN profile");

        // 32-hex passes
        profile.bundle_id = Some("6cc68f014947b66dea74130802768a37".to_string());
        assert!(profile.validate_profile().is_ok());

        // Old fake UUID placeholder with hyphens (length 36) rejected
        profile.bundle_id = Some("c2a8f8e1-5b74-4b57-9d7a-18f4a9b6c001".to_string());
        let res_uuid = profile.validate_profile();
        assert!(res_uuid.is_err(), "Scenario 8: Old fake UUID placeholder must be rejected");
        assert!(res_uuid.unwrap_err().contains("32-character"));

        // Arbitrary non-32-hex string rejected
        profile.bundle_id = Some("not-a-valid-hex-bundle-id".to_string());
        assert!(profile.validate_profile().is_err());

        // Short hex string rejected
        profile.bundle_id = Some("6cc68f014947".to_string());
        assert!(profile.validate_profile().is_err());
    }

    // Scenario 9: Bilibili process = 吉星派对.exe (assert profile process_executable == Some("吉星派对.exe")).
    #[test]
    fn test_scenario_9_bilibili_process_executable() {
        let profile = get_profile(PlatformId::Bilibili).expect("BILIBILI profile");
        assert_eq!(
            profile.process_executable.as_deref(),
            Some("吉星派对.exe"),
            "Scenario 9: Bilibili process must be 吉星派对.exe"
        );
        assert_eq!(profile.process_executable, Some("吉星派对.exe".to_string()));
    }

    // Scenario 10: WeGame process = 吉星派对.exe (assert profile process_executable == Some("吉星派对.exe")).
    #[test]
    fn test_scenario_10_wegame_process_executable() {
        let profile = get_profile(PlatformId::WeGame).expect("WEGAME profile");
        assert_eq!(
            profile.process_executable.as_deref(),
            Some("吉星派对.exe"),
            "Scenario 10: WeGame process must be 吉星派对.exe"
        );
        assert_eq!(profile.process_executable, Some("吉星派对.exe".to_string()));
    }

    // Scenario 11: STEAM_GLOBAL = PARTIALLY_AUDITED (assert compatibility_status == PartiallyAudited).
    #[test]
    fn test_scenario_11_steam_global_partially_audited() {
        let profile = get_profile(PlatformId::SteamGlobal).expect("STEAM_GLOBAL profile");
        assert_eq!(
            profile.compatibility_status,
            CompatibilityStatus::PartiallyAudited,
            "Scenario 11: STEAM_GLOBAL must be PARTIALLY_AUDITED"
        );
    }

    // Scenario 12: Current baseline governance: verify_hs4_a_baseline_governance PASS
    // (only STEAM_CN is VERIFIED; if any other is promoted without audit, governance fails closed).
    #[test]
    fn test_scenario_12_current_baseline_governance_verify_hs4_a_baseline_governance() {
        let default_profiles = get_default_profiles();
        assert!(
            verify_hs4_a_baseline_governance(&default_profiles).is_ok(),
            "Scenario 12: Default profiles must satisfy HS4-A baseline governance"
        );

        // Attempting to promote any non-STEAM_CN profile without audit fails closed
        for unverified_id in [
            PlatformId::SteamGlobal,
            PlatformId::TapTap,
            PlatformId::Bilibili,
            PlatformId::WeGame,
        ] {
            let mut tampered = default_profiles.clone();
            for p in &mut tampered {
                if p.platform_id == unverified_id {
                    p.compatibility_status = CompatibilityStatus::Verified;
                }
            }
            let res = verify_hs4_a_baseline_governance(&tampered);
            assert!(
                res.is_err(),
                "Scenario 12: Promoting {unverified_id} to VERIFIED must fail governance check"
            );
            assert!(res.unwrap_err().contains("baseline governance violation"));
        }
    }

    // Scenario 13: Tri-Language Decoupling Conformance: Generic Validation PASS for fully verified STEAM_GLOBAL, but Baseline Governance FAILS
    #[test]
    fn test_scenario_13_tri_language_decoupling_conformance() {
        let promoted_global = PlatformProfile {
            platform_id: PlatformId::SteamGlobal,
            channel: "INT_STEAM".to_string(),
            region: "GLOBAL".to_string(),
            steam_app_id: Some(2622000),
            game_version: Some("3.3.0".to_string()),
            game_build: Some("3.3.0".to_string()),
            process_executable: Some("AstralParty.exe".to_string()),
            install_root: Some(r"C:\Program Files (x86)\Steam\steamapps\common\AstralParty".to_string()),
            game_runtime_dll_path: Some("GameAssembly.dll".to_string()),
            game_runtime_dll_sha256: Some("27A1215326741786D41E82A4335669727A3A6F6283EF2C025BF2C0A97002D7E2".to_string()),
            xingshu_runtime_dll_sha256: Some("B8CC3264CC36B33DA6B28930F7854223160B39B09991F17917DEDD6A41D7927F".to_string()),
            bundle_relative_path: Some(r"AstralParty_Data\__data".to_string()),
            bundle_id: Some("6cc68f014947b66dea74130802768a37".to_string()),
            container_sha256: Some("105E39705202C5DC125063D2A9412FB0573E61213906C731827F14660C996C10".to_string()),
            hook_text_asset_path_id: Some(99992000),
            hook_signatures: vec![
                "XingShuBootstrap.EnsureRuntimeHost".to_string(),
                "SpectatorEventInterceptor.Initialize".to_string(),
            ],
            compatibility_status: CompatibilityStatus::Verified,
            last_verified_at: Some("2026-10-17T00:00:00Z".to_string()),
        };

        // 1. Generic Validation MUST PASS
        assert!(
            promoted_global.validate_profile().is_ok(),
            "Scenario 13: Generic Validation for fully verified STEAM_GLOBAL must pass"
        );

        // 2. Baseline Governance (standalone list) MUST FAIL
        assert!(
            verify_hs4_a_baseline_governance(&[promoted_global.clone()]).is_err(),
            "Scenario 13: Standalone verified STEAM_GLOBAL must fail baseline governance"
        );

        // 3. Baseline Governance (in default profiles collection) MUST FAIL
        let mut profiles = get_default_profiles();
        profiles[1] = promoted_global;
        let res = verify_hs4_a_baseline_governance(&profiles);
        assert!(
            res.is_err(),
            "Scenario 13: Baseline governance with verified STEAM_GLOBAL must fail closed"
        );
        assert!(
            res.unwrap_err().contains("HS4-A baseline governance violation"),
            "Scenario 13: Error must mention baseline governance violation"
        );
    }
}
