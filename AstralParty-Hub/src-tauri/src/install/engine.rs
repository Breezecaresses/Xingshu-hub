use chrono::Utc;
use std::path::{Path, PathBuf};
use tracing::{error, info, warn};
use uuid::Uuid;

use crate::domain::error::{HubError, HubResult};
use crate::domain::installation::{GameInstallation, InstallTransaction, InstallTransactionStep, TransactionState};
use crate::domain::mod_package::ModPackage;
use crate::domain::plugin::Plugin;
use crate::install::atomic_replace::AtomicReplacer;
use crate::install::backup::BackupEngine;
use crate::install::gates::{CatalogGate, DiskSpaceGate, PathSafetyGate, ProcessGate};
use crate::install::rollback::RollbackEngine;
use crate::install::staging::StagingManager;
use crate::install::verification::PostVerifier;
use crate::packages::parser::PackageParser;
use crate::storage::database::Database;
use crate::storage::repositories::{BackupRepository, ExtensionRepository, TransactionRepository};

pub struct InstallEngine {
    _db: Database,
    backup_repo: BackupRepository,
    extension_repo: ExtensionRepository,
    tx_repo: TransactionRepository,
    backup_storage_root: PathBuf,
}

impl InstallEngine {
    pub fn new(db: Database, backup_storage_root: PathBuf) -> Self {
        Self {
            _db: db.clone(),
            backup_repo: BackupRepository::new(db.clone()),
            extension_repo: ExtensionRepository::new(db.clone()),
            tx_repo: TransactionRepository::new(db),
            backup_storage_root,
        }
    }

    /// Resolves logical targets against the actual game installation root
    pub fn resolve_logical_target(game_root: &Path, logical_target: &str) -> HubResult<PathBuf> {
        let clean = logical_target.replace('\\', "/");
        crate::security::path_guard::PathGuard::sanitize_relative_path(&clean)?;
        let target = [("apx/plugins/", "apx/plugins"), ("game/mods/", "mods"), ("game/resources/", "resources"), ("hub/data/", "hub_data")]
            .iter().find_map(|(prefix, directory)| clean.strip_prefix(prefix).map(|rel| game_root.join(directory).join(rel)))
            .ok_or_else(|| HubError::invalid_manifest(format!("Unrecognized logical prefix in '{logical_target}'")))?;
        crate::security::path_guard::PathGuard::ensure_within_root(game_root, &target)
    }

    pub fn check_compatibility(manifest: &crate::packages::manifest::HubPackageManifestV1, game: &GameInstallation) -> HubResult<()> {
        let compatibility = &manifest.compatibility.game;
        let declared = if compatibility.platforms.is_empty() { vec!["STEAM_CN".to_string()] } else { compatibility.platforms.clone() };
        if !declared.contains(&game.platform) {
            return Err(HubError::invalid_manifest(format!("扩展未声明支持所选渠道 {}。旧包未填写 platforms 时仅按 Steam 国服处理。", game.platform)));
        }
        if compatibility.app_id != "2622000" { return Err(HubError::invalid_manifest("扩展的游戏标识与吉星派对不一致。")); }
        if compatibility.min_version.is_some() || compatibility.max_version.is_some() {
            let profile = crate::extension_center::profiles().into_iter().find(|p| p["platformId"] == game.platform)
                .ok_or_else(|| HubError::invalid_manifest("尚无此渠道的版本指纹。"))?;
            let hash = crate::security::hash::compute_file_sha256(crate::extension_center::game_root(game)?.join("GameAssembly.dll"))?;
            if !hash.eq_ignore_ascii_case(profile["gameRuntimeDllSha256"].as_str().unwrap_or_default()) {
                return Err(HubError::invalid_manifest("无法核验所选游戏版本，不能安装有版本限制的包。"));
            }
            let parse = |v: &str| -> HubResult<Vec<u32>> {
                let mut parts: Vec<u32> = v.split('.').map(|part| part.parse().map_err(|_| HubError::invalid_manifest("游戏版本应为数字点分格式。"))).collect::<HubResult<_>>()?;
                while parts.len() < 4 { parts.push(0); }
                Ok(parts)
            };
            let version = parse(profile["gameVersion"].as_str().unwrap_or_default())?;
            if compatibility.min_version.as_ref().map(|v| parse(v).map(|min| version < min)).transpose()?.unwrap_or(false)
                || compatibility.max_version.as_ref().map(|v| parse(v).map(|max| version > max)).transpose()?.unwrap_or(false) {
                return Err(HubError::invalid_manifest("所选游戏版本不在扩展支持范围内。"));
            }
        }
        Ok(())
    }

    /// Full atomic installation transaction pipeline
    pub fn install_package<P: AsRef<Path>>(
        &self,
        archive_path: P,
        game_installation: &GameInstallation,
    ) -> HubResult<InstallTransaction> {
        let tx_id = Uuid::new_v4().to_string();
        let archive_path = archive_path.as_ref();
        let selected_root = crate::extension_center::game_root(game_installation)?;
        let game_root = selected_root.as_path();

        info!("Starting install transaction {} for archive {}", tx_id, archive_path.display());

        // 1. Parse and validate manifest from archive
        let manifest = PackageParser::parse_archive(archive_path)?;
        Self::check_compatibility(&manifest, game_installation)?;
        let pkg_id = crate::extension_center::scoped_package_id(&manifest.package.id, game_installation);
        let pkg_version = manifest.package.version.clone();
        let pkg_type = manifest.package.package_type.clone();

        let mut tx = InstallTransaction {
            id: tx_id.clone(),
            package_id: pkg_id.clone(),
            package_version: pkg_version.clone(),
            operation: "INSTALL".into(),
            state: TransactionState::PackageReceived.to_string(),
            game_installation_id: Some(game_installation.id.clone()),
            backup_id: None,
            started_at: Utc::now(),
            finished_at: None,
            error_code: None,
            error_message: None,
        };
        self.tx_repo.create_transaction(&tx)?;

        // Step 1: Safety Gates
        self.log_step(&tx_id, 1, "SAFETY_GATES", "RUNNING")?;
        PathSafetyGate::check(&manifest)?;
        CatalogGate::check(&manifest, &["apx/plugins/", "game/mods/", "game/resources/", "hub/data/"])?;
        ProcessGate::check_target(Path::new(game_installation.executable_path.as_deref().unwrap_or_default()))?;
        
        let total_size: u64 = manifest.files.iter().map(|f| f.size).sum();
        DiskSpaceGate::check(game_root, total_size)?;

        self.update_tx_state(&mut tx, TransactionState::CatalogGatePassed)?;
        self.log_step(&tx_id, 1, "SAFETY_GATES", "PASSED")?;

        // Step 2: Build target mappings
        let mut resolved_targets: Vec<(String, PathBuf)> = Vec::new();
        for file in &manifest.files {
            let disk_path = Self::resolve_logical_target(game_root, &file.target)?;
            let owner: Option<String> = self._db.with_conn(|conn| {
                use rusqlite::OptionalExtension;
                conn.query_row("SELECT package_id FROM installed_files WHERE target_path=?1", [disk_path.display().to_string()], |row| row.get(0))
                    .optional().map_err(|e| HubError::storage_error(e.to_string()))
            })?;
            if owner.as_ref().is_some_and(|id| id != &pkg_id) { return Err(HubError::security_violation("目标文件已由其他扩展管理，请先处理冲突。")); }
            resolved_targets.push((file.target.clone(), disk_path));
        }

        // Step 3: Create pre-install backup
        self.log_step(&tx_id, 2, "BACKUP", "RUNNING")?;
        let backup = if manifest.install.backup {
            let b = BackupEngine::create_pre_install_backup(
                &self.backup_storage_root,
                &tx_id,
                &pkg_id,
                &resolved_targets,
            )?;
            self.backup_repo.save(&b)?;
            tx.backup_id = Some(b.id.clone());
            Some(b)
        } else {
            None
        };
        self.update_tx_state(&mut tx, TransactionState::BackupCreated)?;
        self.log_step(&tx_id, 2, "BACKUP", "COMPLETED")?;

        // Step 4: Staging extraction & hash verification
        self.log_step(&tx_id, 3, "STAGING", "RUNNING")?;
        self.update_tx_state(&mut tx, TransactionState::Staging)?;

        let staging = StagingManager::new()?;
        let staged_pairs = staging.stage_package(archive_path, &manifest)?;
        self.update_tx_state(&mut tx, TransactionState::Staged)?;
        self.log_step(&tx_id, 3, "STAGING", "COMPLETED")?;

        // Step 5: Ready to Commit & Committing
        ProcessGate::check_target(Path::new(game_installation.executable_path.as_deref().unwrap_or_default()))?;
        self.update_tx_state(&mut tx, TransactionState::ReadyToCommit)?;
        self.update_tx_state(&mut tx, TransactionState::Committing)?;
        self.log_step(&tx_id, 4, "COMMIT", "RUNNING")?;

        // Map staged sources to target destinations
        let mut commit_pairs = Vec::new();
        for (staged_path, logical_rel) in staged_pairs {
            let logical_str = logical_rel.display().to_string().replace('\\', "/");
            let target_disk = Self::resolve_logical_target(game_root, &logical_str)?;
            commit_pairs.push((staged_path, target_disk));
        }

        let commit_res = AtomicReplacer::commit_staged_files(
            &commit_pairs,
            &pkg_id,
            &pkg_version,
            &self.extension_repo,
        );

        if let Err(e) = commit_res {
            error!("Commit failed for tx {}: {e}", tx_id);
            self.handle_failure_and_rollback(&mut tx, &backup, &e)?;
            return Err(e);
        }
        self.log_step(&tx_id, 4, "COMMIT", "COMPLETED")?;

        // Step 6: Post Verification
        self.update_tx_state(&mut tx, TransactionState::PostVerify)?;
        self.log_step(&tx_id, 5, "POST_VERIFY", "RUNNING")?;

        let verify_res = PostVerifier::verify_committed_files(&manifest, &resolved_targets);
        if let Err(e) = verify_res {
            error!("Post-commit verification failed for tx {}: {e}", tx_id);
            self.handle_failure_and_rollback(&mut tx, &backup, &e)?;
            return Err(e);
        }
        self.log_step(&tx_id, 5, "POST_VERIFY", "PASSED")?;

        // Step 7: Record installed extension entity
        if pkg_type == "apx-plugin" {
            let plugin = Plugin {
                plugin_id: pkg_id.clone(),
                name: manifest.package.name.clone(),
                installed_version: Some(pkg_version.clone()),
                status: "INSTALLED".into(),
                install_path: Some(resolved_targets.first().map(|(_, p)| p.display().to_string()).unwrap_or_default()),
                manifest_json: Some(serde_json::to_value(&manifest).unwrap_or_default()),
                installed_at: Some(Utc::now()),
                updated_at: Some(Utc::now()),
            };
            self.extension_repo.save_plugin(&plugin)?;
        } else {
            let mod_pkg = ModPackage {
                mod_id: pkg_id.clone(),
                name: manifest.package.name.clone(),
                installed_version: Some(pkg_version.clone()),
                status: "INSTALLED".into(),
                install_path: Some(resolved_targets.first().map(|(_, p)| p.display().to_string()).unwrap_or_default()),
                manifest_json: Some(serde_json::to_value(&manifest).unwrap_or_default()),
                installed_at: Some(Utc::now()),
                updated_at: Some(Utc::now()),
            };
            self.extension_repo.save_mod(&mod_pkg)?;
        }

        // Finalize transaction
        self.update_tx_state(&mut tx, TransactionState::Completed)?;
        self.tx_repo.update_state(&tx.id, "COMPLETED", Some(Utc::now()), None, None)?;
        info!("Install transaction {} completed successfully!", tx_id);

        Ok(tx)
    }

    fn handle_failure_and_rollback(
        &self,
        tx: &mut InstallTransaction,
        backup: &Option<crate::domain::installation::Backup>,
        err: &HubError,
    ) -> HubResult<()> {
        self.update_tx_state(tx, TransactionState::RollbackRequired)?;
        if let Some(b) = backup {
            self.update_tx_state(tx, TransactionState::RollingBack)?;
            if let Err(rb_err) = RollbackEngine::execute_rollback(b, &self.extension_repo) {
                error!("Rollback failed for tx {}: {rb_err}", tx.id);
                self.update_tx_state(tx, TransactionState::RollbackFailed)?;
                self.tx_repo.update_state(&tx.id, "ROLLBACK_FAILED", Some(Utc::now()), Some(&err.code), Some(&err.message))?;
            } else {
                self.update_tx_state(tx, TransactionState::RolledBack)?;
                self.tx_repo.update_state(&tx.id, "ROLLED_BACK", Some(Utc::now()), Some(&err.code), Some(&err.message))?;
            }
        } else {
            self.update_tx_state(tx, TransactionState::Failed)?;
            self.tx_repo.update_state(&tx.id, "FAILED", Some(Utc::now()), Some(&err.code), Some(&err.message))?;
        }
        Ok(())
    }

    /// Startup crash recovery: rolls back any transaction interrupted mid-flight (COMMITTING or POST_VERIFY)
    pub fn recover_interrupted_transactions(&self) -> HubResult<Vec<String>> {
        let interrupted = self.tx_repo.get_incomplete_transactions()?;
        let mut recovered_ids = Vec::new();

        for tx in interrupted {
            let state: TransactionState = tx.state.as_str().into();
            if state == TransactionState::Committing || state == TransactionState::PostVerify || state == TransactionState::RollbackRequired {
                warn!("Found interrupted transaction {} in state {:?}. Initiating startup recovery...", tx.id, state);
                if let Some(backup_id) = &tx.backup_id {
                    if let Ok(Some(backup)) = self.backup_repo.get_by_id(backup_id) {
                        RollbackEngine::execute_rollback(&backup, &self.extension_repo)?;
                        self.tx_repo.update_state(
                            &tx.id,
                            "ROLLED_BACK",
                            Some(Utc::now()),
                            Some("CRASH_RECOVERY"),
                            Some("Transaction recovered and rolled back upon app launch."),
                        )?;
                        recovered_ids.push(tx.id.clone());
                    }
                }
            }
        }

        Ok(recovered_ids)
    }

    fn update_tx_state(&self, tx: &mut InstallTransaction, state: TransactionState) -> HubResult<()> {
        tx.state = state.to_string();
        self.tx_repo.update_state(&tx.id, &tx.state, None, None, None)
    }

    fn log_step(&self, tx_id: &str, index: i32, name: &str, state: &str) -> HubResult<()> {
        let step = InstallTransactionStep {
            id: Uuid::new_v4().to_string(),
            transaction_id: tx_id.to_string(),
            step_index: index,
            step_name: name.to_string(),
            state: state.to_string(),
            started_at: Some(Utc::now()),
            finished_at: Some(Utc::now()),
            detail_json: None,
        };
        self.tx_repo.record_step(&step)
    }
}
