use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub enum SpectatorConnectionState {
    Disconnected,
    Connecting,
    Idle,
    Spectating,
    Reconnecting,
    Failed(String),
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SpectatorSessionInfo {
    pub session_id: String,
    pub account_display_name: String,
    pub room_id: Option<String>,
    pub match_id: Option<String>,
    pub connection_state: SpectatorConnectionState,
    pub connected_at: Option<DateTime<Utc>>,
    pub latency_ms: Option<u32>,
    pub total_events_received: u64,
}

impl Default for SpectatorSessionInfo {
    fn default() -> Self {
        Self {
            session_id: String::new(),
            account_display_name: "Unauthenticated".into(),
            room_id: None,
            match_id: None,
            connection_state: SpectatorConnectionState::Disconnected,
            connected_at: None,
            latency_ms: None,
            total_events_received: 0,
        }
    }
}
