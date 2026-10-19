use chrono::Utc;
use rusqlite::params;
use crate::domain::error::{HubError, HubResult};
use crate::domain::installation::Backup;
use crate::storage::database::Database;

pub struct BackupRepository {
    db: Database,
}

impl BackupRepository {
    pub fn new(db: Database) -> Self {
        Self { db }
    }

    pub fn save(&self, backup: &Backup) -> HubResult<()> {
        self.db.with_conn(|conn| {
            conn.execute(
                "INSERT INTO backups (
                    id, backup_type, target_id, storage_path, manifest_json,
                    created_at, verified_at, status
                ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)",
                params![
                    backup.id,
                    backup.backup_type,
                    backup.target_id,
                    backup.storage_path,
                    backup.manifest_json.to_string(),
                    backup.created_at.to_rfc3339(),
                    backup.verified_at.map(|t| t.to_rfc3339()),
                    backup.status,
                ],
            )
            .map_err(|e| HubError::storage_error(format!("Failed to record backup: {e}")))?;
            Ok(())
        })
    }

    pub fn get_by_id(&self, id: &str) -> HubResult<Option<Backup>> {
        self.db.with_conn(|conn| {
            let mut stmt = conn
                .prepare("SELECT id, backup_type, target_id, storage_path, manifest_json, created_at, verified_at, status FROM backups WHERE id = ?1")
                .map_err(|e| HubError::storage_error(format!("Failed to prepare query: {e}")))?;

            let mut rows = stmt
                .query([id])
                .map_err(|e| HubError::storage_error(format!("Query failed: {e}")))?;

            if let Some(row) = rows.next().map_err(|e| HubError::storage_error(format!("Row read failed: {e}")))? {
                let manifest_str: String = row.get(4).unwrap_or_default();
                let created_str: String = row.get(5).unwrap_or_default();
                let verified_str: Option<String> = row.get(6).ok();

                Ok(Some(Backup {
                    id: row.get(0).unwrap_or_default(),
                    backup_type: row.get(1).unwrap_or_default(),
                    target_id: row.get(2).ok(),
                    storage_path: row.get(3).unwrap_or_default(),
                    manifest_json: serde_json::from_str(&manifest_str).unwrap_or(serde_json::Value::Null),
                    created_at: chrono::DateTime::parse_from_rfc3339(&created_str).ok().map(|dt| dt.with_timezone(&Utc)).unwrap_or_else(Utc::now),
                    verified_at: verified_str.and_then(|s| chrono::DateTime::parse_from_rfc3339(&s).ok().map(|dt| dt.with_timezone(&Utc))),
                    status: row.get(7).unwrap_or_default(),
                }))
            } else {
                Ok(None)
            }
        })
    }

    pub fn list_all(&self) -> HubResult<Vec<Backup>> {
        self.db.with_conn(|conn| {
            let mut stmt = conn
                .prepare("SELECT id, backup_type, target_id, storage_path, manifest_json, created_at, verified_at, status FROM backups ORDER BY created_at DESC")
                .map_err(|e| HubError::storage_error(format!("Failed to prepare query: {e}")))?;

            let rows = stmt
                .query_map([], |row| {
                    let manifest_str: String = row.get(4).unwrap_or_default();
                    let created_str: String = row.get(5).unwrap_or_default();
                    let verified_str: Option<String> = row.get(6).ok();

                    Ok(Backup {
                        id: row.get(0).unwrap_or_default(),
                        backup_type: row.get(1).unwrap_or_default(),
                        target_id: row.get(2).ok(),
                        storage_path: row.get(3).unwrap_or_default(),
                        manifest_json: serde_json::from_str(&manifest_str).unwrap_or(serde_json::Value::Null),
                        created_at: chrono::DateTime::parse_from_rfc3339(&created_str).ok().map(|dt| dt.with_timezone(&Utc)).unwrap_or_else(Utc::now),
                        verified_at: verified_str.and_then(|s| chrono::DateTime::parse_from_rfc3339(&s).ok().map(|dt| dt.with_timezone(&Utc))),
                        status: row.get(7).unwrap_or_default(),
                    })
                })
                .map_err(|e| HubError::storage_error(format!("Query failed: {e}")))?;

            let mut list = Vec::new();
            for r in rows {
                if let Ok(item) = r {
                    list.push(item);
                }
            }
            Ok(list)
        })
    }
}
