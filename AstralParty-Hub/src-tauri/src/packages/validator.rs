use crate::domain::error::{HubError, HubResult};
use crate::packages::manifest::HubPackageManifestV1;
use crate::security::path_guard::PathGuard;
use crate::security::permissions::PermissionsGuard;

pub struct ManifestValidator;

impl ManifestValidator {
    pub fn validate(manifest: &HubPackageManifestV1) -> HubResult<()> {
        if manifest.schema_version != 1 {
            return Err(HubError::invalid_manifest(format!(
                "Unsupported manifest schema version: {}. Expected version: 1",
                manifest.schema_version
            )));
        }

        if !PermissionsGuard::assert_valid_package_type(&manifest.package.package_type) {
            return Err(HubError::invalid_manifest(format!(
                "Invalid package type '{}'. Allowed: 'xingshu-plugin', 'mod', 'resource-pack'",
                manifest.package.package_type
            )));
        }

        if manifest.package.id.trim().is_empty() {
            return Err(HubError::invalid_manifest("Package ID cannot be empty."));
        }

        if manifest.files.is_empty() {
            return Err(HubError::invalid_manifest("Package contains no file declarations."));
        }

        for file in &manifest.files {
            // Validate logical target prefix
            if !PermissionsGuard::assert_valid_logical_prefix(&file.target) {
                return Err(HubError::invalid_manifest(format!(
                    "Invalid logical target prefix in '{}'. Must start with 'xingshu/plugins/', 'game/mods/', 'game/resources/', or 'hub/data/'",
                    file.target
                )));
            }

            // Path traversal safety
            PathGuard::sanitize_relative_path(&file.source)?;
            PathGuard::sanitize_relative_path(&file.target)?;

            // Validate SHA256 format
            if file.sha256.len() != 64 || !file.sha256.chars().all(|c| c.is_ascii_hexdigit()) {
                return Err(HubError::invalid_manifest(format!(
                    "Invalid SHA256 checksum for file '{}'",
                    file.source
                )));
            }
        }

        Ok(())
    }
}
