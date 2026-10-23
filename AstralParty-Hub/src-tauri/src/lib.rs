pub mod application;
pub mod commands;
pub mod diagnostics;
pub mod domain;
pub mod game;
pub mod game_library;
pub mod extension_center;
pub mod install;
pub mod packages;
pub mod platform;
pub mod providers;
pub mod security;
pub mod storage;
pub mod xing_shu;

pub mod desktop_host;
#[cfg(feature = "web-ui")]
pub mod android_installer;
#[cfg(feature = "web-ui")]
pub mod external_mods;
#[cfg(feature = "web-ui")]
pub mod app_update;

#[cfg(feature = "web-ui")]
mod web_host {
use super::*;
use std::sync::Arc;
use tauri::Manager;
use tracing::info;

use application::{
    CatalogService, DiagnosticService, ExtensionService, GameService, InstallService, PlayerService,
};
use commands::*;
use diagnostics::logger::HubLogger;
use providers::imported_data::ImportedDataProvider;
use providers::orchestrator::ProviderOrchestrator;
use providers::registry::ProviderRegistry;
use providers::xing_shu_spectator::XingShuSpectatorProvider;
use storage::database::Database;
use xing_shu::bridge::server::BridgeServer;
use xing_shu::package_manager::service::XingShuPackageManager;
use xing_shu::spectator::service::SpectatorService;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    HubLogger::init();
    info!("Starting XingShu Hub Core Backend...");

    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .setup(|app| {
            let app_handle = app.handle();
            let app_data_dir = app_handle
                .path()
                .app_data_dir()
                .unwrap_or_else(|_| dirs::data_dir().unwrap_or_default().join("xingshu-hub"));

            std::fs::create_dir_all(&app_data_dir).ok();

            // Seamless migration from legacy astral_party_hub.db if present
            let legacy_db = app_data_dir.join("astral_party_hub.db");
            let db_path = app_data_dir.join("xingshu_hub.db");
            if !db_path.exists() && legacy_db.exists() {
                info!("Migrating legacy database from {} to {}", legacy_db.display(), db_path.display());
                let _ = std::fs::copy(&legacy_db, &db_path);
            }

            let backup_dir = app_data_dir.join("backups");
            std::fs::create_dir_all(&backup_dir).ok();

            info!("Database file location: {}", db_path.display());
            info!("Backup storage location: {}", backup_dir.display());

            let db = Database::init(&db_path).expect("Failed to initialize XingShu Hub SQLite database");
            let game_library_store = game_library::GameLibraryStore::new(db.clone())?;

            let game_service = GameService::new(db.clone());
            let player_service = PlayerService::new(db.clone());
            let catalog_service = CatalogService::new(db.clone());
            let extension_service = ExtensionService::new(db.clone());
            let install_service = InstallService::new(db.clone(), backup_dir);
            let diagnostic_service = DiagnosticService::new(db.clone());

            // Startup Crash Recovery
            if let Ok(recovered) = install_service.recover_crashes() {
                if !recovered.is_empty() {
                    info!("Startup recovery: resolved {} interrupted transactions: {:?}", recovered.len(), recovered);
                }
            }

            // XingShu Core Subsystems: Bridge, Spectator, Package Manager
            let offline_replay = std::env::var("XINGSHU_OFFLINE_REPLAY").is_ok()
                || std::env::args().any(|arg| arg == "--offline-replay");
            let bridge_server = if offline_replay {
                info!("XingShu Hub starting in OFFLINE REPLAY mode (test security policy enabled)");
                Arc::new(BridgeServer::new_test())
            } else {
                Arc::new(BridgeServer::new())
            };
            bridge_server.set_game_library_store(game_library_store.clone());
            bridge_server.start().map_err(|e| format!("Failed to start XingShu Bridge production server: {}", e))?;
            bridge_server.set_app_handle(app_handle.clone());
            let spectator_service = Arc::new(SpectatorService::new(bridge_server.clone()));
            spectator_service.set_app_handle(app_handle.clone());
            spectator_service.start_snapshot_listener();
            let package_manager = Arc::new(XingShuPackageManager::new());

            // Register default provider(s)
            let mut registry = ProviderRegistry::new();
            let imported_provider = Arc::new(ImportedDataProvider::new());
            let spectator_provider = Arc::new(XingShuSpectatorProvider::new(spectator_service.clone()));
            registry.register(imported_provider);
            registry.register(spectator_provider);

            let orchestrator = ProviderOrchestrator::new(db.clone(), registry);

            // Manage services in Tauri state
            app.manage(game_service);
            app.manage(extension_center::ExtensionCenter { db: db.clone() });
            app.manage(android_installer::AndroidInstaller::default());
            app.manage(app_update::AppUpdater::default());
            app.manage(game_library_store);
            app.manage(player_service);
            app.manage(catalog_service);
            app.manage(extension_service);
            app.manage(install_service);
            app.manage(diagnostic_service);
            app.manage(orchestrator);
            app.manage(bridge_server);
            app.manage(spectator_service);
            app.manage(package_manager);
            app.manage(xing_shu::local_watcher::LocalWatcher::default());

            // Parse optional startup CLI arguments e.g. --locale zh-CN or --locale=en-US
            let startup_locale = parse_locale_from_args(std::env::args());
            if let Some(ref loc) = startup_locale {
                info!("XingShu Hub received CLI startup locale override: {}", loc);
            }

            let startup_route = parse_route_from_args(std::env::args());
            if let Some(ref rt) = startup_route {
                info!("XingShu Hub received CLI startup route: {}", rt);
            }

            let startup_eval = parse_eval_from_args(std::env::args());

            if let Some(main_win) = app.get_webview_window("main") {
                let title = match startup_locale.as_deref() {
                    Some("en-US") => "XingShu Hub",
                    _ => "星枢 Hub",
                };
                let _ = main_win.set_title(title);
                if offline_replay {
                    let _ = main_win.eval("window.__OFFLINE_REPLAY__ = true;");
                }
                if let Some(ref script) = startup_eval {
                    let _ = main_win.eval(script);
                }
            }

            app.manage(StartupLocale::new(startup_locale));
            app.manage(StartupRoute::new(startup_route));

            info!("XingShu Hub state and services successfully registered.");
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            game_library::get_game_library,
            game_library::sync_game_library,
            game_library::get_game_match_detail,
            game_library::request_game_match_detail,
            // Game commands
            detect_game,
            set_manual_game_path,
            get_primary_game,
            extension_center::list_extension_targets,
            extension_center::register_extension_target,
            extension_center::get_extension_inventory,
            extension_center::get_extension_runtime_state,
            extension_center::open_group_installer,
            android_installer::get_android_installer_status,
            android_installer::android_installer_action,
            external_mods::get_external_mod_report,
            external_mods::external_mod_action,
            external_mods::archive_external_mod_report,
            app_update::get_app_build,
            app_update::check_app_update,
            app_update::prepare_app_update,
            app_update::apply_app_update,
            // Player commands
            get_player_profile,
            list_character_progress,
            list_card_inventory,
            // Catalog commands
            list_characters,
            list_cards,
            // Extension commands
            list_plugins,
            list_mods,
            inspect_package_archive,
            uninstall_extension,
            // Install commands
            install_package,
            rollback_transaction,
            list_install_transactions,
            list_backups,
            // Diagnostic commands
            get_diagnostic_report,
            list_providers,
            probe_providers,
            sync_provider_data,
            // XingShu Core commands
            get_bridge_status,
            get_runtime_sessions,
            configure_runtime_bindings,
            send_bridge_test_event,
            send_watch_target,
            managed_local_status,
            managed_local_allocate,
            managed_local_release,
            clear_bridge_logs,
            get_spectator_session,
            get_spectator_snapshot,
            get_spectator_hand_payload,
            login_spectator,
            start_spectating,
            stop_spectating,
            inspect_xspkg,
            verify_xspkg,
            get_startup_locale,
            get_startup_route,
            check_game_process_status,
            launch_game_uri,
        ])
        .run(tauri::generate_context!())
        .expect("error while running XingShu Hub application");
}

}
#[cfg(feature = "web-ui")]
pub use web_host::run;
