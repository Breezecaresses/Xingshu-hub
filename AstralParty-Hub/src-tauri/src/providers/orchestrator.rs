use tracing::info;
use crate::domain::error::HubResult;
use crate::providers::provider::{ProbeResult, ProviderContext, ProviderMetadata};
use crate::providers::registry::ProviderRegistry;
use crate::storage::database::Database;
use crate::storage::repositories::{CatalogRepository, PlayerRepository};

pub struct ProviderOrchestrator {
    registry: ProviderRegistry,
    player_repo: PlayerRepository,
    catalog_repo: CatalogRepository,
}

impl ProviderOrchestrator {
    pub fn new(db: Database, registry: ProviderRegistry) -> Self {
        Self {
            registry,
            player_repo: PlayerRepository::new(db.clone()),
            catalog_repo: CatalogRepository::new(db),
        }
    }

    pub fn list_providers(&self) -> Vec<ProviderMetadata> {
        self.registry.list().iter().map(|p| p.metadata()).collect()
    }

    pub async fn probe_all(&self, ctx: &ProviderContext) -> Vec<(ProviderMetadata, ProbeResult)> {
        let mut results = Vec::new();
        for provider in self.registry.list() {
            let meta = provider.metadata();
            let probe_res = provider.probe(ctx).await.unwrap_or(ProbeResult {
                available: false,
                latency_ms: None,
                status_message: "Probe failed".into(),
            });
            results.push((meta, probe_res));
        }
        results
    }

    pub async fn sync_provider(&self, provider_id: &str, ctx: &ProviderContext) -> HubResult<usize> {
        let mut records_written = 0;
        if let Some(provider) = self.registry.get(provider_id) {
            info!("Running data sync for provider '{}'", provider_id);

            // Sync profile
            if let Ok(Some(envelope)) = provider.player_profile(ctx).await {
                self.player_repo.save_profile(&envelope.data)?;
                records_written += 1;
            }

            // Sync characters catalog
            if let Ok(characters) = provider.characters(ctx).await {
                for env in characters {
                    self.catalog_repo.save_character(&env.data)?;
                    records_written += 1;
                }
            }

            // Sync character progression
            if let Ok(progress) = provider.character_progress(ctx).await {
                for env in progress {
                    self.player_repo.save_character_progress(&env.data)?;
                    records_written += 1;
                }
            }

            // Sync cards catalog
            if let Ok(cards) = provider.cards(ctx).await {
                for env in cards {
                    self.catalog_repo.save_card(&env.data)?;
                    records_written += 1;
                }
            }

            // Sync card inventory
            if let Ok(inventory) = provider.card_inventory(ctx).await {
                for env in inventory {
                    self.player_repo.save_card_inventory(&env.data)?;
                    records_written += 1;
                }
            }
        }
        Ok(records_written)
    }
}
