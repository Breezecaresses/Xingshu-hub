use chrono::Utc;
use rusqlite::params;
use crate::domain::card::Card;
use crate::domain::character::Character;
use crate::domain::error::{HubError, HubResult};
use crate::storage::database::Database;

pub struct CatalogRepository {
    db: Database,
}

impl CatalogRepository {
    pub fn new(db: Database) -> Self {
        Self { db }
    }

    pub fn save_character(&self, character: &Character) -> HubResult<()> {
        self.db.with_conn(|conn| {
            conn.execute(
                "INSERT INTO characters (
                    character_key, name, rarity, metadata_json,
                    first_seen_version, last_seen_version, created_at, updated_at
                ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)
                ON CONFLICT(character_key) DO UPDATE SET
                    name = excluded.name,
                    rarity = excluded.rarity,
                    metadata_json = excluded.metadata_json,
                    first_seen_version = excluded.first_seen_version,
                    last_seen_version = excluded.last_seen_version,
                    updated_at = excluded.updated_at;",
                params![
                    character.character_key,
                    character.name,
                    character.rarity,
                    character.metadata_json.as_ref().map(|v| v.to_string()),
                    character.first_seen_version,
                    character.last_seen_version,
                    character.created_at.to_rfc3339(),
                    character.updated_at.to_rfc3339(),
                ],
            )
            .map_err(|e| HubError::storage_error(format!("Failed to save character: {e}")))?;
            Ok(())
        })
    }

    pub fn list_characters(&self) -> HubResult<Vec<Character>> {
        self.db.with_conn(|conn| {
            let mut stmt = conn
                .prepare("SELECT character_key, name, rarity, metadata_json, first_seen_version, last_seen_version, created_at, updated_at FROM characters ORDER BY name ASC")
                .map_err(|e| HubError::storage_error(format!("Failed to prepare query: {e}")))?;

            let rows = stmt
                .query_map([], |row| {
                    let meta_str: Option<String> = row.get(3).ok();
                    let created_str: String = row.get(6).unwrap_or_default();
                    let updated_str: String = row.get(7).unwrap_or_default();

                    Ok(Character {
                        character_key: row.get(0).unwrap_or_default(),
                        name: row.get(1).unwrap_or_default(),
                        rarity: row.get(2).ok(),
                        metadata_json: meta_str.and_then(|s| serde_json::from_str(&s).ok()),
                        first_seen_version: row.get(4).ok(),
                        last_seen_version: row.get(5).ok(),
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

    pub fn save_card(&self, card: &Card) -> HubResult<()> {
        self.db.with_conn(|conn| {
            conn.execute(
                "INSERT INTO cards (
                    card_key, name, rarity, card_type, metadata_json,
                    first_seen_version, last_seen_version, created_at, updated_at
                ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9)
                ON CONFLICT(card_key) DO UPDATE SET
                    name = excluded.name,
                    rarity = excluded.rarity,
                    card_type = excluded.card_type,
                    metadata_json = excluded.metadata_json,
                    first_seen_version = excluded.first_seen_version,
                    last_seen_version = excluded.last_seen_version,
                    updated_at = excluded.updated_at;",
                params![
                    card.card_key,
                    card.name,
                    card.rarity,
                    card.card_type,
                    card.metadata_json.as_ref().map(|v| v.to_string()),
                    card.first_seen_version,
                    card.last_seen_version,
                    card.created_at.to_rfc3339(),
                    card.updated_at.to_rfc3339(),
                ],
            )
            .map_err(|e| HubError::storage_error(format!("Failed to save card: {e}")))?;
            Ok(())
        })
    }

    pub fn list_cards(&self) -> HubResult<Vec<Card>> {
        self.db.with_conn(|conn| {
            let mut stmt = conn
                .prepare("SELECT card_key, name, rarity, card_type, metadata_json, first_seen_version, last_seen_version, created_at, updated_at FROM cards ORDER BY name ASC")
                .map_err(|e| HubError::storage_error(format!("Failed to prepare query: {e}")))?;

            let rows = stmt
                .query_map([], |row| {
                    let meta_str: Option<String> = row.get(4).ok();
                    let created_str: String = row.get(7).unwrap_or_default();
                    let updated_str: String = row.get(8).unwrap_or_default();

                    Ok(Card {
                        card_key: row.get(0).unwrap_or_default(),
                        name: row.get(1).unwrap_or_default(),
                        rarity: row.get(2).ok(),
                        card_type: row.get(3).ok(),
                        metadata_json: meta_str.and_then(|s| serde_json::from_str(&s).ok()),
                        first_seen_version: row.get(5).ok(),
                        last_seen_version: row.get(6).ok(),
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
