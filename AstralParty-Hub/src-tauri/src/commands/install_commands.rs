use std::path::Path;
use tauri::State;
use crate::application::InstallService;
use crate::domain::error::HubResult;
use crate::domain::installation::{Backup, InstallTransaction};

#[tauri::command]
pub async fn install_package(
    archive_path: String,
    installation_id: String,
    install_service: State<'_, InstallService>,
) -> HubResult<InstallTransaction> {
    install_service.install_package_from_zip(Path::new(&archive_path), &installation_id)
}

#[tauri::command]
pub async fn rollback_transaction(
    transaction_id: String,
    install_service: State<'_, InstallService>,
) -> HubResult<()> {
    install_service.rollback_transaction(&transaction_id)
}

#[tauri::command]
pub async fn list_install_transactions(
    limit: Option<usize>,
    install_service: State<'_, InstallService>,
) -> HubResult<Vec<InstallTransaction>> {
    install_service.list_transactions(limit.unwrap_or(20))
}

#[tauri::command]
pub async fn list_backups(
    install_service: State<'_, InstallService>,
) -> HubResult<Vec<Backup>> {
    install_service.list_backups()
}
