use chrono::Utc;
use serde::{Deserialize, Serialize};

pub const XINGSHU_BRIDGE_PROTOCOL_VERSION: &str = "0.1";
pub const XINGSHU_BRIDGE_NAMED_PIPE: &str = r"\\.\pipe\XingShuBridge_v1";
pub const XINGSHU_BRIDGE_LOCALHOST_PORT: u16 = 48231;
pub const XINGSHU_DIAGNOSTIC_TCP_PORT: u16 = 25890;
pub const XINGSHU_DIAGNOSTIC_TCP_ADDR: &str = "127.0.0.1:25890";
pub const XINGSHU_BRIDGE_TCP_PORT: u16 = 25890;
pub const XINGSHU_BRIDGE_TCP_ADDR: &str = "127.0.0.1:25890";
pub const MAX_MESSAGE_SIZE: usize = 64 * 1024; // 64 KB

pub mod message_types {
    pub const HELLO: &str = "HELLO";
    pub const HELLO_ACK: &str = "HELLO_ACK";
    pub const HEARTBEAT: &str = "HEARTBEAT";
    pub const RUNTIME_STATUS: &str = "RUNTIME_STATUS";
    pub const HUB_STATUS: &str = "HUB_STATUS";
    pub const TEST_EVENT: &str = "TEST_EVENT";
    pub const ERROR: &str = "ERROR";
    pub const SPECTATOR_HAND_SNAPSHOT: &str = "SPECTATOR_HAND_SNAPSHOT";
    pub const RUNTIME_IDENTITY_UPDATE: &str = "RUNTIME_IDENTITY_UPDATE";
    pub const WATCH_TARGET: &str = "WATCH_TARGET";
    pub const WATCH_JOIN_ACCEPTED: &str = "WATCH_JOIN_ACCEPTED";
    pub const WATCH_JOIN_REJECTED: &str = "WATCH_JOIN_REJECTED";
    pub const WATCH_JOIN_TIMEOUT: &str = "WATCH_JOIN_TIMEOUT";
    pub const WATCH_ALREADY_JOINED: &str = "WATCH_ALREADY_JOINED";
    pub const WATCH_TARGET_INVALID: &str = "WATCH_TARGET_INVALID";
    pub const HAND_TRUTH_MIRROR: &str = "HAND_TRUTH_MIRROR";
    pub const RUNTIME_MATCH_CONTEXT: &str = "RUNTIME_MATCH_CONTEXT";
}

pub mod error_codes {
    pub const BUSY_SINGLE_CLIENT_ONLY: &str = "BUSY_SINGLE_CLIENT_ONLY";
    pub const AUTH_CHALLENGE_FAILED: &str = "AUTH_CHALLENGE_FAILED";
    pub const AUTH_CHALLENGE_REQUIRED: &str = "AUTH_CHALLENGE_REQUIRED";
    pub const MALFORMED_HELLO: &str = "MALFORMED_HELLO";
    pub const UNSUPPORTED_PROTOCOL: &str = "UNSUPPORTED_PROTOCOL";
    pub const PROTOCOL_GATE_VIOLATION: &str = "PROTOCOL_GATE_VIOLATION";
    pub const MALFORMED_HELLO_PAYLOAD: &str = "MALFORMED_HELLO_PAYLOAD";
    pub const INVALID_COMPONENT: &str = "INVALID_COMPONENT";
    pub const INVALID_CAPABILITIES: &str = "INVALID_CAPABILITIES";
    pub const CAPABILITIES_REQUIRED: &str = "CAPABILITIES_REQUIRED";
    pub const UNKNOWN_CAPABILITY: &str = "UNKNOWN_CAPABILITY";
    pub const INVALID_GAME_PID: &str = "INVALID_GAME_PID";
    pub const MISSING_GAME_PID: &str = "MISSING_GAME_PID";
    pub const PID_MISMATCH: &str = "PID_MISMATCH";
    pub const SECURITY_TRUST_VIOLATION: &str = "SECURITY_TRUST_VIOLATION";
    pub const PIPE_NAME_ALREADY_OWNED: &str = "PIPE_NAME_ALREADY_OWNED";
}

pub const ALLOWED_CAPABILITIES: &[&str] = &[
    "status", "events", "spectator_hand_snapshot", "RUNTIME", "SPECTATOR", "HOOKS", "CHAT", "METRICS",
];

/// Security Rule: Bridge protocol strictly prohibits transmitting sensitive credentials,
/// including passwords, Steam tickets/session tickets, OAuth access tokens, refresh tokens,
/// or authentication secrets.
pub const PROHIBITED_CREDENTIAL_KEYS: &[&str] = &[
    "password", "steam_ticket", "steamTicket", "access_token", "accessToken",
    "refresh_token", "refreshToken", "session_token", "sessionToken", "secret",
];

/// Connection state tracking for XingShu Bridge Named Pipe
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Default)]
#[serde(rename_all = "SCREAMING_SNAKE_CASE")]
pub enum BridgeServerState {
    #[default]
    Disconnected,
    PipeConnected,
    RuntimeAuthenticated,
}

impl std::fmt::Display for BridgeServerState {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            BridgeServerState::Disconnected => write!(f, "DISCONNECTED"),
            BridgeServerState::PipeConnected => write!(f, "PIPE_CONNECTED"),
            BridgeServerState::RuntimeAuthenticated => write!(f, "RUNTIME_AUTHENTICATED"),
        }
    }
}

/// Standard XingShu Bridge JSON Message Envelope V0.1
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct BridgeEnvelope<T = serde_json::Value> {
    pub protocol_version: String,
    pub message_type: String,
    pub request_id: String,
    pub timestamp: i64,
    pub payload: T,
}

impl<T> BridgeEnvelope<T> {
    pub fn new(message_type: impl Into<String>, payload: T) -> Self {
        Self {
            protocol_version: XINGSHU_BRIDGE_PROTOCOL_VERSION.to_string(),
            message_type: message_type.into(),
            request_id: uuid::Uuid::new_v4().to_string(),
            timestamp: Utc::now().timestamp_millis(),
            payload,
        }
    }

    pub fn with_request_id(
        message_type: impl Into<String>,
        request_id: impl Into<String>,
        payload: T,
    ) -> Self {
        Self {
            protocol_version: XINGSHU_BRIDGE_PROTOCOL_VERSION.to_string(),
            message_type: message_type.into(),
            request_id: request_id.into(),
            timestamp: Utc::now().timestamp_millis(),
            payload,
        }
    }
}

// ==========================================
// Payload Definitions V0.1
// ==========================================

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Default)]
#[serde(rename_all = "camelCase")]
pub struct HelloPayload {
    #[serde(default)]
    pub component: Option<String>,
    #[serde(default)]
    pub runtime_version: Option<String>,
    #[serde(default)]
    pub game_pid: Option<u32>,
    #[serde(default)]
    pub game_version: Option<String>,
    #[serde(default)]
    pub capabilities: Option<Vec<String>>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub session_challenge: Option<String>,
    #[serde(default, alias = "platform_id", skip_serializing_if = "Option::is_none")]
    pub platform_id: Option<String>,
    #[serde(default, alias = "player_id", skip_serializing_if = "Option::is_none")]
    pub player_id: Option<i64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub role: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Default)]
#[serde(rename_all = "camelCase")]
pub struct HelloAckPayload {
    pub status: String,
    pub hub_version: String,
    pub protocol_version: String,
    pub heartbeat_interval_ms: u64,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub session_challenge: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub session_id: Option<String>,
    #[serde(default, alias = "connection_id", skip_serializing_if = "Option::is_none")]
    pub connection_id: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub generation: Option<u64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub capabilities: Option<Vec<String>>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Default)]
#[serde(rename_all = "camelCase")]
pub struct HeartbeatPayload {
    #[serde(default)]
    pub sequence: u64,
    #[serde(default)]
    pub client_timestamp: Option<i64>,
    #[serde(default)]
    pub server_timestamp: Option<i64>,
    #[serde(default)]
    pub echo_timestamp: Option<i64>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Default)]
#[serde(rename_all = "camelCase")]
pub struct RuntimeStatusPayload {
    #[serde(default)]
    pub game_pid: Option<u32>,
    #[serde(default)]
    pub game_version: Option<String>,
    #[serde(default)]
    pub runtime_version: Option<String>,
    #[serde(default)]
    pub is_initialized: bool,
    #[serde(default)]
    pub in_match: Option<bool>,
    #[serde(default, alias = "player_id", skip_serializing_if = "Option::is_none")]
    pub player_id: Option<i64>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Default)]
#[serde(rename_all = "camelCase")]
pub struct RuntimeIdentityUpdatePayload {
    #[serde(default, alias = "player_id", alias = "localPlayerId", alias = "local_player_id")]
    pub player_id: i64,
    #[serde(default, alias = "account_id", skip_serializing_if = "Option::is_none")]
    pub account_id: Option<u64>,
    #[serde(default, alias = "display_name", skip_serializing_if = "Option::is_none")]
    pub display_name: Option<String>,
    #[serde(default, alias = "game_pid", alias = "processId", alias = "process_id", skip_serializing_if = "Option::is_none")]
    pub game_pid: Option<u32>,
    #[serde(default, alias = "platform_id", alias = "platformId", skip_serializing_if = "Option::is_none")]
    pub platform_id: Option<String>,
    #[serde(default, alias = "is_self", skip_serializing_if = "Option::is_none")]
    pub is_self: Option<bool>,
    #[serde(default, alias = "identitySource", alias = "identity_source", skip_serializing_if = "Option::is_none")]
    pub identity_source: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Default)]
#[serde(rename_all = "camelCase")]
pub struct RuntimeMatchContext {
    #[serde(default, alias = "connection_id")]
    pub connection_id: String,
    #[serde(default, alias = "platform_id")]
    pub platform_id: String,
    #[serde(default, alias = "process_id", alias = "processId", alias = "game_pid", alias = "gamePid")]
    pub process_id: u32,
    #[serde(default, alias = "local_player_id", alias = "localPlayerId", alias = "player_id", alias = "playerId", skip_serializing_if = "Option::is_none")]
    pub local_player_id: Option<i64>,
    #[serde(default = "default_match_state", alias = "match_state")]
    pub match_state: String, // e.g. MAIN_MENU, LOBBY, MATCH_LOADING, MAIN_IN_MATCH, MATCH_ENDED
    #[serde(default, alias = "match_kind", skip_serializing_if = "Option::is_none")]
    pub match_kind: Option<String>, // e.g. PVE, PVP
    #[serde(default, alias = "internal_room_id", alias = "internalRoomId", skip_serializing_if = "Option::is_none")]
    pub internal_room_id: Option<i64>,
    #[serde(default, alias = "spectator_code", alias = "spectatorCode", alias = "watch_code", alias = "watchCode", skip_serializing_if = "Option::is_none")]
    pub spectator_code: Option<String>, // STRING! Never parsed as integer, never hashed into roomId
    #[serde(default, alias = "player_count")]
    pub player_count: u32,
    #[serde(default, alias = "roster_player_ids", alias = "rosterPlayerIds")]
    pub roster_player_ids: Vec<i64>,
    #[serde(default, alias = "scene", skip_serializing_if = "Option::is_none")]
    pub scene: Option<String>,
    #[serde(default, alias = "timestamp")]
    pub timestamp: u64,
    #[serde(default, alias = "sequence")]
    pub sequence: u64,

    // Backward compatibility fields
    #[serde(default, alias = "room_id")]
    pub room_id: u64,
    #[serde(default, alias = "in_match")]
    pub in_match: bool,
    #[serde(default, alias = "is_pve", skip_serializing_if = "Option::is_none")]
    pub is_pve: Option<bool>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub generation: Option<u64>,
}

fn default_match_state() -> String {
    "UNKNOWN".to_string()
}

impl RuntimeMatchContext {
    pub fn is_in_match(&self) -> bool {
        matches!(self.match_state.as_str(), "MAIN_IN_MATCH" | "SPECTATING")
    }

    pub fn is_pve_match(&self) -> bool {
        self.match_kind.as_deref() == Some("PVE") || self.is_pve.unwrap_or(false)
    }

    pub fn get_internal_room_id(&self) -> Option<i64> {
        self.internal_room_id.or_else(|| if self.room_id > 0 { Some(self.room_id as i64) } else { None })
    }

    pub fn normalize(&mut self) {
        if self.internal_room_id.is_none() && self.room_id > 0 {
            self.internal_room_id = Some(self.room_id as i64);
        }
        if self.room_id == 0 {
            if let Some(r) = self.internal_room_id {
                if r > 0 {
                    self.room_id = r as u64;
                }
            }
        }
        if self.match_state.is_empty() {
            self.match_state = if self.in_match {
                "MAIN_IN_MATCH".to_string()
            } else {
                "MAIN_MENU".to_string()
            };
        }
        self.in_match = self.is_in_match();
        if self.match_kind.is_none() {
            self.match_kind = Some(if self.is_pve == Some(true) { "PVE" } else { "UNKNOWN" }.to_string());
        }
        self.is_pve = match self.match_kind.as_deref() {
            Some("PVE") => Some(true),
            Some("PVP") => Some(false),
            _ => None,
        };
        if self.player_count == 0 && !self.roster_player_ids.is_empty() {
            self.player_count = self.roster_player_ids.len() as u32;
        }
        if self.timestamp == 0 {
            self.timestamp = chrono::Utc::now().timestamp_millis() as u64;
        }
    }
}

pub type RuntimeMatchContextPayload = RuntimeMatchContext;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct HubStatusPayload {
    pub status: String,
    pub server_time: i64,
    pub active_clients: usize,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Default)]
#[serde(rename_all = "camelCase")]
pub struct TestEventPayload {
    pub event_name: String,
    #[serde(default)]
    pub sender: String,
    #[serde(default)]
    pub message: String,
    #[serde(default)]
    pub extra: Option<serde_json::Value>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct WatchTargetPayload {
    pub target: serde_json::Value,
    pub request_id: String,
    pub timestamp: i64,
    pub target_platform: String,
    pub expected_spectator_player_id: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Default)]
#[serde(rename_all = "camelCase")]
pub struct WatchTargetResponsePayload {
    pub status: String,
    pub request_id: String,
    #[serde(default)]
    pub target: Option<serde_json::Value>,
    #[serde(default)]
    pub reason: Option<String>,
    #[serde(default)]
    pub timestamp: Option<i64>,
}


#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq, Default)]
#[serde(rename_all = "camelCase")]
pub struct CardCountEntry {
    pub card_id: i32,
    pub count: u32,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq, Default)]
#[serde(rename_all = "camelCase")]
pub struct PlayerHandSummary {
    pub player_id: u64,
    pub hand_size: usize,
    pub card_ids: Vec<i32>,
    pub cards: Vec<CardCountEntry>,
    pub draw_count_total: u32,
    pub play_count_total: u32,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct SpectatorHandPayload {
    pub schema_version: String,
    pub room_id: u64,
    pub spectator_session_epoch: u64,
    pub spectator_account_player_id: u64,
    pub sequence_number: u64,
    pub snapshot_timestamp: String,
    pub player_id: u64,
    pub card_ids: Vec<i32>,
    pub cards: Vec<CardCountEntry>,
    pub hand_size: usize,
    pub draw_count_total: u32,
    pub play_count_total: u32,
    pub room_players: std::collections::HashMap<String, PlayerHandSummary>,
    #[serde(default, alias = "connection_id", skip_serializing_if = "Option::is_none")]
    pub connection_id: Option<String>,
    #[serde(default, alias = "platform_id", skip_serializing_if = "Option::is_none")]
    pub platform_id: Option<String>,
}

impl Default for SpectatorHandPayload {
    fn default() -> Self {
        Self {
            schema_version: "1.1.0".to_string(),
            room_id: 0,
            spectator_session_epoch: 0,
            spectator_account_player_id: 0,
            sequence_number: 0,
            snapshot_timestamp: String::new(),
            player_id: 0,
            card_ids: Vec::new(),
            cards: Vec::new(),
            hand_size: 0,
            draw_count_total: 0,
            play_count_total: 0,
            room_players: std::collections::HashMap::new(),
            connection_id: None,
            platform_id: None,
        }
    }
}

// ==========================================
// Compatibility Types for Spectator & Legacy
// ==========================================

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub enum ComponentIdentity {
    #[serde(rename = "XingShu_Hub")]
    Hub,
    #[serde(rename = "XingShu_Spectator")]
    Spectator,
    #[serde(rename = "XingShu_Runtime")]
    Runtime,
}

impl std::fmt::Display for ComponentIdentity {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            ComponentIdentity::Hub => write!(f, "XingShu_Hub"),
            ComponentIdentity::Spectator => write!(f, "XingShu_Spectator"),
            ComponentIdentity::Runtime => write!(f, "XingShu_Runtime"),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BridgeHandshake {
    pub protocol_version: u32,
    pub component: ComponentIdentity,
    pub capabilities: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BridgeHandshakeAck {
    pub session_id: String,
    pub accepted_capabilities: Vec<String>,
    pub heartbeat_interval_ms: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BridgeHeartbeat {
    pub sequence: u64,
    pub timestamp: i64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BridgeHeartbeatAck {
    pub sequence: u64,
    pub timestamp: i64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BridgeError {
    pub code: String,
    pub message: String,
    pub recoverable: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "type", content = "data")]
pub enum BridgeMessage {
    Handshake(BridgeHandshake),
    HandshakeAck(BridgeHandshakeAck),
    Heartbeat(BridgeHeartbeat),
    HeartbeatAck(BridgeHeartbeatAck),
    MatchEvent { event_name: String, event_json: serde_json::Value },
    HandSync { player_key: String, cards: Vec<String> },
    Error(BridgeError),
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_envelope_serde_v01() {
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0-rc1.1".into()),
            game_pid: Some(12345),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into()]),
            session_challenge: None,
            ..Default::default()
        };

        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        let json = serde_json::to_string(&env).expect("Serialize envelope");

        assert!(json.contains(r#""protocolVersion":"0.1""#));
        assert!(json.contains(r#""messageType":"HELLO""#));
        assert!(json.contains(r#""gamePid":12345"#));

        let parsed: BridgeEnvelope<HelloPayload> =
            serde_json::from_str(&json).expect("Deserialize envelope");
        assert_eq!(parsed.protocol_version, "0.1");
        assert_eq!(parsed.message_type, "HELLO");
        assert_eq!(parsed.payload.game_pid, Some(12345));
    }

    #[test]
    fn test_hello_payload_with_session_challenge() {
        let challenge = "test-challenge-uuid-1234";
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(9999),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into()]),
            session_challenge: Some(challenge.into()),
            ..Default::default()
        };

        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        let json = serde_json::to_string(&env).expect("Serialize");
        assert!(json.contains("test-challenge-uuid-1234"));

        let parsed: BridgeEnvelope<HelloPayload> = serde_json::from_str(&json).unwrap();
        assert_eq!(parsed.payload.session_challenge.as_deref(), Some(challenge));

        let ack = HelloAckPayload {
            status: "ACCEPTED".into(),
            hub_version: "0.1.0".into(),
            protocol_version: "0.1".into(),
            heartbeat_interval_ms: 3000,
            session_challenge: Some(challenge.into()),
            session_id: Some("session-uuid-5678".into()),
            generation: None,
            capabilities: None,
            ..Default::default()
        };
        let ack_json = serde_json::to_string(&ack).unwrap();
        assert!(ack_json.contains("sessionChallenge"));
        assert!(ack_json.contains("sessionId"));

        let ack_parsed: HelloAckPayload = serde_json::from_str(&ack_json).unwrap();
        assert_eq!(ack_parsed.session_challenge.as_deref(), Some(challenge));
        assert_eq!(ack_parsed.session_id.as_deref(), Some("session-uuid-5678"));
    }

    #[test]
    fn test_bridge_server_state_display_and_serde() {
        assert_eq!(BridgeServerState::Disconnected.to_string(), "DISCONNECTED");
        assert_eq!(BridgeServerState::PipeConnected.to_string(), "PIPE_CONNECTED");
        assert_eq!(BridgeServerState::RuntimeAuthenticated.to_string(), "RUNTIME_AUTHENTICATED");

        let json = serde_json::to_string(&BridgeServerState::RuntimeAuthenticated).unwrap();
        assert_eq!(json, "\"RUNTIME_AUTHENTICATED\"");
        let parsed: BridgeServerState = serde_json::from_str(&json).unwrap();
        assert_eq!(parsed, BridgeServerState::RuntimeAuthenticated);
    }

    #[test]
    fn test_component_identity_display() {
        assert_eq!(ComponentIdentity::Hub.to_string(), "XingShu_Hub");
        assert_eq!(ComponentIdentity::Runtime.to_string(), "XingShu_Runtime");
        assert_eq!(ComponentIdentity::Spectator.to_string(), "XingShu_Spectator");
    }

    #[test]
    fn test_spectator_hand_payload_serde() {
        let mut room_players = std::collections::HashMap::new();
        room_players.insert(
            "2081758".to_string(),
            PlayerHandSummary {
                player_id: 2081758,
                hand_size: 2,
                card_ids: vec![20002, 10005],
                cards: vec![
                    CardCountEntry { card_id: 10005, count: 1 },
                    CardCountEntry { card_id: 20002, count: 1 },
                ],
                draw_count_total: 8,
                play_count_total: 3,
            },
        );

        let payload = SpectatorHandPayload {
            schema_version: "1.1.0".to_string(),
            room_id: 7777,
            spectator_session_epoch: 1,
            spectator_account_player_id: 1668707,
            sequence_number: 42,
            snapshot_timestamp: "2026-10-16T15:35:12.450Z".to_string(),
            player_id: 2081758,
            card_ids: vec![20002, 10005],
            cards: vec![
                CardCountEntry { card_id: 10005, count: 1 },
                CardCountEntry { card_id: 20002, count: 1 },
            ],
            hand_size: 2,
            draw_count_total: 8,
            play_count_total: 3,
            room_players,
            ..Default::default()
        };

        let env = BridgeEnvelope::new(
            message_types::SPECTATOR_HAND_SNAPSHOT,
            serde_json::to_value(&payload).unwrap(),
        );
        let json = serde_json::to_string(&env).expect("Serialize envelope");
        assert!(json.contains(r#""schemaVersion":"1.1.0""#));
        assert!(json.contains(r#""roomId":7777"#));
        assert!(json.contains(r#""spectatorSessionEpoch":1"#));
        assert!(json.contains(r#""spectatorAccountPlayerId":1668707"#));
        assert!(json.contains(r#""sequenceNumber":42"#));
        assert!(json.contains(r#""playerId":2081758"#));
        assert!(json.contains(r#""handSize":2"#));
        assert!(json.contains(r#""cardIds":[20002,10005]"#));
        assert!(json.contains(r#""cards":[{"cardId":10005,"count":1},{"cardId":20002,"count":1}]"#));

        let parsed: BridgeEnvelope<SpectatorHandPayload> =
            serde_json::from_str(&json).expect("Deserialize envelope");
        assert_eq!(parsed.message_type, message_types::SPECTATOR_HAND_SNAPSHOT);
        assert_eq!(parsed.payload.schema_version, "1.1.0");
        assert_eq!(parsed.payload.room_id, 7777);
        assert_eq!(parsed.payload.sequence_number, 42);
        assert_eq!(parsed.payload.cards.len(), 2);
        assert_eq!(parsed.payload.room_players.len(), 1);
    }

    #[test]
    fn test_runtime_match_context_payload_serde() {
        let mut ctx = RuntimeMatchContext {
            connection_id: "conn_steam_1".into(),
            platform_id: "STEAM_CN".into(),
            process_id: 12345,
            local_player_id: Some(1556514),
            match_state: "MAIN_IN_MATCH".into(),
            match_kind: Some("PVP".into()),
            internal_room_id: Some(88888),
            spectator_code: Some("C05FE7B".into()),
            player_count: 4,
            roster_player_ids: vec![1556514, 212667, 3045259, 444555],
            scene: Some("BattleScene".into()),
            timestamp: 1690000000000,
            sequence: 1,
            room_id: 88888,
            in_match: true,
            is_pve: Some(false),
            generation: Some(1),
        };
        ctx.normalize();

        let env = BridgeEnvelope::new(
            message_types::RUNTIME_MATCH_CONTEXT,
            serde_json::to_value(&ctx).unwrap(),
        );
        let json = serde_json::to_string(&env).expect("Serialize");
        assert!(json.contains(r#""messageType":"RUNTIME_MATCH_CONTEXT""#));
        assert!(json.contains(r#""platformId":"STEAM_CN""#));
        assert!(json.contains(r#""processId":12345"#));
        assert!(json.contains(r#""localPlayerId":1556514"#));
        assert!(json.contains(r#""internalRoomId":88888"#));
        assert!(json.contains(r#""spectatorCode":"C05FE7B""#));
        assert!(json.contains(r#""matchState":"MAIN_IN_MATCH""#));
        assert!(json.contains(r#""matchKind":"PVP""#));
        assert!(json.contains(r#""playerCount":4"#));
        assert!(json.contains(r#""rosterPlayerIds":[1556514,212667,3045259,444555]"#));

        let parsed: BridgeEnvelope<RuntimeMatchContext> =
            serde_json::from_str(&json).expect("Deserialize");
        assert_eq!(parsed.message_type, message_types::RUNTIME_MATCH_CONTEXT);
        assert_eq!(parsed.payload.platform_id, "STEAM_CN");
        assert_eq!(parsed.payload.process_id, 12345);
        assert_eq!(parsed.payload.local_player_id, Some(1556514));
        assert_eq!(parsed.payload.internal_room_id, Some(88888));
        assert_eq!(parsed.payload.spectator_code.as_deref(), Some("C05FE7B"));
        assert_eq!(parsed.payload.match_state, "MAIN_IN_MATCH");
        assert_eq!(parsed.payload.match_kind.as_deref(), Some("PVP"));
        assert_eq!(parsed.payload.player_count, 4);
        assert_eq!(parsed.payload.roster_player_ids.len(), 4);
        assert!(parsed.payload.is_in_match());

        // Test snake_case alias deserialization
        let raw_snake = r#"{
            "protocolVersion": "0.1",
            "messageType": "RUNTIME_MATCH_CONTEXT",
            "requestId": "test-id",
            "timestamp": 1234,
            "payload": {
                "connection_id": "conn_taptap_2",
                "platform_id": "TAPTAP",
                "game_pid": 9999,
                "player_id": 3045259,
                "room_id": 7777,
                "match_state": "LOBBY",
                "in_match": false,
                "is_pve": true,
                "spectator_code": "SP99AA"
            }
        }"#;
        let mut parsed_snake: BridgeEnvelope<RuntimeMatchContext> =
            serde_json::from_str(raw_snake).expect("Deserialize snake_case");
        parsed_snake.payload.normalize();
        assert_eq!(parsed_snake.payload.platform_id, "TAPTAP");
        assert_eq!(parsed_snake.payload.process_id, 9999);
        assert_eq!(parsed_snake.payload.local_player_id, Some(3045259));
        assert_eq!(parsed_snake.payload.internal_room_id, Some(7777));
        assert_eq!(parsed_snake.payload.spectator_code.as_deref(), Some("SP99AA"));
        assert_eq!(parsed_snake.payload.match_state, "LOBBY");
        assert!(!parsed_snake.payload.is_in_match());
        assert!(parsed_snake.payload.is_pve_match());
    }

    #[test]
    fn test_runtime_match_context_three_identifiers_distinction() {
        // Requirement 1:
        // 1. Player ID: number (e.g. 1556514)
        // 2. Internal Room ID: number | null (e.g. 351045)
        // 3. Public Spectator Code: STRING | null (e.g. 'C05FE7B')
        // Strictly separate: spectatorCode MUST be string. Never parse C05FE7B as integer,
        // never hash into roomId, never substitute.
        let ctx_json = r#"{
            "connectionId": "conn_steam_pve",
            "platformId": "STEAM_CN",
            "processId": 18244,
            "localPlayerId": 1556514,
            "matchState": "MAIN_IN_MATCH",
            "matchKind": "PVE",
            "internalRoomId": 351045,
            "spectatorCode": "C05FE7B",
            "playerCount": 4,
            "rosterPlayerIds": [1556514, 212667, 3045259, 999111],
            "scene": "BattleScene_PVE",
            "timestamp": 1729300000000,
            "sequence": 10
        }"#;

        let ctx: RuntimeMatchContext = serde_json::from_str(ctx_json).expect("Parse strict 3-identifier JSON");

        // 1. Player ID is strictly integer 1556514
        assert_eq!(ctx.local_player_id, Some(1556514i64));
        // 2. Internal Room ID is strictly integer 351045
        assert_eq!(ctx.internal_room_id, Some(351045i64));
        assert_eq!(ctx.get_internal_room_id(), Some(351045i64));
        // 3. Public Spectator Code is strictly string "C05FE7B"
        assert_eq!(ctx.spectator_code, Some("C05FE7B".to_string()));

        // Verification of Non-Substitution / Non-Collision:
        // - Spectator Code "C05FE7B" is NOT 351045
        assert_ne!(ctx.spectator_code.as_deref().unwrap(), &ctx.internal_room_id.unwrap().to_string());
        // - Spectator Code is NOT hex-parsed to integer (0xC05FE7B = 201719419)
        assert_ne!(ctx.internal_room_id.unwrap(), 0xC05FE7Bi64);
        // - Player ID is NOT room ID
        assert_ne!(ctx.local_player_id.unwrap(), ctx.internal_room_id.unwrap());
    }
}
