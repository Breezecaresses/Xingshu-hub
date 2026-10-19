use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Match {
    pub id: String,
    pub provider_id: Option<String>,
    pub external_match_key: Option<String>,
    pub started_at: Option<DateTime<Utc>>,
    pub ended_at: Option<DateTime<Utc>>,
    pub game_version: Option<String>,
    pub mode: Option<String>,
    pub map_key: Option<String>,
    pub result_json: Option<serde_json::Value>,
    pub created_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MatchPlayer {
    pub id: String,
    pub match_id: String,
    pub seat_index: Option<i32>,
    pub external_player_key: Option<String>,
    pub display_name: Option<String>,
    pub character_key: Option<String>,
    pub placement: Option<i32>,
    pub is_self: bool,
    pub stats_json: Option<serde_json::Value>,
}
