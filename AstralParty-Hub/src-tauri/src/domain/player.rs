use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PlayerProfile {
    pub id: String,
    pub display_name: Option<String>,
    pub avatar_uri: Option<String>,
    pub created_at: DateTime<Utc>,
    pub updated_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PlayerIdentityLink {
    pub id: String,
    pub player_profile_id: String,
    pub provider_id: String,
    pub external_player_key: String,
    pub created_at: DateTime<Utc>,
}
