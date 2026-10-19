use std::fs;
use std::path::PathBuf;
use crate::domain::error::{HubError, HubResult};
use crate::install::gates::Sha256Gate;
use crate::packages::manifest::HubPackageManifestV1;

pub struct PostVerifier;

impl PostVerifier {
    pub fn verify_committed_files(
        manifest: &HubPackageManifestV1,
        resolved_targets: &[(String, PathBuf)],
    ) -> HubResult<()> {
        for (logical_target, target_path) in resolved_targets {
            if !target_path.exists() {
                return Err(HubError::new(
                    "POST_VERIFY_MISSING_FILE",
                    format!("Committed file '{}' was not found on disk at '{}'", logical_target, target_path.display()),
                    "VERIFICATION",
                ));
            }

            // Find matching file entry in manifest
            if let Some(entry) = manifest.files.iter().find(|f| f.target == *logical_target) {
                // Verify size
                let metadata = fs::metadata(target_path).map_err(|e| {
                    HubError::new("POST_VERIFY_IO_ERROR", format!("Failed to read metadata for '{}': {e}", target_path.display()), "VERIFICATION")
                })?;
                if metadata.len() != entry.size {
                    return Err(HubError::new(
                        "POST_VERIFY_SIZE_MISMATCH",
                        format!("Committed file size mismatch for '{}'. Expected: {} bytes, Actual: {} bytes", logical_target, entry.size, metadata.len()),
                        "VERIFICATION",
                    ));
                }

                // Verify SHA256 checksum
                Sha256Gate::verify_file(target_path, &entry.sha256)?;
            }
        }

        Ok(())
    }
}
