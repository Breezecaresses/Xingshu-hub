use async_trait::async_trait;
use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use crate::domain::card::{Card, CardInventory};
use crate::domain::character::{Character, CharacterProgress};
use crate::domain::error::HubResult;
use crate::domain::match_record::Match;
use crate::domain::player::PlayerProfile;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub enum DataConfidence {
    High,
    Medium,
    Low,
    Speculative,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub enum DataFreshness {
    Realtime,
    Cached,
    Historical,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DataEnvelope<T> {
    pub data: T,
    pub provider_id: String,
    pub observed_at: DateTime<Utc>,
    pub confidence: DataConfidence,
    pub source_version: Option<String>,
    pub freshness: DataFreshness,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProviderMetadata {
    pub id: String,
    pub name: String,
    pub provider_type: String, // "OfficialApi", "LocalGame", "LocalLog", "XingShuRuntime", "ImportedData"
    pub version: String,
    pub description: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProviderCapabilities {
    pub supports_profile: bool,
    pub supports_characters: bool,
    pub supports_character_progress: bool,
    pub supports_cards: bool,
    pub supports_card_inventory: bool,
    pub supports_matches: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProbeResult {
    pub available: bool,
    pub latency_ms: Option<u64>,
    pub status_message: String,
}

#[derive(Debug, Clone)]
pub struct ProviderContext {
    pub game_root: Option<std::path::PathBuf>,
    pub profile_id: Option<String>,
}

#[async_trait]
pub trait IDataProvider: Send + Sync {
    fn metadata(&self) -> ProviderMetadata;
    fn capabilities(&self) -> ProviderCapabilities;
    async fn probe(&self, ctx: &ProviderContext) -> HubResult<ProbeResult>;

    async fn player_profile(&self, ctx: &ProviderContext) -> HubResult<Option<DataEnvelope<PlayerProfile>>>;
    async fn characters(&self, ctx: &ProviderContext) -> HubResult<Vec<DataEnvelope<Character>>>;
    async fn character_progress(&self, ctx: &ProviderContext) -> HubResult<Vec<DataEnvelope<CharacterProgress>>>;
    async fn cards(&self, ctx: &ProviderContext) -> HubResult<Vec<DataEnvelope<Card>>>;
    async fn card_inventory(&self, ctx: &ProviderContext) -> HubResult<Vec<DataEnvelope<CardInventory>>>;
    async fn matches(&self, ctx: &ProviderContext) -> HubResult<Vec<DataEnvelope<Match>>>;
}
