use chrono::Utc;
use rusqlite::params;
use crate::domain::error::{HubError, HubResult};
use crate::domain::installation::GameInstallation;
use crate::storage::database::Database;

pub struct GameRepository {
    db: Database,
}

impl GameRepository {
    pub fn new(db: Database) -> Self {
        Self { db }
    }

    pub fn save(&self, inst: &GameInstallation) -> HubResult<()> {
        self.db.with_conn(|conn| {
            conn.execute(
                "INSERT INTO game_installations (
                    id, platform, platform_app_id, install_path, executable_path,
                    detected_build_id, detected_version, detection_method,
                    is_valid, is_primary, last_verified_at, created_at, updated_at
                ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13)
                ON CONFLICT(id) DO UPDATE SET
                    platform = excluded.platform,
                    platform_app_id = excluded.platform_app_id,
                    install_path = excluded.install_path,
                    executable_path = excluded.executable_path,
                    detected_build_id = excluded.detected_build_id,
                    detected_version = excluded.detected_version,
                    detection_method = excluded.detection_method,
                    is_valid = excluded.is_valid,
                    is_primary = excluded.is_primary,
                    last_verified_at = excluded.last_verified_at,
                    updated_at = excluded.updated_at;",
                params![
                    inst.id,
                    inst.platform,
                    inst.platform_app_id,
                    inst.install_path,
                    inst.executable_path,
                    inst.detected_build_id,
                    inst.detected_version,
                    inst.detection_method,
                    inst.is_valid as i32,
                    inst.is_primary as i32,
                    inst.last_verified_at.map(|t| t.to_rfc3339()),
                    inst.created_at.to_rfc3339(),
                    inst.updated_at.to_rfc3339(),
                ],
            )
            .map_err(|e| HubError::storage_error(format!("Failed to save game installation: {e}")))?;
            Ok(())
        })
    }

    pub fn get_primary(&self) -> HubResult<Option<GameInstallation>> {
        self.db.with_conn(|conn| {
            let mut stmt = conn
                .prepare("SELECT id, platform, platform_app_id, install_path, executable_path,
                                 detected_build_id, detected_version, detection_method,
                                 is_valid, is_primary, last_verified_at, created_at, updated_at
                          FROM game_installations
                          ORDER BY is_primary DESC, updated_at DESC
                          LIMIT 1")
                .map_err(|e| HubError::storage_error(format!("Failed to prepare query: {e}")))?;

            let mut rows = stmt
                .query([])
                .map_err(|e| HubError::storage_error(format!("Query failed: {e}")))?;

            if let Some(row) = rows.next().map_err(|e| HubError::storage_error(format!("Row read failed: {e}")))? {
                let last_verified_str: Option<String> = row.get(10).ok();
                let created_str: String = row.get(11).unwrap_or_default();
                let updated_str: String = row.get(12).unwrap_or_default();

                Ok(Some(GameInstallation {
                    id: row.get(0).unwrap_or_default(),
                    platform: row.get(1).unwrap_or_default(),
                    platform_app_id: row.get(2).ok(),
                    install_path: row.get(3).unwrap_or_default(),
                    executable_path: row.get(4).ok(),
                    detected_build_id: row.get(5).ok(),
                    detected_version: row.get(6).ok(),
                    detection_method: row.get(7).unwrap_or_default(),
                    is_valid: row.get::<_, i32>(8).unwrap_or(0) != 0,
                    is_primary: row.get::<_, i32>(9).unwrap_or(0) != 0,
                    last_verified_at: last_verified_str.and_then(|s| chrono::DateTime::parse_from_rfc3339(&s).ok().map(|dt| dt.with_timezone(&Utc))),
                    created_at: chrono::DateTime::parse_from_rfc3339(&created_str).ok().map(|dt| dt.with_timezone(&Utc)).unwrap_or_else(Utc::now),
                    updated_at: chrono::DateTime::parse_from_rfc3339(&updated_str).ok().map(|dt| dt.with_timezone(&Utc)).unwrap_or_else(Utc::now),
                }))
            } else {
                Ok(None)
            }
        })
    }

    pub fn list_all(&self) -> HubResult<Vec<GameInstallation>> {
        self.db.with_conn(|conn| {
            let mut stmt = conn
                .prepare("SELECT id, platform, platform_app_id, install_path, executable_path,
                                 detected_build_id, detected_version, detection_method,
                                 is_valid, is_primary, last_verified_at, created_at, updated_at
                          FROM game_installations
                          ORDER BY is_primary DESC, updated_at DESC")
                .map_err(|e| HubError::storage_error(format!("Failed to prepare query: {e}")))?;

            let rows = stmt
                .query_map([], |row| {
                    let last_verified_str: Option<String> = row.get(10).ok();
                    let created_str: String = row.get(11).unwrap_or_default();
                    let updated_str: String = row.get(12).unwrap_or_default();

                    Ok(GameInstallation {
                        id: row.get(0).unwrap_or_default(),
                        platform: row.get(1).unwrap_or_default(),
                        platform_app_id: row.get(2).ok(),
                        install_path: row.get(3).unwrap_or_default(),
                        executable_path: row.get(4).ok(),
                        detected_build_id: row.get(5).ok(),
                        detected_version: row.get(6).ok(),
                        detection_method: row.get(7).unwrap_or_default(),
                        is_valid: row.get::<_, i32>(8).unwrap_or(0) != 0,
                        is_primary: row.get::<_, i32>(9).unwrap_or(0) != 0,
                        last_verified_at: last_verified_str.and_then(|s| chrono::DateTime::parse_from_rfc3339(&s).ok().map(|dt| dt.with_timezone(&Utc))),
                        created_at: chrono::DateTime::parse_from_rfc3339(&created_str).ok().map(|dt| dt.with_timezone(&Utc)).unwrap_or_else(Utc::now),
                        updated_at: chrono::DateTime::parse_from_rfc3339(&updated_str).ok().map(|dt| dt.with_timezone(&Utc)).unwrap_or_else(Utc::now),
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
