use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HubPackageManifestV1 {
    pub schema_version: u32,
    pub package: PackageMeta,
    pub compatibility: CompatibilityMeta,
    pub install: InstallPolicy,
    pub files: Vec<PackageFileEntry>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PackageMeta {
    pub id: String,
    #[serde(rename = "type")]
    pub package_type: String, // "xingshu-plugin", "mod", "resource-pack"
    pub name: String,
    pub version: String,
    pub publisher: String,
    pub description: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CompatibilityMeta {
    pub game: GameCompatibility,
    #[serde(alias = "apx")]
    pub xingshu: Option<XingShuCompatibility>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GameCompatibility {
    pub app_id: String,
    #[serde(default)]
    pub platforms: Vec<String>,
    pub min_version: Option<String>,
    pub max_version: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct XingShuCompatibility {
    pub min_version: Option<String>,
    pub max_version: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct InstallPolicy {
    pub requires_game_closed: bool,
    #[serde(rename = "requiresXingShuClosed", alias = "requiresApxClosed", alias = "requiresXingshuClosed")]
    pub requires_xingshu_closed: bool,
    pub backup: bool,
}

#[cfg(test)]
mod rename_compatibility_tests {
    use super::*;

    #[test]
    fn legacy_package_fields_are_read_but_new_names_are_written() {
        let policy: InstallPolicy = serde_json::from_str(r#"{"requiresGameClosed":true,"requiresApxClosed":true,"backup":true}"#).unwrap();
        let serialized = serde_json::to_value(&policy).unwrap();
        assert_eq!(serialized["requiresXingShuClosed"], true);
        assert!(serialized.get("requiresApxClosed").is_none());
        let compatibility: CompatibilityMeta = serde_json::from_str(r#"{"game":{"appId":"astralparty"},"apx":{"minVersion":"1.0"}}"#).unwrap();
        assert_eq!(compatibility.xingshu.as_ref().unwrap().min_version.as_deref(), Some("1.0"));
        let serialized = serde_json::to_value(compatibility).unwrap();
        assert!(serialized.get("xingshu").is_some());
        assert!(serialized.get("apx").is_none());
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PackageFileEntry {
    pub source: String,
    pub target: String,
    pub sha256: String,
    pub size: u64,
}
