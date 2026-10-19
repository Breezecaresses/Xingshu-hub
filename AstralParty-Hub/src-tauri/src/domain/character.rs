use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Character {
    pub character_key: String,
    pub name: String,
    pub rarity: Option<String>,
    pub metadata_json: Option<serde_json::Value>,
    pub first_seen_version: Option<String>,
    pub last_seen_version: Option<String>,
    pub created_at: DateTime<Utc>,
    pub updated_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CharacterProgress {
    pub id: String,
    pub player_profile_id: String,
    pub character_key: String,
    pub owned: bool,
    pub level: Option<i32>,
    pub star_level: Option<i32>,
    pub progress_json: Option<serde_json::Value>,
    pub observed_at: Option<DateTime<Utc>>,
    pub created_at: DateTime<Utc>,
    pub updated_at: DateTime<Utc>,
}
