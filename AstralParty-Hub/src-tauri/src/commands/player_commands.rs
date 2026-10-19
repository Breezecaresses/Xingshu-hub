use tauri::State;
use crate::application::PlayerService;
use crate::domain::card::CardInventory;
use crate::domain::character::CharacterProgress;
use crate::domain::error::HubResult;
use crate::domain::player::PlayerProfile;

#[tauri::command]
pub async fn get_player_profile(
    player_service: State<'_, PlayerService>,
) -> HubResult<Option<PlayerProfile>> {
    player_service.get_default_profile()
}

#[tauri::command]
pub async fn list_character_progress(
    profile_id: String,
    player_service: State<'_, PlayerService>,
) -> HubResult<Vec<CharacterProgress>> {
    player_service.list_character_progress(&profile_id)
}

#[tauri::command]
pub async fn list_card_inventory(
    profile_id: String,
    player_service: State<'_, PlayerService>,
) -> HubResult<Vec<CardInventory>> {
    player_service.list_card_inventory(&profile_id)
}
