use chrono::Utc;
use rusqlite::params;
use crate::domain::error::{HubError, HubResult};
use crate::domain::mod_package::ModPackage;
use crate::domain::plugin::{InstalledFile, Plugin};
use crate::storage::database::Database;

pub struct ExtensionRepository {
    db: Database,
}

impl ExtensionRepository {
    pub fn new(db: Database) -> Self {
        Self { db }
    }

    pub fn save_plugin(&self, plugin: &Plugin) -> HubResult<()> {
        self.db.with_conn(|conn| {
            conn.execute(
                "INSERT INTO plugins (
                    plugin_id, name, installed_version, status, install_path,
                    manifest_json, installed_at, updated_at
                ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)
                ON CONFLICT(plugin_id) DO UPDATE SET
                    name = excluded.name,
                    installed_version = excluded.installed_version,
                    status = excluded.status,
                    install_path = excluded.install_path,
                    manifest_json = excluded.manifest_json,
                    updated_at = excluded.updated_at;",
                params![
                    plugin.plugin_id,
                    plugin.name,
                    plugin.installed_version,
                    plugin.status,
                    plugin.install_path,
                    plugin.manifest_json.as_ref().map(|v| v.to_string()),
                    plugin.installed_at.map(|t| t.to_rfc3339()),
                    plugin.updated_at.map(|t| t.to_rfc3339()),
                ],
            )
            .map_err(|e| HubError::storage_error(format!("Failed to save plugin: {e}")))?;
            Ok(())
        })
    }

    pub fn list_plugins(&self) -> HubResult<Vec<Plugin>> {
        self.db.with_conn(|conn| {
            let mut stmt = conn
                .prepare("SELECT plugin_id, name, installed_version, status, install_path, manifest_json, installed_at, updated_at FROM plugins ORDER BY name ASC")
                .map_err(|e| HubError::storage_error(format!("Failed to prepare query: {e}")))?;

            let rows = stmt
                .query_map([], |row| {
                    let manifest_str: Option<String> = row.get(5).ok();
                    let installed_str: Option<String> = row.get(6).ok();
                    let updated_str: Option<String> = row.get(7).ok();

                    Ok(Plugin {
                        plugin_id: row.get(0).unwrap_or_default(),
                        name: row.get(1).unwrap_or_default(),
                        installed_version: row.get(2).ok(),
                        status: row.get(3).unwrap_or_default(),
                        install_path: row.get(4).ok(),
                        manifest_json: manifest_str.and_then(|s| serde_json::from_str(&s).ok()),
                        installed_at: installed_str.and_then(|s| chrono::DateTime::parse_from_rfc3339(&s).ok().map(|dt| dt.with_timezone(&Utc))),
                        updated_at: updated_str.and_then(|s| chrono::DateTime::parse_from_rfc3339(&s).ok().map(|dt| dt.with_timezone(&Utc))),
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

    pub fn delete_plugin(&self, plugin_id: &str) -> HubResult<()> {
        self.db.with_conn(|conn| {
            conn.execute("DELETE FROM plugins WHERE plugin_id = ?1", [plugin_id])
                .map_err(|e| HubError::storage_error(format!("Failed to delete plugin: {e}")))?;
            Ok(())
        })
    }

    pub fn save_mod(&self, mod_pkg: &ModPackage) -> HubResult<()> {
        self.db.with_conn(|conn| {
            conn.execute(
                "INSERT INTO mod_packages (
                    mod_id, name, installed_version, status, install_path,
                    manifest_json, installed_at, updated_at
                ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)
                ON CONFLICT(mod_id) DO UPDATE SET
                    name = excluded.name,
                    installed_version = excluded.installed_version,
                    status = excluded.status,
                    install_path = excluded.install_path,
                    manifest_json = excluded.manifest_json,
                    updated_at = excluded.updated_at;",
                params![
                    mod_pkg.mod_id,
                    mod_pkg.name,
                    mod_pkg.installed_version,
                    mod_pkg.status,
                    mod_pkg.install_path,
                    mod_pkg.manifest_json.as_ref().map(|v| v.to_string()),
                    mod_pkg.installed_at.map(|t| t.to_rfc3339()),
                    mod_pkg.updated_at.map(|t| t.to_rfc3339()),
                ],
            )
            .map_err(|e| HubError::storage_error(format!("Failed to save mod package: {e}")))?;
            Ok(())
        })
    }

    pub fn delete_mod(&self, mod_id: &str) -> HubResult<()> {
        self.db.with_conn(|conn| {
            conn.execute("DELETE FROM mod_packages WHERE mod_id = ?1", [mod_id])
                .map_err(|e| HubError::storage_error(e.to_string()))?;
            Ok(())
        })
    }

    pub fn list_mods(&self) -> HubResult<Vec<ModPackage>> {
        self.db.with_conn(|conn| {
            let mut stmt = conn
                .prepare("SELECT mod_id, name, installed_version, status, install_path, manifest_json, installed_at, updated_at FROM mod_packages ORDER BY name ASC")
                .map_err(|e| HubError::storage_error(format!("Failed to prepare query: {e}")))?;

            let rows = stmt
                .query_map([], |row| {
                    let manifest_str: Option<String> = row.get(5).ok();
                    let installed_str: Option<String> = row.get(6).ok();
                    let updated_str: Option<String> = row.get(7).ok();

                    Ok(ModPackage {
                        mod_id: row.get(0).unwrap_or_default(),
                        name: row.get(1).unwrap_or_default(),
                        installed_version: row.get(2).ok(),
                        status: row.get(3).unwrap_or_default(),
                        install_path: row.get(4).ok(),
                        manifest_json: manifest_str.and_then(|s| serde_json::from_str(&s).ok()),
                        installed_at: installed_str.and_then(|s| chrono::DateTime::parse_from_rfc3339(&s).ok().map(|dt| dt.with_timezone(&Utc))),
                        updated_at: updated_str.and_then(|s| chrono::DateTime::parse_from_rfc3339(&s).ok().map(|dt| dt.with_timezone(&Utc))),
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

    pub fn record_installed_file(&self, file: &InstalledFile) -> HubResult<()> {
        self.db.with_conn(|conn| {
            conn.execute(
                "INSERT INTO installed_files (target_path, package_id, package_version, installed_sha256, installed_at)
                 VALUES (?1, ?2, ?3, ?4, ?5)
                 ON CONFLICT(target_path) DO UPDATE SET
                    package_id = excluded.package_id,
                    package_version = excluded.package_version,
                    installed_sha256 = excluded.installed_sha256,
                    installed_at = excluded.installed_at;",
                params![
                    file.target_path,
                    file.package_id,
                    file.package_version,
                    file.installed_sha256,
                    file.installed_at.to_rfc3339(),
                ],
            )
            .map_err(|e| HubError::storage_error(format!("Failed to record installed file: {e}")))?;
            Ok(())
        })
    }

    pub fn get_installed_files_by_package(&self, package_id: &str) -> HubResult<Vec<InstalledFile>> {
        self.db.with_conn(|conn| {
            let mut stmt = conn
                .prepare("SELECT target_path, package_id, package_version, installed_sha256, installed_at FROM installed_files WHERE package_id = ?1")
                .map_err(|e| HubError::storage_error(format!("Failed to prepare query: {e}")))?;

            let rows = stmt
                .query_map([package_id], |row| {
                    let installed_str: String = row.get(4).unwrap_or_default();
                    Ok(InstalledFile {
                        target_path: row.get(0).unwrap_or_default(),
                        package_id: row.get(1).unwrap_or_default(),
                        package_version: row.get(2).unwrap_or_default(),
                        installed_sha256: row.get(3).unwrap_or_default(),
                        installed_at: chrono::DateTime::parse_from_rfc3339(&installed_str).ok().map(|dt| dt.with_timezone(&Utc)).unwrap_or_else(Utc::now),
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

    pub fn delete_installed_files_by_package(&self, package_id: &str) -> HubResult<()> {
        self.db.with_conn(|conn| {
            conn.execute("DELETE FROM installed_files WHERE package_id = ?1", [package_id])
                .map_err(|e| HubError::storage_error(format!("Failed to delete installed files: {e}")))?;
            Ok(())
        })
    }
}
