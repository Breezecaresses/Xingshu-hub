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
    extension_service: State<'_, ExtensionService>,
) -> HubResult<()> {
    extension_service.uninstall_extension(&extension_id, is_plugin)
}
