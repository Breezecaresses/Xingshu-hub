use std::path::{Path, PathBuf};
use crate::domain::error::{HubError, HubResult};
use crate::domain::installation::{Backup, InstallTransaction};
use crate::install::engine::InstallEngine;
use crate::install::rollback::RollbackEngine;
use crate::storage::database::Database;
use crate::storage::repositories::{BackupRepository, ExtensionRepository, GameRepository, TransactionRepository};

pub struct InstallService {
    engine: InstallEngine,
    game_repo: GameRepository,
    backup_repo: BackupRepository,
    tx_repo: TransactionRepository,
    extension_repo: ExtensionRepository,
}

impl InstallService {
    pub fn new(db: Database, backup_storage_root: PathBuf) -> Self {
        Self {
            engine: InstallEngine::new(db.clone(), backup_storage_root),
            game_repo: GameRepository::new(db.clone()),
            backup_repo: BackupRepository::new(db.clone()),
            tx_repo: TransactionRepository::new(db.clone()),
            extension_repo: ExtensionRepository::new(db),
        }
    }

    pub fn install_package_from_zip<P: AsRef<Path>>(&self, zip_path: P) -> HubResult<InstallTransaction> {
        let primary_game = self.game_repo.get_primary()?.ok_or_else(|| {
            HubError::game_not_found("No primary Astral Party installation configured. Please detect or set game path in Settings.")
        })?;

        self.engine.install_package(zip_path, &primary_game)
    }

    pub fn rollback_transaction(&self, transaction_id: &str) -> HubResult<()> {
        let backup = self.backup_repo.get_by_id(transaction_id)?.ok_or_else(|| {
            HubError::transaction_error(format!("No backup found matching transaction ID '{transaction_id}'"))
        })?;

        RollbackEngine::execute_rollback(&backup, &self.extension_repo)?;
        self.tx_repo.update_state(
            transaction_id,
            "ROLLED_BACK",
            Some(chrono::Utc::now()),
            Some("USER_ROLLBACK"),
            Some("User manually requested transaction rollback."),
        )?;
        Ok(())
    }

    pub fn list_transactions(&self, limit: usize) -> HubResult<Vec<InstallTransaction>> {
        self.tx_repo.list_recent(limit)
    }

    pub fn list_backups(&self) -> HubResult<Vec<Backup>> {
        self.backup_repo.list_all()
    }

    pub fn recover_crashes(&self) -> HubResult<Vec<String>> {
        self.engine.recover_interrupted_transactions()
    }
}
