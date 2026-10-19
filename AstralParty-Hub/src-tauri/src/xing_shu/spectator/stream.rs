use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SpectatorPlayerState {
    pub player_id: String,
    pub nickname: String,
    pub character_id: String,
    pub hp: i32,
    pub max_hp: i32,
    pub stars: i32,
    pub hand_cards: Vec<String>,
    pub status_effects: Vec<String>,
    pub is_current_turn: bool,
    pub ranking: u32,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SpectatorMatchSnapshot {
    pub match_id: String,
    pub room_id: String,
    pub round_number: u32,
    pub elapsed_seconds: u64,
    pub players: Vec<SpectatorPlayerState>,
    pub timestamp: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "event_type", content = "data")]
pub enum SpectatorEvent {
    RoomJoined { room_id: String, player_count: u32 },
    MatchStarted { match_id: String, player_ids: Vec<String> },
    RoundStarted { round: u32 },
    TurnStarted { player_id: String },
    DiceRolled { player_id: String, roll: u32 },
    CardPlayed { player_id: String, card_id: String, target_id: Option<String> },
    Combat { attacker_id: String, defender_id: String, damage: i32 },
    HandUpdated { player_id: String, hand_count: usize, cards: Vec<String> },
    MatchEnded { winner_id: String, match_id: String },
}

pub use crate::xing_shu::bridge::protocol::{CardCountEntry, PlayerHandSummary, SpectatorHandPayload};

