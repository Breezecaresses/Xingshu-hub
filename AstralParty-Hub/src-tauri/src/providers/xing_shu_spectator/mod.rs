use async_trait::async_trait;
use chrono::Utc;
use std::sync::Arc;
use crate::domain::card::{Card, CardInventory};
use crate::domain::character::{Character, CharacterProgress};
use crate::domain::error::HubResult;
use crate::domain::match_record::Match;
use crate::domain::player::PlayerProfile;
use crate::providers::provider::{
    DataConfidence, DataEnvelope, DataFreshness, IDataProvider, ProbeResult, ProviderCapabilities,
    ProviderContext, ProviderMetadata,
};
use crate::xing_shu::spectator::session::SpectatorConnectionState;
use crate::xing_shu::spectator::service::SpectatorService;

#[derive(Clone)]
pub struct XingShuSpectatorProvider {
    spectator_service: Arc<SpectatorService>,
}

impl XingShuSpectatorProvider {
    pub fn new(spectator_service: Arc<SpectatorService>) -> Self {
        Self { spectator_service }
    }
}

#[async_trait]
impl IDataProvider for XingShuSpectatorProvider {
    fn metadata(&self) -> ProviderMetadata {
        ProviderMetadata {
            id: "XingShuSpectatorProvider".into(),
            name: "XingShu Spectator Telemetry Provider".into(),
            provider_type: "Spectator".into(),
            version: "1.0.0".into(),
            description: "Acquires real-time match telemetry and player hand state through spectator match stream.".into(),
        }
    }

    fn capabilities(&self) -> ProviderCapabilities {
        ProviderCapabilities {
            supports_profile: false,
            supports_characters: false,
            supports_character_progress: false,
            supports_cards: false,
            supports_card_inventory: false,
            supports_matches: true,
        }
    }

    async fn probe(&self, _ctx: &ProviderContext) -> HubResult<ProbeResult> {
        let info = self.spectator_service.get_session_info();
        let is_active = info.connection_state == SpectatorConnectionState::Idle
            || info.connection_state == SpectatorConnectionState::Spectating;

        Ok(ProbeResult {
            available: is_active,
            latency_ms: info.latency_ms.map(|l| l as u64),
            status_message: format!(
                "Spectator State: {:?} (Account: {})",
                info.connection_state, info.account_display_name
            ),
        })
    }

    async fn player_profile(&self, _ctx: &ProviderContext) -> HubResult<Option<DataEnvelope<PlayerProfile>>> {
        Ok(None)
    }

    async fn characters(&self, _ctx: &ProviderContext) -> HubResult<Vec<DataEnvelope<Character>>> {
        Ok(vec![])
    }

    async fn character_progress(&self, _ctx: &ProviderContext) -> HubResult<Vec<DataEnvelope<CharacterProgress>>> {
        Ok(vec![])
    }

    async fn cards(&self, _ctx: &ProviderContext) -> HubResult<Vec<DataEnvelope<Card>>> {
        Ok(vec![])
    }

    async fn card_inventory(&self, _ctx: &ProviderContext) -> HubResult<Vec<DataEnvelope<CardInventory>>> {
        Ok(vec![])
    }

    async fn matches(&self, _ctx: &ProviderContext) -> HubResult<Vec<DataEnvelope<Match>>> {
        if let Some(snapshot) = self.spectator_service.get_latest_snapshot() {
            let m = Match {
                id: snapshot.match_id.clone(),
                provider_id: Some("XingShuSpectatorProvider".into()),
                external_match_key: Some(snapshot.room_id.clone()),
                started_at: Some(snapshot.timestamp),
                ended_at: None,
                game_version: None,
                mode: Some("SpectatedLive".into()),
                map_key: None,
                result_json: Some(serde_json::json!({
                    "round": snapshot.round_number,
                    "elapsed_seconds": snapshot.elapsed_seconds,
                    "players": snapshot.players,
                })),
                created_at: Utc::now(),
            };

            Ok(vec![DataEnvelope {
                data: m,
                provider_id: "XingShuSpectatorProvider".into(),
                observed_at: Utc::now(),
                confidence: DataConfidence::High,
                source_version: Some("1.0.0".into()),
                freshness: DataFreshness::Realtime,
            }])
        } else {
            Ok(vec![])
        }
    }
}
