use crate::domain::card::CardInventory;
use crate::domain::character::CharacterProgress;
use crate::domain::error::HubResult;
use crate::domain::player::PlayerProfile;
use crate::storage::database::Database;
use crate::storage::repositories::PlayerRepository;

pub struct PlayerService {
    player_repo: PlayerRepository,
}

impl PlayerService {
    pub fn new(db: Database) -> Self {
        Self {
            player_repo: PlayerRepository::new(db),
        }
    }

    pub fn get_default_profile(&self) -> HubResult<Option<PlayerProfile>> {
        self.player_repo.get_default_profile()
    }

    pub fn list_character_progress(&self, profile_id: &str) -> HubResult<Vec<CharacterProgress>> {
        self.player_repo.list_character_progress(profile_id)
    }

    pub fn list_card_inventory(&self, profile_id: &str) -> HubResult<Vec<CardInventory>> {
        self.player_repo.list_card_inventory(profile_id)
    }
}
