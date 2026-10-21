use serde::{Deserialize, Serialize};
use std::path::Path;
use sysinfo::System;
use crate::domain::installation::{GameInstallation, InstallTransaction};
use crate::game::detector::GameDetector;
use crate::platform::windows::process::find_running_game_processes;
use crate::storage::repositories::{GameRepository, TransactionRepository};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SystemDiagnosticSnapshot {
    pub os_name: String,
    pub os_version: String,
    pub primary_game: Option<GameInstallation>,
    pub running_processes: Vec<String>,
    pub xingshu_detected: bool,
    pub xingshu_path: Option<String>,
    pub recent_transactions: Vec<InstallTransaction>,
}

pub struct DiagnosticCollector;

impl DiagnosticCollector {
    pub fn collect(
        game_repo: &GameRepository,
        tx_repo: &TransactionRepository,
    ) -> SystemDiagnosticSnapshot {
        let primary_game = game_repo.get_primary().unwrap_or(None).or_else(|| {
            // If none saved, perform on-the-fly detection and persist
            if let Some(d) = GameDetector::detect_game() {
                let inst = GameInstallation {
                    id: "detected_auto".into(),
                    platform: "steam".into(),
                    platform_app_id: Some("2622000".into()),
                    install_path: d.install_path,
                    executable_path: d.executable_path,
                    detected_build_id: d.build_id,
                    detected_version: d.version,
                    detection_method: "AUTO_STEAM".into(),
                    is_valid: d.is_valid,
                    is_primary: true,
                    last_verified_at: Some(chrono::Utc::now()),
                    created_at: chrono::Utc::now(),
                    updated_at: chrono::Utc::now(),
                };
                let _ = game_repo.save(&inst);
                Some(inst)
            } else {
                None
            }
        });

        // Check XingShu presence in game directory
        let (xingshu_detected, xingshu_path) = if let Some(game) = &primary_game {
            let root = Path::new(&game.install_path);
            let cn_xingshu = root.join("8vJXn6CN").join("xingshu");
            let root_xingshu = root.join("xingshu");
            let cn_version_dll = root.join("8vJXn6CN").join("version.dll");
            let root_version_dll = root.join("version.dll");

            if cn_xingshu.exists() {
                (true, Some(cn_xingshu.display().to_string()))
            } else if root_xingshu.exists() {
                (true, Some(root_xingshu.display().to_string()))
            } else if cn_version_dll.exists() {
                (true, Some(cn_version_dll.display().to_string()))
            } else if root_version_dll.exists() {
                (true, Some(root_version_dll.display().to_string()))
            } else {
                (false, None)
            }
        } else {
            (false, None)
        };

        let running_processes = find_running_game_processes();
        let recent_transactions = tx_repo.list_recent(5).unwrap_or_default();

        SystemDiagnosticSnapshot {
            os_name: System::name().unwrap_or_else(|| "Windows".into()),
            os_version: System::os_version().unwrap_or_default(),
            primary_game,
            running_processes,
            xingshu_detected,
            xingshu_path,
            recent_transactions,
        }
    }
}
