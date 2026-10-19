use tauri::State;
use crate::application::GameService;
use crate::domain::error::HubResult;
use crate::domain::installation::GameInstallation;

#[tauri::command]
pub async fn detect_game(game_service: State<'_, GameService>) -> HubResult<GameInstallation> {
    game_service.auto_detect_game()
}

#[tauri::command]
pub async fn set_manual_game_path(
    path: String,
    game_service: State<'_, GameService>,
) -> HubResult<GameInstallation> {
    game_service.set_manual_game_path(&path)
}

#[tauri::command]
pub async fn get_primary_game(
    game_service: State<'_, GameService>,
) -> HubResult<Option<GameInstallation>> {
    game_service.get_primary_game()
}
