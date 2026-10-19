use async_trait::async_trait;
use chrono::Utc;
use std::fs;
use std::path::PathBuf;
use std::sync::{Arc, Mutex};
use crate::domain::card::{Card, CardInventory};
use crate::domain::character::{Character, CharacterProgress};
use crate::domain::error::{HubError, HubResult};
use crate::domain::match_record::Match;
use crate::domain::player::PlayerProfile;
use crate::providers::provider::{
    DataConfidence, DataEnvelope, DataFreshness, IDataProvider, ProbeResult, ProviderCapabilities,
    ProviderContext, ProviderMetadata,
};

#[derive(Default, Clone)]
pub struct ImportedDataProvider {
    cached_profile: Arc<Mutex<Option<PlayerProfile>>>,
    cached_characters: Arc<Mutex<Vec<Character>>>,
    cached_character_progress: Arc<Mutex<Vec<CharacterProgress>>>,
    cached_cards: Arc<Mutex<Vec<Card>>>,
    cached_card_inventory: Arc<Mutex<Vec<CardInventory>>>,
    cached_matches: Arc<Mutex<Vec<Match>>>,
}

impl ImportedDataProvider {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn load_from_json_string(&self, json_str: &str) -> HubResult<()> {
        let root: serde_json::Value = serde_json::from_str(json_str).map_err(|e| {
            HubError::new("IMPORT_PARSE_ERROR", format!("Failed to parse imported JSON: {e}"), "PROVIDER")
        })?;

        // 1. Ingest Profile
        if let Some(p) = root.get("profile") {
            if let Ok(profile) = serde_json::from_value::<PlayerProfile>(p.clone()) {
                let mut guard = self.cached_profile.lock().unwrap();
                *guard = Some(profile);
            }
        }

        // 2. Ingest Characters
        if let Some(chars) = root.get("characters").and_then(|v| v.as_array()) {
            let mut list = Vec::new();
            for c in chars {
                if let Ok(item) = serde_json::from_value::<Character>(c.clone()) {
                    list.push(item);
                }
            }
            let mut guard = self.cached_characters.lock().unwrap();
            *guard = list;
        }

        // 3. Ingest Progress
        if let Some(prog) = root.get("characterProgress").and_then(|v| v.as_array()) {
            let mut list = Vec::new();
            for p in prog {
                if let Ok(item) = serde_json::from_value::<CharacterProgress>(p.clone()) {
                    list.push(item);
                }
            }
            let mut guard = self.cached_character_progress.lock().unwrap();
            *guard = list;
        }

        // 4. Ingest Cards
        if let Some(cards) = root.get("cards").and_then(|v| v.as_array()) {
            let mut list = Vec::new();
            for c in cards {
                if let Ok(item) = serde_json::from_value::<Card>(c.clone()) {
                    list.push(item);
                }
            }
            let mut guard = self.cached_cards.lock().unwrap();
            *guard = list;
        }

        // 5. Ingest Card Inventory
        if let Some(inv) = root.get("cardInventory").and_then(|v| v.as_array()) {
            let mut list = Vec::new();
            for i in inv {
                if let Ok(item) = serde_json::from_value::<CardInventory>(i.clone()) {
                    list.push(item);
                }
            }
            let mut guard = self.cached_card_inventory.lock().unwrap();
            *guard = list;
        }

        Ok(())
    }

    pub fn load_from_file(&self, path: PathBuf) -> HubResult<()> {
        let content = fs::read_to_string(&path).map_err(|e| {
            HubError::new("FILE_READ_ERROR", format!("Failed to read import file '{}': {e}", path.display()), "PROVIDER")
        })?;
        self.load_from_json_string(&content)
    }
}

#[async_trait]
impl IDataProvider for ImportedDataProvider {
    fn metadata(&self) -> ProviderMetadata {
        ProviderMetadata {
            id: "ImportedDataProvider".into(),
            name: "User Data Import Provider".into(),
            provider_type: "ImportedData".into(),
            version: "1.0.0".into(),
            description: "Allows manual ingestion of player profiles, catalogs, and inventories via JSON export files.".into(),
        }
    }

    fn capabilities(&self) -> ProviderCapabilities {
        ProviderCapabilities {
            supports_profile: true,
            supports_characters: true,
            supports_character_progress: true,
            supports_cards: true,
            supports_card_inventory: true,
            supports_matches: true,
        }
    }

    async fn probe(&self, _ctx: &ProviderContext) -> HubResult<ProbeResult> {
        let has_data = self.cached_profile.lock().unwrap().is_some()
            || !self.cached_characters.lock().unwrap().is_empty();

        Ok(ProbeResult {
            available: true,
            latency_ms: Some(0),
            status_message: if has_data {
                "Imported data cache populated".into()
            } else {
                "Imported data cache empty (ready for file import)".into()
            },
        })
    }

    async fn player_profile(&self, _ctx: &ProviderContext) -> HubResult<Option<DataEnvelope<PlayerProfile>>> {
        let guard = self.cached_profile.lock().unwrap();
        Ok(guard.as_ref().map(|p| DataEnvelope {
            data: p.clone(),
            provider_id: "ImportedDataProvider".into(),
            observed_at: Utc::now(),
            confidence: DataConfidence::High,
            source_version: None,
            freshness: DataFreshness::Cached,
        }))
    }

    async fn characters(&self, _ctx: &ProviderContext) -> HubResult<Vec<DataEnvelope<Character>>> {
        let guard = self.cached_characters.lock().unwrap();
        Ok(guard
            .iter()
            .map(|c| DataEnvelope {
                data: c.clone(),
                provider_id: "ImportedDataProvider".into(),
                observed_at: Utc::now(),
                confidence: DataConfidence::High,
                source_version: None,
                freshness: DataFreshness::Cached,
            })
            .collect())
    }

    async fn character_progress(&self, _ctx: &ProviderContext) -> HubResult<Vec<DataEnvelope<CharacterProgress>>> {
        let guard = self.cached_character_progress.lock().unwrap();
        Ok(guard
            .iter()
            .map(|p| DataEnvelope {
                data: p.clone(),
                provider_id: "ImportedDataProvider".into(),
                observed_at: Utc::now(),
                confidence: DataConfidence::High,
                source_version: None,
                freshness: DataFreshness::Cached,
            })
            .collect())
    }

    async fn cards(&self, _ctx: &ProviderContext) -> HubResult<Vec<DataEnvelope<Card>>> {
        let guard = self.cached_cards.lock().unwrap();
        Ok(guard
            .iter()
            .map(|c| DataEnvelope {
                data: c.clone(),
                provider_id: "ImportedDataProvider".into(),
                observed_at: Utc::now(),
                confidence: DataConfidence::High,
                source_version: None,
                freshness: DataFreshness::Cached,
            })
            .collect())
    }

    async fn card_inventory(&self, _ctx: &ProviderContext) -> HubResult<Vec<DataEnvelope<CardInventory>>> {
        let guard = self.cached_card_inventory.lock().unwrap();
        Ok(guard
            .iter()
            .map(|i| DataEnvelope {
                data: i.clone(),
                provider_id: "ImportedDataProvider".into(),
                observed_at: Utc::now(),
                confidence: DataConfidence::High,
                source_version: None,
                freshness: DataFreshness::Cached,
            })
            .collect())
    }

    async fn matches(&self, _ctx: &ProviderContext) -> HubResult<Vec<DataEnvelope<Match>>> {
        let guard = self.cached_matches.lock().unwrap();
        Ok(guard
            .iter()
            .map(|m| DataEnvelope {
                data: m.clone(),
                provider_id: "ImportedDataProvider".into(),
                observed_at: Utc::now(),
                confidence: DataConfidence::High,
                source_version: None,
                freshness: DataFreshness::Cached,
            })
            .collect())
    }
}
