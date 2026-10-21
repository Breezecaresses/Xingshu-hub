use std::fs::File;
use std::io::Read;
use std::path::Path;
use sha2::{Digest, Sha256};
use zip::ZipArchive;

use crate::domain::error::{HubError, HubResult};
use crate::xing_shu::package_manager::format::{XspkgManifest, XspkgType, XSPKG_CURRENT_SCHEMA};

pub trait IPackageFormatAdapter: Send + Sync {
    fn format_name(&self) -> &str;
    fn can_handle(&self, filename: &str) -> bool;
    fn inspect(&self, path: &Path) -> HubResult<XspkgManifest>;
}

fn calculate_file_sha256(path: &Path) -> HubResult<String> {
    let mut file = File::open(path).map_err(|e| {
        HubError::new("FILE_OPEN_FAILED", format!("Failed to open package: {e}"), "PACKAGE")
    })?;
    let mut hasher = Sha256::new();
    let mut buffer = [0; 8192];
    loop {
        let count = file.read(&mut buffer).map_err(|e| {
            HubError::new("FILE_READ_FAILED", format!("Failed reading package: {e}"), "PACKAGE")
        })?;
        if count == 0 {
            break;
        }
        hasher.update(&buffer[..count]);
    }
    Ok(hex::encode(hasher.finalize()))
}

// 1. Canonical XingShu V1 Package Adapter (.xspkg)
pub struct XspkgV1Adapter;

impl IPackageFormatAdapter for XspkgV1Adapter {
    fn format_name(&self) -> &str {
        "XingShu Canonical Package (.xspkg)"
    }

    fn can_handle(&self, filename: &str) -> bool {
        filename.to_lowercase().ends_with(".xspkg")
    }

    fn inspect(&self, path: &Path) -> HubResult<XspkgManifest> {
        let file = File::open(path).map_err(|e| {
            HubError::new("PACKAGE_OPEN_ERROR", format!("Cannot open package {}: {e}", path.display()), "PACKAGE")
        })?;
        let mut archive = ZipArchive::new(file).map_err(|e| {
            HubError::new("INVALID_ZIP_ARCHIVE", format!("Invalid zip archive: {e}"), "PACKAGE")
        })?;

        let manifest_content = {
            let entry_name = if archive.by_name("xspkg.json").is_ok() {
                "xspkg.json"
            } else if archive.by_name("manifest.json").is_ok() {
                "manifest.json"
            } else {
                return Err(HubError::new(
                    "MISSING_MANIFEST",
                    "Package missing required 'xspkg.json' manifest file",
                    "PACKAGE",
                ));
            };

            let mut manifest_entry = archive.by_name(entry_name).map_err(|e| {
                HubError::new("MANIFEST_ENTRY_ERROR", format!("Cannot read {entry_name}: {e}"), "PACKAGE")
            })?;
            let mut s = String::new();
            manifest_entry.read_to_string(&mut s).map_err(|e| {
                HubError::new("MANIFEST_READ_ERROR", format!("Failed to read manifest: {e}"), "PACKAGE")
            })?;
            s
        };

        let mut manifest: XspkgManifest = serde_json::from_str(&manifest_content).map_err(|e| {
            HubError::new("MANIFEST_PARSE_ERROR", format!("Corrupt xspkg manifest: {e}"), "PACKAGE")
        })?;

        manifest.checksum_sha256 = Some(calculate_file_sha256(path)?);
        Ok(manifest)
    }
}

// 2. Legacy XingShu Adapter (.xingshu)
pub struct LegacyXingShuAdapter;

impl IPackageFormatAdapter for LegacyXingShuAdapter {
    fn format_name(&self) -> &str {
        "Legacy XingShu Plugin Package (.xingshu)"
    }

    fn can_handle(&self, filename: &str) -> bool {
        filename.to_lowercase().ends_with(".xingshu")
    }

    fn inspect(&self, path: &Path) -> HubResult<XspkgManifest> {
        let file = File::open(path).map_err(|e| {
            HubError::new("PACKAGE_OPEN_ERROR", format!("Cannot open .xingshu package {}: {e}", path.display()), "PACKAGE")
        })?;
        let mut archive = ZipArchive::new(file).map_err(|e| {
            HubError::new("INVALID_ZIP_ARCHIVE", format!("Invalid .xingshu archive: {e}"), "PACKAGE")
        })?;

        let (manifest_str, entry_filename) = {
            if let Ok(mut entry) = archive.by_name("xingshu.json") {
                let mut s = String::new();
                entry.read_to_string(&mut s).ok();
                (s, "xingshu.json")
            } else if let Ok(mut entry) = archive.by_name("plugin.json") {
                let mut s = String::new();
                entry.read_to_string(&mut s).ok();
                (s, "plugin.json")
            } else {
                return Err(HubError::new(
                    "MISSING_LEGACY_MANIFEST",
                    "Legacy .xingshu package missing xingshu.json or plugin.json",
                    "PACKAGE",
                ));
            }
        };

        let val: serde_json::Value = serde_json::from_str(&manifest_str).map_err(|e| {
            HubError::new("LEGACY_MANIFEST_PARSE_ERROR", format!("Failed to parse legacy {entry_filename}: {e}"), "PACKAGE")
        })?;

        let package_id = val.get("id").or_else(|| val.get("plugin_id"))
            .and_then(|v| v.as_str()).unwrap_or("legacy.xingshu.plugin").to_string();
        let name = val.get("name").or_else(|| val.get("title"))
            .and_then(|v| v.as_str()).unwrap_or("Legacy XingShu Plugin").to_string();
        let version = val.get("version").and_then(|v| v.as_str()).unwrap_or("1.0.0").to_string();
        let author = val.get("author").and_then(|v| v.as_str()).unwrap_or("Unknown").to_string();
        let description = val.get("description").and_then(|v| v.as_str()).unwrap_or("Migrated from legacy XingShu package format.").to_string();
        let entry_dll = val.get("entry_dll").or_else(|| val.get("dll")).and_then(|v| v.as_str()).map(|s| s.to_string());

        let checksum = calculate_file_sha256(path)?;

        Ok(XspkgManifest {
            schema_version: XSPKG_CURRENT_SCHEMA,
            package_id,
            name,
            version,
            author,
            description,
            package_type: XspkgType::RuntimePlugin,
            entry_dll,
            target_game_version: None,
            min_runtime_version: Some("0.1.0".into()),
            dependencies: vec![],
            permissions: vec!["runtime_hook".into()],
            checksum_sha256: Some(checksum),
            signature: None,
            legacy_format_origin: Some("xingshu_v0_legacy".into()),
        })
    }
}

// 3. APMM / Generic Mod Package Adapter (.zip / .apmm)
pub struct ApmmPackageAdapter;

impl IPackageFormatAdapter for ApmmPackageAdapter {
    fn format_name(&self) -> &str {
        "APMM Mod / Generic Resource Archive (.zip, .apmm)"
    }

    fn can_handle(&self, filename: &str) -> bool {
        let lower = filename.to_lowercase();
        lower.ends_with(".apmm") || lower.ends_with(".zip")
    }

    fn inspect(&self, path: &Path) -> HubResult<XspkgManifest> {
        let file = File::open(path).map_err(|e| {
            HubError::new("PACKAGE_OPEN_ERROR", format!("Cannot open mod archive {}: {e}", path.display()), "PACKAGE")
        })?;
        let mut archive = ZipArchive::new(file).map_err(|e| {
            HubError::new("INVALID_ZIP_ARCHIVE", format!("Invalid zip archive: {e}"), "PACKAGE")
        })?;

        let mut mod_info: Option<serde_json::Value> = None;
        if let Ok(mut entry) = archive.by_name("modinfo.json") {
            let mut s = String::new();
            if entry.read_to_string(&mut s).is_ok() {
                mod_info = serde_json::from_str(&s).ok();
            }
        }

        let stem = path.file_stem().and_then(|s| s.to_str()).unwrap_or("mod");
        let package_id = format!("mod.apmm.{}", stem.to_lowercase().replace(' ', "_"));
        let name = mod_info.as_ref().and_then(|v| v.get("name")).and_then(|v| v.as_str()).unwrap_or(stem).to_string();
        let version = mod_info.as_ref().and_then(|v| v.get("version")).and_then(|v| v.as_str()).unwrap_or("1.0.0").to_string();
        let author = mod_info.as_ref().and_then(|v| v.get("author")).and_then(|v| v.as_str()).unwrap_or("Community Modder").to_string();
        let description = mod_info.as_ref().and_then(|v| v.get("description")).and_then(|v| v.as_str()).unwrap_or("Community Mod package imported via APMM adapter.").to_string();

        let checksum = calculate_file_sha256(path)?;

        Ok(XspkgManifest {
            schema_version: XSPKG_CURRENT_SCHEMA,
            package_id,
            name,
            version,
            author,
            description,
            package_type: XspkgType::Mod,
            entry_dll: None,
            target_game_version: None,
            min_runtime_version: None,
            dependencies: vec![],
            permissions: vec![],
            checksum_sha256: Some(checksum),
            signature: None,
            legacy_format_origin: Some("apmm_mod_legacy".into()),
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_adapter_can_handle() {
        let xspkg = XspkgV1Adapter;
        assert!(xspkg.can_handle("my_mod.xspkg"));
        assert!(xspkg.can_handle("test.XSPKG"));
        assert!(!xspkg.can_handle("my_mod.zip"));

        let xingshu = LegacyXingShuAdapter;
        assert!(xingshu.can_handle("legacy.xingshu"));
        assert!(xingshu.can_handle("plugin.XingShu"));
        assert!(!xingshu.can_handle("legacy.xspkg"));

        let apmm = ApmmPackageAdapter;
        assert!(apmm.can_handle("costume.apmm"));
        assert!(apmm.can_handle("mod.zip"));
        assert!(!apmm.can_handle("package.xspkg"));
    }
}
