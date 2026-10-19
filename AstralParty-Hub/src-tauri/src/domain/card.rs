use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Card {
    pub card_key: String,
    pub name: String,
    pub rarity: Option<String>,
    pub card_type: Option<String>,
    pub metadata_json: Option<serde_json::Value>,
    pub first_seen_version: Option<String>,
    pub last_seen_version: Option<String>,
    pub created_at: DateTime<Utc>,
    pub updated_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CardInventory {
    pub id: String,
    pub player_profile_id: String,
    pub card_key: String,
    pub owned: bool,
    pub quantity: Option<i32>,
    pub upgrade_level: Option<i32>,
    pub inventory_json: Option<serde_json::Value>,
    pub observed_at: Option<DateTime<Utc>>,
    pub created_at: DateTime<Utc>,
    pub updated_at: DateTime<Utc>,
}
