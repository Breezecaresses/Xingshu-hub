use std::fs;
use std::path::PathBuf;
use chrono::Utc;
use crate::domain::error::{HubError, HubResult};
use crate::domain::plugin::InstalledFile;
use crate::security::hash::compute_file_sha256;
use crate::storage::repositories::ExtensionRepository;

pub struct AtomicReplacer;

impl AtomicReplacer {
    pub fn commit_staged_files(
        staged_items: &[(PathBuf, PathBuf)], // (staged_source_path, target_dest_path)
        package_id: &str,
        package_version: &str,
        extension_repo: &ExtensionRepository,
    ) -> HubResult<()> {
        for (staged_source, target_dest) in staged_items {
            if let Some(parent) = target_dest.parent() {
                fs::create_dir_all(parent).map_err(|e| {
                    HubError::new("COMMIT_IO_FAILED", format!("Failed to create destination directories: {e}"), "INSTALL")
                })?;
            }

            // Copy file from staging to target destination
            fs::copy(staged_source, target_dest).map_err(|e| {
                HubError::new("COMMIT_IO_FAILED", format!("Failed to write file to '{}': {e}", target_dest.display()), "INSTALL")
            })?;

            // Compute installed checksum
            let sha256 = compute_file_sha256(target_dest)?;

            // Record ownership in installed_files table
            let record = InstalledFile {
                target_path: target_dest.display().to_string(),
                package_id: package_id.to_string(),
                package_version: package_version.to_string(),
                installed_sha256: sha256,
                installed_at: Utc::now(),
            };
            extension_repo.record_installed_file(&record)?;
        }

        Ok(())
    }
}
