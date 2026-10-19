use rusqlite::Connection;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use tracing::info;
use crate::domain::error::{HubError, HubResult};
use crate::storage::migrations::run_migrations;

#[derive(Clone)]
pub struct Database {
    conn: Arc<Mutex<Connection>>,
    db_path: PathBuf,
}

impl Database {
    pub fn init<P: AsRef<Path>>(path: P) -> HubResult<Self> {
        let path = path.as_ref();
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent).map_err(|e| {
                HubError::storage_error(format!("Failed to create database parent directory: {e}"))
            })?;
        }

        info!("Initializing SQLite database at: {}", path.display());
        let mut conn = Connection::open(path).map_err(|e| {
            HubError::storage_error(format!("Failed to open SQLite database: {e}"))
        })?;

        // Configure WAL mode and timeout for concurrency resilience
        conn.execute_batch(
            "PRAGMA journal_mode = WAL;
             PRAGMA busy_timeout = 5000;
             PRAGMA foreign_keys = ON;
             PRAGMA synchronous = NORMAL;",
        )
        .map_err(|e| HubError::storage_error(format!("Failed to set database PRAGMAs: {e}")))?;

        run_migrations(&mut conn)?;

        Ok(Self {
            conn: Arc::new(Mutex::new(conn)),
            db_path: path.to_path_buf(),
        })
    }

    pub fn in_memory() -> HubResult<Self> {
        let mut conn = Connection::open_in_memory().map_err(|e| {
            HubError::storage_error(format!("Failed to open in-memory SQLite database: {e}"))
        })?;
        conn.execute_batch("PRAGMA foreign_keys = ON;")
            .map_err(|e| HubError::storage_error(format!("Failed to set database PRAGMAs: {e}")))?;

        run_migrations(&mut conn)?;

        Ok(Self {
            conn: Arc::new(Mutex::new(conn)),
            db_path: PathBuf::from(":memory:"),
        })
    }

    pub fn with_conn<F, T>(&self, f: F) -> HubResult<T>
    where
        F: FnOnce(&mut Connection) -> HubResult<T>,
    {
        let mut guard = self.conn.lock().map_err(|e| {
            HubError::storage_error(format!("Database lock contention: {e}"))
        })?;
        f(&mut guard)
    }

    pub fn path(&self) -> &Path {
        &self.db_path
    }
}
