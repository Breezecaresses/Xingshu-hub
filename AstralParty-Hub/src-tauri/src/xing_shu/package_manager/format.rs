use serde::{Deserialize, Serialize};

pub const XSPKG_MAGIC: &[u8; 4] = b"XSPK";
pub const XSPKG_CURRENT_SCHEMA: u32 = 1;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub enum XspkgType {
    RuntimePlugin,
    Mod,
    AssetPack,
    Theme,
    Composite,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct XspkgDependency {
    pub package_id: String,
    pub version_req: String,
    pub optional: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct XspkgSignature {
    pub algorithm: String, // e.g. "ed25519"
    pub public_key: String,
    pub signature_hex: String,
    pub signed_by: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct XspkgManifest {
    pub schema_version: u32,
    pub package_id: String,
    pub name: String,
    pub version: String,
    pub author: String,
    pub description: String,
    pub package_type: XspkgType,
    pub entry_dll: Option<String>,
    pub target_game_version: Option<String>,
    pub min_runtime_version: Option<String>,
    pub dependencies: Vec<XspkgDependency>,
    pub permissions: Vec<String>,
    pub checksum_sha256: Option<String>,
    pub signature: Option<XspkgSignature>,
    pub legacy_format_origin: Option<String>,
}

impl Default for XspkgManifest {
    fn default() -> Self {
        Self {
            schema_version: XSPKG_CURRENT_SCHEMA,
            package_id: String::new(),
            name: String::new(),
            version: "1.0.0".into(),
            author: "Unknown".into(),
            description: String::new(),
            package_type: XspkgType::RuntimePlugin,
            entry_dll: None,
            target_game_version: None,
            min_runtime_version: None,
            dependencies: vec![],
            permissions: vec![],
            checksum_sha256: None,
            signature: None,
            legacy_format_origin: None,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_xspkg_manifest_roundtrip() {
        let manifest = XspkgManifest {
            schema_version: XSPKG_CURRENT_SCHEMA,
            package_id: "xs.plugin.hud_enhancer".into(),
            name: "HUD Enhancer".into(),
            version: "1.2.0".into(),
            author: "XingShu Dev".into(),
            description: "Enhances in-game battle interface".into(),
            package_type: XspkgType::RuntimePlugin,
            entry_dll: Some("HudEnhancer.dll".into()),
            target_game_version: Some("1.3.4".into()),
            min_runtime_version: Some("1.0.0".into()),
            dependencies: vec![XspkgDependency {
                package_id: "xs.core.runtime".into(),
                version_req: ">=1.0.0".into(),
                optional: false,
            }],
            permissions: vec!["runtime_hook".into(), "bridge_read".into()],
            checksum_sha256: Some("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855".into()),
            signature: Some(XspkgSignature {
                algorithm: "ed25519".into(),
                public_key: "pubkey_test".into(),
                signature_hex: "deadbeef".into(),
                signed_by: "XingShu Official".into(),
            }),
            legacy_format_origin: None,
        };

        let json = serde_json::to_string(&manifest).expect("Serialize xspkg manifest");
        let parsed: XspkgManifest = serde_json::from_str(&json).expect("Deserialize xspkg manifest");

        assert_eq!(parsed.package_id, "xs.plugin.hud_enhancer");
        assert_eq!(parsed.package_type, XspkgType::RuntimePlugin);
        assert_eq!(parsed.entry_dll.as_deref(), Some("HudEnhancer.dll"));
        assert_eq!(parsed.dependencies.len(), 1);
        assert!(parsed.signature.is_some());
    }
}
