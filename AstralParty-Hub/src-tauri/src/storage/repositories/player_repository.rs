use chrono::Utc;
use rusqlite::params;
use crate::domain::card::CardInventory;
use crate::domain::character::CharacterProgress;
use crate::domain::error::{HubError, HubResult};
use crate::domain::player::PlayerProfile;
use crate::storage::database::Database;

pub struct PlayerRepository {
    db: Database,
}

impl PlayerRepository {
    pub fn new(db: Database) -> Self {
        Self { db }
    }

    pub fn save_profile(&self, profile: &PlayerProfile) -> HubResult<()> {
        self.db.with_conn(|conn| {
            conn.execute(
                "INSERT INTO player_profiles (id, display_name, avatar_uri, created_at, updated_at)
                 VALUES (?1, ?2, ?3, ?4, ?5)
                 ON CONFLICT(id) DO UPDATE SET
                    display_name = excluded.display_name,
                    avatar_uri = excluded.avatar_uri,
                    updated_at = excluded.updated_at;",
                params![
                    profile.id,
                    profile.display_name,
                    profile.avatar_uri,
                    profile.created_at.to_rfc3339(),
                    profile.updated_at.to_rfc3339(),
                ],
            )
            .map_err(|e| HubError::storage_error(format!("Failed to save player profile: {e}")))?;
            Ok(())
        })
    }

    pub fn get_default_profile(&self) -> HubResult<Option<PlayerProfile>> {
        self.db.with_conn(|conn| {
            let mut stmt = conn
                .prepare("SELECT id, display_name, avatar_uri, created_at, updated_at FROM player_profiles ORDER BY updated_at DESC LIMIT 1")
                .map_err(|e| HubError::storage_error(format!("Failed to prepare query: {e}")))?;

            let mut rows = stmt
                .query([])
                .map_err(|e| HubError::storage_error(format!("Query failed: {e}")))?;

            if let Some(row) = rows.next().map_err(|e| HubError::storage_error(format!("Row read failed: {e}")))? {
                let created_str: String = row.get(3).unwrap_or_default();
                let updated_str: String = row.get(4).unwrap_or_default();

                Ok(Some(PlayerProfile {
                    id: row.get(0).unwrap_or_default(),
                    display_name: row.get(1).ok(),
                    avatar_uri: row.get(2).ok(),
                    created_at: chrono::DateTime::parse_from_rfc3339(&created_str).ok().map(|dt| dt.with_timezone(&Utc)).unwrap_or_else(Utc::now),
                    updated_at: chrono::DateTime::parse_from_rfc3339(&updated_str).ok().map(|dt| dt.with_timezone(&Utc)).unwrap_or_else(Utc::now),
                }))
            } else {
                Ok(None)
            }
        })
    }

    pub fn save_character_progress(&self, progress: &CharacterProgress) -> HubResult<()> {
        self.db.with_conn(|conn| {
            conn.execute(
                "INSERT INTO character_progress (
                    id, player_profile_id, character_key, owned, level, star_level,
                    progress_json, observed_at, created_at, updated_at
                ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10)
                ON CONFLICT(player_profile_id, character_key) DO UPDATE SET
                    owned = excluded.owned,
                    level = excluded.level,
                    star_level = excluded.star_level,
                    progress_json = excluded.progress_json,
                    observed_at = excluded.observed_at,
                    updated_at = excluded.updated_at;",
                params![
                    progress.id,
                    progress.player_profile_id,
                    progress.character_key,
                    progress.owned as i32,
                    progress.level,
                    progress.star_level,
                    progress.progress_json.as_ref().map(|v| v.to_string()),
                    progress.observed_at.map(|t| t.to_rfc3339()),
                    progress.created_at.to_rfc3339(),
                    progress.updated_at.to_rfc3339(),
                ],
            )
            .map_err(|e| HubError::storage_error(format!("Failed to save character progress: {e}")))?;
            Ok(())
        })
    }

    pub fn list_character_progress(&self, profile_id: &str) -> HubResult<Vec<CharacterProgress>> {
        self.db.with_conn(|conn| {
            let mut stmt = conn
                .prepare("SELECT id, player_profile_id, character_key, owned, level, star_level, progress_json, observed_at, created_at, updated_at FROM character_progress WHERE player_profile_id = ?1")
                .map_err(|e| HubError::storage_error(format!("Failed to prepare query: {e}")))?;

            let rows = stmt
                .query_map([profile_id], |row| {
                    let progress_str: Option<String> = row.get(6).ok();
                    let observed_str: Option<String> = row.get(7).ok();
                    let created_str: String = row.get(8).unwrap_or_default();
                    let updated_str: String = row.get(9).unwrap_or_default();

                    Ok(CharacterProgress {
                        id: row.get(0).unwrap_or_default(),
                        player_profile_id: row.get(1).unwrap_or_default(),
                        character_key: row.get(2).unwrap_or_default(),
                        owned: row.get::<_, i32>(3).unwrap_or(0) != 0,
                        level: row.get(4).ok(),
                        star_level: row.get(5).ok(),
                        progress_json: progress_str.and_then(|s| serde_json::from_str(&s).ok()),
                        observed_at: observed_str.and_then(|s| chrono::DateTime::parse_from_rfc3339(&s).ok().map(|dt| dt.with_timezone(&Utc))),
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

    pub fn save_card_inventory(&self, item: &CardInventory) -> HubResult<()> {
        self.db.with_conn(|conn| {
            conn.execute(
                "INSERT INTO card_inventory (
                    id, player_profile_id, card_key, owned, quantity, upgrade_level,
                    inventory_json, observed_at, created_at, updated_at
                ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10)
                ON CONFLICT(player_profile_id, card_key) DO UPDATE SET
                    owned = excluded.owned,
                    quantity = excluded.quantity,
                    upgrade_level = excluded.upgrade_level,
                    inventory_json = excluded.inventory_json,
                    observed_at = excluded.observed_at,
                    updated_at = excluded.updated_at;",
                params![
                    item.id,
                    item.player_profile_id,
                    item.card_key,
                    item.owned as i32,
                    item.quantity,
                    item.upgrade_level,
                    item.inventory_json.as_ref().map(|v| v.to_string()),
                    item.observed_at.map(|t| t.to_rfc3339()),
                    item.created_at.to_rfc3339(),
                    item.updated_at.to_rfc3339(),
                ],
            )
            .map_err(|e| HubError::storage_error(format!("Failed to save card inventory: {e}")))?;
            Ok(())
        })
    }

    pub fn list_card_inventory(&self, profile_id: &str) -> HubResult<Vec<CardInventory>> {
        self.db.with_conn(|conn| {
            let mut stmt = conn
                .prepare("SELECT id, player_profile_id, card_key, owned, quantity, upgrade_level, inventory_json, observed_at, created_at, updated_at FROM card_inventory WHERE player_profile_id = ?1")
                .map_err(|e| HubError::storage_error(format!("Failed to prepare query: {e}")))?;

            let rows = stmt
                .query_map([profile_id], |row| {
                    let inv_str: Option<String> = row.get(6).ok();
                    let observed_str: Option<String> = row.get(7).ok();
                    let created_str: String = row.get(8).unwrap_or_default();
                    let updated_str: String = row.get(9).unwrap_or_default();

                    Ok(CardInventory {
                        id: row.get(0).unwrap_or_default(),
                        player_profile_id: row.get(1).unwrap_or_default(),
                        card_key: row.get(2).unwrap_or_default(),
                        owned: row.get::<_, i32>(3).unwrap_or(0) != 0,
                        quantity: row.get(4).ok(),
                        upgrade_level: row.get(5).ok(),
                        inventory_json: inv_str.and_then(|s| serde_json::from_str(&s).ok()),
                        observed_at: observed_str.and_then(|s| chrono::DateTime::parse_from_rfc3339(&s).ok().map(|dt| dt.with_timezone(&Utc))),
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
