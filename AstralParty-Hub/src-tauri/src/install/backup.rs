use chrono::Utc;
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Path, PathBuf};
use crate::domain::error::{HubError, HubResult};
use crate::domain::installation::Backup;
use crate::security::hash::compute_file_sha256;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BackupFileRecord {
    pub logical_target: String,
    pub original_path: String,
    pub backup_path: Option<String>,
    pub original_sha256: Option<String>,
    pub size: Option<u64>,
    pub existed_before: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BackupManifestData {
    pub transaction_id: String,
    pub package_id: String,
    pub created_at: String,
    pub files: Vec<BackupFileRecord>,
}

pub struct BackupEngine;

impl BackupEngine {
    pub fn create_pre_install_backup(
        backup_root: &Path,
        transaction_id: &str,
        package_id: &str,
        resolved_targets: &[(String, PathBuf)], // (logical_target, absolute_disk_path)
    ) -> HubResult<Backup> {
        let tx_backup_dir = backup_root.join(transaction_id);
        fs::create_dir_all(&tx_backup_dir).map_err(|e| {
            HubError::new("BACKUP_IO_FAILED", format!("Failed to create backup directory: {e}"), "INSTALL")
        })?;

        let mut file_records = Vec::new();

        for (idx, (logical_target, target_path)) in resolved_targets.iter().enumerate() {
            if target_path.exists() && target_path.is_file() {
                let backup_file_name = format!("file_{idx}_{}", target_path.file_name().and_then(|n| n.to_str()).unwrap_or("backup.bin"));
                let backup_dest = tx_backup_dir.join(&backup_file_name);

                fs::copy(target_path, &backup_dest).map_err(|e| {
                    HubError::new("BACKUP_IO_FAILED", format!("Failed to copy file for backup: {e}"), "INSTALL")
                })?;

                let sha256 = compute_file_sha256(&backup_dest)?;
                let metadata = fs::metadata(&backup_dest).ok();
                let size = metadata.map(|m| m.len());

                file_records.push(BackupFileRecord {
                    logical_target: logical_target.clone(),
                    original_path: target_path.display().to_string(),
                    backup_path: Some(backup_dest.display().to_string()),
                    original_sha256: Some(sha256),
                    size,
                    existed_before: true,
                });
            } else {
                file_records.push(BackupFileRecord {
                    logical_target: logical_target.clone(),
                    original_path: target_path.display().to_string(),
                    backup_path: None,
                    original_sha256: None,
                    size: None,
                    existed_before: false,
                });
            }
        }

        let manifest_data = BackupManifestData {
            transaction_id: transaction_id.to_string(),
            package_id: package_id.to_string(),
            created_at: Utc::now().to_rfc3339(),
            files: file_records,
        };

        let manifest_json = serde_json::to_value(&manifest_data).unwrap_or(serde_json::Value::Null);

        // Write backup manifest to disk inside backup folder
        let manifest_disk_path = tx_backup_dir.join("backup_manifest.json");
        fs::write(&manifest_disk_path, serde_json::to_string_pretty(&manifest_data).unwrap_or_default()).ok();

        Ok(Backup {
            id: transaction_id.to_string(),
            backup_type: "PRE_INSTALL".into(),
            target_id: Some(package_id.to_string()),
            storage_path: tx_backup_dir.display().to_string(),
            manifest_json,
            created_at: Utc::now(),
            verified_at: Some(Utc::now()),
            status: "VERIFIED".into(),
        })
    }
}
