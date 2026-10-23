use crate::desktop_host::State;
use crate::application::GameService;
use crate::domain::error::HubResult;
use crate::domain::installation::GameInstallation;

#[cfg_attr(feature = "web-ui", tauri::command)]
pub async fn detect_game(game_service: State<'_, GameService>) -> HubResult<GameInstallation> {
    game_service.auto_detect_game()
}

#[cfg_attr(feature = "web-ui", tauri::command)]
pub async fn set_manual_game_path(
    path: String,
    game_service: State<'_, GameService>,
) -> HubResult<GameInstallation> {
    game_service.set_manual_game_path(&path)
}

#[cfg_attr(feature = "web-ui", tauri::command)]
pub async fn get_primary_game(
    game_service: State<'_, GameService>,
) -> HubResult<Option<GameInstallation>> {
    game_service.get_primary_game()
}
