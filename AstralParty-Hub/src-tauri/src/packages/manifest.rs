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
    pub package_type: String, // "apx-plugin", "mod", "resource-pack"
    pub name: String,
    pub version: String,
    pub publisher: String,
    pub description: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CompatibilityMeta {
    pub game: GameCompatibility,
    pub apx: Option<ApxCompatibility>,
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
pub struct ApxCompatibility {
    pub min_version: Option<String>,
    pub max_version: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct InstallPolicy {
    pub requires_game_closed: bool,
    pub requires_apx_closed: bool,
    pub backup: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PackageFileEntry {
    pub source: String,
    pub target: String,
    pub sha256: String,
    pub size: u64,
}
