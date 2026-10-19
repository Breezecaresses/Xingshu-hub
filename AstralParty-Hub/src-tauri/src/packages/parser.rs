use std::fs::File;
use std::io::Read;
use std::path::Path;
use zip::ZipArchive;
use crate::domain::error::{HubError, HubResult};
use crate::packages::manifest::HubPackageManifestV1;
use crate::packages::validator::ManifestValidator;
use crate::security::archive_guard::{ArchiveGuard, ArchiveLimits};

pub struct PackageParser;

impl PackageParser {
    pub fn parse_archive<P: AsRef<Path>>(zip_path: P) -> HubResult<HubPackageManifestV1> {
        let path = zip_path.as_ref();

        // 1. Enforce archive safety thresholds (Zip Bomb guard)
        ArchiveGuard::inspect_zip(path, &ArchiveLimits::default())?;

        // 2. Open archive and locate manifest.json
        let file = File::open(path).map_err(|e| {
            HubError::new("ARCHIVE_OPEN_FAILED", format!("Failed to open package: {e}"), "PACKAGE")
        })?;
        let mut archive = ZipArchive::new(file).map_err(|e| {
            HubError::new("ARCHIVE_PARSE_FAILED", format!("Failed to read zip archive: {e}"), "PACKAGE")
        })?;

        let mut manifest_str = String::new();
        {
            let mut manifest_entry = archive
                .by_name("manifest.json")
                .map_err(|_| HubError::invalid_manifest("Archive is missing required 'manifest.json' file."))?;

            manifest_entry
                .read_to_string(&mut manifest_str)
                .map_err(|e| HubError::invalid_manifest(format!("Failed to read manifest.json content: {e}")))?;
        }

        // 3. Deserialize and validate
        let manifest: HubPackageManifestV1 = serde_json::from_str(&manifest_str).map_err(|e| {
            HubError::invalid_manifest(format!("Failed to parse manifest.json: {e}"))
        })?;

        ManifestValidator::validate(&manifest)?;

        // 4. Verify that all declared sources actually exist inside the archive
        for file in &manifest.files {
            let normalized_source = file.source.replace('\\', "/");
            let found = (0..archive.len()).any(|idx| {
                if let Ok(entry) = archive.by_index(idx) {
                    entry.name().replace('\\', "/") == normalized_source
                } else {
                    false
                }
            });

            if !found {
                return Err(HubError::invalid_manifest(format!(
                    "Declared source file '{}' was not found in package archive.",
                    file.source
                )));
            }
        }

        Ok(manifest)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_parse_sample_plugin_zip() {
        let zip_path = Path::new("../examples/sample-plugin.zip");
        if zip_path.exists() {
            let res = PackageParser::parse_archive(zip_path);
            assert!(res.is_ok(), "Failed to parse sample-plugin.zip: {:?}", res.err());
            let manifest = res.unwrap();
            assert_eq!(manifest.package.id, "community.sample-plugin");
            assert_eq!(manifest.files.len(), 1);
        }
    }
}

