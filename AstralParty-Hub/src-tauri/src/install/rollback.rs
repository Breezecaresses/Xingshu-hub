use std::fs;
use std::path::Path;
use tracing::{info, warn};
use crate::domain::error::{HubError, HubResult};
use crate::domain::installation::Backup;
use crate::install::backup::BackupManifestData;
use crate::storage::repositories::ExtensionRepository;

pub struct RollbackEngine;

impl RollbackEngine {
    pub fn execute_rollback(
        backup: &Backup,
        extension_repo: &ExtensionRepository,
    ) -> HubResult<()> {
        info!("Initiating rollback for transaction: {}", backup.id);

        let manifest_data: BackupManifestData = serde_json::from_value(backup.manifest_json.clone()).map_err(|e| {
            HubError::transaction_error(format!("Failed to parse backup manifest for rollback: {e}"))
        })?;

        for record in &manifest_data.files {
            let target_path = Path::new(&record.original_path);

            if record.existed_before {
                if let Some(backup_path_str) = &record.backup_path {
                    let backup_file = Path::new(backup_path_str);
                    if backup_file.exists() {
                        info!("Restoring original file: {} -> {}", backup_file.display(), target_path.display());
                        if let Some(parent) = target_path.parent() {
                            fs::create_dir_all(parent).ok();
                        }
                        fs::copy(backup_file, target_path).map_err(|e| {
                            HubError::transaction_error(format!("Failed to restore original file during rollback: {e}"))
                        })?;
                    } else {
                        warn!("Backup file '{}' missing during rollback attempt!", backup_path_str);
                    }
                }
            } else {
                // File was newly added by the transaction; delete it to cleanly restore prior state
                if target_path.exists() {
                    info!("Removing newly added file during rollback: {}", target_path.display());
                    fs::remove_file(target_path).ok();
                }
            }
        }

        // Clean up installed_files entries for this package
        extension_repo.delete_installed_files_by_package(&manifest_data.package_id)?;

        info!("Rollback successfully completed for transaction: {}", backup.id);
        Ok(())
    }
}
