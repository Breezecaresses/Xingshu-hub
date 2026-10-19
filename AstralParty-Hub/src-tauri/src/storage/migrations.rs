use rusqlite::Connection;
use tracing::info;
use crate::domain::error::{HubError, HubResult};

struct Migration {
    version: i64,
    name: &'static str,
    sql: &'static str,
}

const MIGRATIONS: &[Migration] = &[
    Migration {
        version: 1,
        name: "001_initial_schema",
        sql: r#"
        -- System
        CREATE TABLE IF NOT EXISTS schema_migrations (
            version INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            applied_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS app_settings (
            key TEXT PRIMARY KEY,
            value_json TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        -- Game Installation
        CREATE TABLE IF NOT EXISTS game_installations (
            id TEXT PRIMARY KEY,
            platform TEXT NOT NULL,
            platform_app_id TEXT,
            install_path TEXT NOT NULL,
            executable_path TEXT,
            detected_build_id TEXT,
            detected_version TEXT,
            detection_method TEXT NOT NULL,
            is_valid INTEGER NOT NULL DEFAULT 0,
            is_primary INTEGER NOT NULL DEFAULT 0,
            last_verified_at TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        -- Providers
        CREATE TABLE IF NOT EXISTS provider_configs (
            provider_id TEXT PRIMARY KEY,
            provider_type TEXT NOT NULL,
            enabled INTEGER NOT NULL DEFAULT 1,
            priority INTEGER NOT NULL DEFAULT 100,
            config_json TEXT,
            secret_ref TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS provider_sync_runs (
            id TEXT PRIMARY KEY,
            provider_id TEXT NOT NULL,
            started_at TEXT NOT NULL,
            finished_at TEXT,
            status TEXT NOT NULL,
            records_read INTEGER NOT NULL DEFAULT 0,
            records_written INTEGER NOT NULL DEFAULT 0,
            error_code TEXT,
            error_message TEXT,
            FOREIGN KEY(provider_id) REFERENCES provider_configs(provider_id)
        );

        CREATE TABLE IF NOT EXISTS source_records (
            id TEXT PRIMARY KEY,
            provider_id TEXT NOT NULL,
            entity_type TEXT NOT NULL,
            entity_key TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            observed_at TEXT NOT NULL,
            confidence TEXT,
            source_version TEXT,
            FOREIGN KEY(provider_id) REFERENCES provider_configs(provider_id)
        );

        -- Player Profile & Catalog
        CREATE TABLE IF NOT EXISTS player_profiles (
            id TEXT PRIMARY KEY,
            display_name TEXT,
            avatar_uri TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS player_identity_links (
            id TEXT PRIMARY KEY,
            player_profile_id TEXT NOT NULL,
            provider_id TEXT NOT NULL,
            external_player_key TEXT NOT NULL,
            created_at TEXT NOT NULL,
            UNIQUE(provider_id, external_player_key),
            FOREIGN KEY(player_profile_id) REFERENCES player_profiles(id)
        );

        CREATE TABLE IF NOT EXISTS characters (
            character_key TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            rarity TEXT,
            metadata_json TEXT,
            first_seen_version TEXT,
            last_seen_version TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS character_progress (
            id TEXT PRIMARY KEY,
            player_profile_id TEXT NOT NULL,
            character_key TEXT NOT NULL,
            owned INTEGER NOT NULL DEFAULT 0,
            level INTEGER,
            star_level INTEGER,
            progress_json TEXT,
            observed_at TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            UNIQUE(player_profile_id, character_key),
            FOREIGN KEY(player_profile_id) REFERENCES player_profiles(id),
            FOREIGN KEY(character_key) REFERENCES characters(character_key)
        );

        CREATE TABLE IF NOT EXISTS cards (
            card_key TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            rarity TEXT,
            card_type TEXT,
            metadata_json TEXT,
            first_seen_version TEXT,
            last_seen_version TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS card_inventory (
            id TEXT PRIMARY KEY,
            player_profile_id TEXT NOT NULL,
            card_key TEXT NOT NULL,
            owned INTEGER NOT NULL DEFAULT 0,
            quantity INTEGER,
            upgrade_level INTEGER,
            inventory_json TEXT,
            observed_at TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            UNIQUE(player_profile_id, card_key),
            FOREIGN KEY(player_profile_id) REFERENCES player_profiles(id),
            FOREIGN KEY(card_key) REFERENCES cards(card_key)
        );

        -- Matches
        CREATE TABLE IF NOT EXISTS matches (
            id TEXT PRIMARY KEY,
            provider_id TEXT,
            external_match_key TEXT,
            started_at TEXT,
            ended_at TEXT,
            game_version TEXT,
            mode TEXT,
            map_key TEXT,
            result_json TEXT,
            created_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS match_players (
            id TEXT PRIMARY KEY,
            match_id TEXT NOT NULL,
            seat_index INTEGER,
            external_player_key TEXT,
            display_name TEXT,
            character_key TEXT,
            placement INTEGER,
            is_self INTEGER NOT NULL DEFAULT 0,
            stats_json TEXT,
            FOREIGN KEY(match_id) REFERENCES matches(id)
        );

        -- Extensions & Mod Catalog
        CREATE TABLE IF NOT EXISTS plugins (
            plugin_id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            installed_version TEXT,
            status TEXT NOT NULL,
            install_path TEXT,
            manifest_json TEXT,
            installed_at TEXT,
            updated_at TEXT
        );

        CREATE TABLE IF NOT EXISTS mod_packages (
            mod_id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            installed_version TEXT,
            status TEXT NOT NULL,
            install_path TEXT,
            manifest_json TEXT,
            installed_at TEXT,
            updated_at TEXT
        );

        CREATE TABLE IF NOT EXISTS packages (
            id TEXT PRIMARY KEY,
            package_id TEXT NOT NULL,
            package_type TEXT NOT NULL,
            package_version TEXT NOT NULL,
            format TEXT NOT NULL,
            archive_path TEXT,
            package_sha256 TEXT NOT NULL,
            manifest_json TEXT NOT NULL,
            imported_at TEXT NOT NULL,
            UNIQUE(package_id, package_version, package_sha256)
        );

        CREATE TABLE IF NOT EXISTS installed_files (
            target_path TEXT PRIMARY KEY,
            package_id TEXT NOT NULL,
            package_version TEXT NOT NULL,
            installed_sha256 TEXT NOT NULL,
            installed_at TEXT NOT NULL
        );

        -- Transactions & Backups
        CREATE TABLE IF NOT EXISTS install_transactions (
            id TEXT PRIMARY KEY,
            package_id TEXT NOT NULL,
            package_version TEXT NOT NULL,
            operation TEXT NOT NULL,
            state TEXT NOT NULL,
            game_installation_id TEXT,
            backup_id TEXT,
            started_at TEXT NOT NULL,
            finished_at TEXT,
            error_code TEXT,
            error_message TEXT
        );

        CREATE TABLE IF NOT EXISTS install_transaction_steps (
            id TEXT PRIMARY KEY,
            transaction_id TEXT NOT NULL,
            step_index INTEGER NOT NULL,
            step_name TEXT NOT NULL,
            state TEXT NOT NULL,
            started_at TEXT,
            finished_at TEXT,
            detail_json TEXT,
            FOREIGN KEY(transaction_id) REFERENCES install_transactions(id)
        );

        CREATE TABLE IF NOT EXISTS backups (
            id TEXT PRIMARY KEY,
            backup_type TEXT NOT NULL,
            target_id TEXT,
            storage_path TEXT NOT NULL,
            manifest_json TEXT NOT NULL,
            created_at TEXT NOT NULL,
            verified_at TEXT,
            status TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS diagnostic_events (
            id TEXT PRIMARY KEY,
            severity TEXT NOT NULL,
            category TEXT NOT NULL,
            code TEXT,
            message TEXT NOT NULL,
            context_json TEXT,
            created_at TEXT NOT NULL
        );
        "#,
    },
];

pub fn run_migrations(conn: &mut Connection) -> HubResult<()> {
    conn.execute_batch(
        "CREATE TABLE IF NOT EXISTS schema_migrations (
            version INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            applied_at TEXT NOT NULL
        );",
    )
    .map_err(|e| HubError::storage_error(format!("Failed to create schema_migrations table: {e}")))?;

    for m in MIGRATIONS {
        let applied: bool = conn
            .query_row(
                "SELECT COUNT(*) > 0 FROM schema_migrations WHERE version = ?1",
                [m.version],
                |row| row.get(0),
            )
            .unwrap_or(false);

        if !applied {
            info!("Applying database migration {}: {}", m.version, m.name);
            let tx = conn
                .transaction()
                .map_err(|e| HubError::storage_error(format!("Failed to begin migration transaction: {e}")))?;

            tx.execute_batch(m.sql)
                .map_err(|e| HubError::storage_error(format!("Migration {} failed to execute: {e}", m.name)))?;

            tx.execute(
                "INSERT INTO schema_migrations (version, name, applied_at) VALUES (?1, ?2, datetime('now'))",
                (m.version, m.name),
            )
            .map_err(|e| HubError::storage_error(format!("Failed to record migration: {e}")))?;

            tx.commit()
                .map_err(|e| HubError::storage_error(format!("Failed to commit migration transaction: {e}")))?;
            
            info!("Successfully applied migration {}: {}", m.version, m.name);
        }
    }

    Ok(())
}
