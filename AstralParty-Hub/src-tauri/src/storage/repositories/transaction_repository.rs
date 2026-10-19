use chrono::Utc;
use rusqlite::params;
use crate::domain::error::{HubError, HubResult};
use crate::domain::installation::{InstallTransaction, InstallTransactionStep};
use crate::storage::database::Database;

pub struct TransactionRepository {
    db: Database,
}

impl TransactionRepository {
    pub fn new(db: Database) -> Self {
        Self { db }
    }

    pub fn create_transaction(&self, tx: &InstallTransaction) -> HubResult<()> {
        self.db.with_conn(|conn| {
            conn.execute(
                "INSERT INTO install_transactions (
                    id, package_id, package_version, operation, state,
                    game_installation_id, backup_id, started_at, finished_at,
                    error_code, error_message
                ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11)",
                params![
                    tx.id,
                    tx.package_id,
                    tx.package_version,
                    tx.operation,
                    tx.state,
                    tx.game_installation_id,
                    tx.backup_id,
                    tx.started_at.to_rfc3339(),
                    tx.finished_at.map(|t| t.to_rfc3339()),
                    tx.error_code,
                    tx.error_message,
                ],
            )
            .map_err(|e| HubError::storage_error(format!("Failed to create transaction record: {e}")))?;
            Ok(())
        })
    }

    pub fn update_state(&self, tx_id: &str, state: &str, finished_at: Option<chrono::DateTime<Utc>>, error_code: Option<&str>, error_message: Option<&str>) -> HubResult<()> {
        self.db.with_conn(|conn| {
            conn.execute(
                "UPDATE install_transactions
                 SET state = ?1, finished_at = ?2, error_code = ?3, error_message = ?4
                 WHERE id = ?5",
                params![
                    state,
                    finished_at.map(|t| t.to_rfc3339()),
                    error_code,
                    error_message,
                    tx_id,
                ],
            )
            .map_err(|e| HubError::storage_error(format!("Failed to update transaction state: {e}")))?;
            Ok(())
        })
    }

    pub fn record_step(&self, step: &InstallTransactionStep) -> HubResult<()> {
        self.db.with_conn(|conn| {
            conn.execute(
                "INSERT INTO install_transaction_steps (
                    id, transaction_id, step_index, step_name, state, started_at, finished_at, detail_json
                ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)",
                params![
                    step.id,
                    step.transaction_id,
                    step.step_index,
                    step.step_name,
                    step.state,
                    step.started_at.map(|t| t.to_rfc3339()),
                    step.finished_at.map(|t| t.to_rfc3339()),
                    step.detail_json.as_ref().map(|v| v.to_string()),
                ],
            )
            .map_err(|e| HubError::storage_error(format!("Failed to record transaction step: {e}")))?;
            Ok(())
        })
    }

    pub fn get_incomplete_transactions(&self) -> HubResult<Vec<InstallTransaction>> {
        self.db.with_conn(|conn| {
            let mut stmt = conn
                .prepare(
                    "SELECT id, package_id, package_version, operation, state,
                            game_installation_id, backup_id, started_at, finished_at,
                            error_code, error_message
                     FROM install_transactions
                     WHERE state NOT IN ('COMPLETED', 'ROLLED_BACK', 'FAILED')
                     ORDER BY started_at DESC",
                )
                .map_err(|e| HubError::storage_error(format!("Failed to prepare query: {e}")))?;

            let rows = stmt
                .query_map([], |row| {
                    let started_str: String = row.get(7).unwrap_or_default();
                    let finished_str: Option<String> = row.get(8).ok();

                    Ok(InstallTransaction {
                        id: row.get(0).unwrap_or_default(),
                        package_id: row.get(1).unwrap_or_default(),
                        package_version: row.get(2).unwrap_or_default(),
                        operation: row.get(3).unwrap_or_default(),
                        state: row.get(4).unwrap_or_default(),
                        game_installation_id: row.get(5).ok(),
                        backup_id: row.get(6).ok(),
                        started_at: chrono::DateTime::parse_from_rfc3339(&started_str).ok().map(|dt| dt.with_timezone(&Utc)).unwrap_or_else(Utc::now),
                        finished_at: finished_str.and_then(|s| chrono::DateTime::parse_from_rfc3339(&s).ok().map(|dt| dt.with_timezone(&Utc))),
                        error_code: row.get(9).ok(),
                        error_message: row.get(10).ok(),
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

    pub fn list_recent(&self, limit: usize) -> HubResult<Vec<InstallTransaction>> {
        self.db.with_conn(|conn| {
            let mut stmt = conn
                .prepare(
                    "SELECT id, package_id, package_version, operation, state,
                            game_installation_id, backup_id, started_at, finished_at,
                            error_code, error_message
                     FROM install_transactions
                     ORDER BY started_at DESC
                     LIMIT ?1",
                )
                .map_err(|e| HubError::storage_error(format!("Failed to prepare query: {e}")))?;

            let rows = stmt
                .query_map([limit], |row| {
                    let started_str: String = row.get(7).unwrap_or_default();
                    let finished_str: Option<String> = row.get(8).ok();

                    Ok(InstallTransaction {
                        id: row.get(0).unwrap_or_default(),
                        package_id: row.get(1).unwrap_or_default(),
                        package_version: row.get(2).unwrap_or_default(),
                        operation: row.get(3).unwrap_or_default(),
                        state: row.get(4).unwrap_or_default(),
                        game_installation_id: row.get(5).ok(),
                        backup_id: row.get(6).ok(),
                        started_at: chrono::DateTime::parse_from_rfc3339(&started_str).ok().map(|dt| dt.with_timezone(&Utc)).unwrap_or_else(Utc::now),
                        finished_at: finished_str.and_then(|s| chrono::DateTime::parse_from_rfc3339(&s).ok().map(|dt| dt.with_timezone(&Utc))),
                        error_code: row.get(9).ok(),
                        error_message: row.get(10).ok(),
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
