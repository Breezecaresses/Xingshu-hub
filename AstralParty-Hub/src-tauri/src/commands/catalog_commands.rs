use crate::desktop_host::State;
use crate::application::CatalogService;
use crate::domain::card::Card;
use crate::domain::character::Character;
use crate::domain::error::HubResult;

#[cfg_attr(feature = "web-ui", tauri::command)]
pub async fn list_characters(
    catalog_service: State<'_, CatalogService>,
) -> HubResult<Vec<Character>> {
    catalog_service.list_characters()
}

#[cfg_attr(feature = "web-ui", tauri::command)]
pub async fn list_cards(
    catalog_service: State<'_, CatalogService>,
) -> HubResult<Vec<Card>> {
    catalog_service.list_cards()
}
