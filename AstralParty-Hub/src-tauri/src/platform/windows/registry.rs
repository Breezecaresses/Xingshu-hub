use std::path::PathBuf;
use winreg::enums::{HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE};
use winreg::RegKey;
use tracing::info;

pub fn find_steam_install_path() -> Option<PathBuf> {
    // 1. Try HKCU\Software\Valve\Steam
    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    if let Ok(steam_key) = hkcu.open_subkey("Software\\Valve\\Steam") {
        if let Ok(steam_path) = steam_key.get_value::<String, _>("SteamPath") {
            let path = PathBuf::from(steam_path.replace('/', "\\"));
            if path.exists() {
                info!("Found Steam path via HKCU registry: {}", path.display());
                return Some(path);
            }
        }
    }

    // 2. Try HKLM\SOFTWARE\WOW6432Node\Valve\Steam
    let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
    if let Ok(steam_key) = hklm.open_subkey("SOFTWARE\\WOW6432Node\\Valve\\Steam") {
        if let Ok(install_path) = steam_key.get_value::<String, _>("InstallPath") {
            let path = PathBuf::from(install_path.replace('/', "\\"));
            if path.exists() {
                info!("Found Steam path via HKLM WOW6432Node registry: {}", path.display());
                return Some(path);
            }
        }
    }

    // 3. Try standard HKLM\SOFTWARE\Valve\Steam
    if let Ok(steam_key) = hklm.open_subkey("SOFTWARE\\Valve\\Steam") {
        if let Ok(install_path) = steam_key.get_value::<String, _>("InstallPath") {
            let path = PathBuf::from(install_path.replace('/', "\\"));
            if path.exists() {
                info!("Found Steam path via HKLM registry: {}", path.display());
                return Some(path);
            }
        }
    }

    None
}
