use std::path::Path;
use tauri::State;
use crate::application::ExtensionService;
use crate::domain::error::HubResult;
use crate::domain::mod_package::ModPackage;
use crate::domain::plugin::Plugin;
use crate::packages::manifest::HubPackageManifestV1;

#[tauri::command]
pub async fn list_plugins(
    extension_service: State<'_, ExtensionService>,
) -> HubResult<Vec<Plugin>> {
    extension_service.list_plugins()
}

#[tauri::command]
pub async fn list_mods(
    extension_service: State<'_, ExtensionService>,
) -> HubResult<Vec<ModPackage>> {
    extension_service.list_mods()
}

#[tauri::command]
pub async fn inspect_package_archive(
    archive_path: String,
    extension_service: State<'_, ExtensionService>,
) -> HubResult<HubPackageManifestV1> {
    extension_service.inspect_package_archive(Path::new(&archive_path))
}

#[tauri::command]
pub async fn uninstall_extension(
    extension_id: String,
    is_plugin: bool,
    installation_id: String,
    center: State<'_, crate::extension_center::ExtensionCenter>,
    extension_service: State<'_, ExtensionService>,
) -> HubResult<()> {
    let game = crate::extension_center::get_game(&center.db, &installation_id)?;
    let inventory = crate::extension_center::inventory(&center.db, &game)?;
    if !inventory.entries.iter().any(|e| e.id == extension_id && e.managed && (e.category == "apx-plugin") == is_plugin) {
        return Err(crate::domain::error::HubError::security_violation("扩展不属于所选安装位置。"));
    }
    let root = crate::extension_center::game_root(&game)?;
    for file in crate::storage::repositories::ExtensionRepository::new(center.db.clone()).get_installed_files_by_package(&extension_id)? {
        crate::security::path_guard::PathGuard::ensure_within_root(&root, Path::new(&file.target_path))?;
    }
    extension_service.uninstall_extension(&extension_id, is_plugin, Path::new(game.executable_path.as_deref().unwrap_or_default()))
}
