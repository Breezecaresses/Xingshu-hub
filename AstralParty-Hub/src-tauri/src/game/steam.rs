use std::fs;
use std::path::{Path, PathBuf};
use tracing::info;
use crate::platform::windows::registry::find_steam_install_path;

pub const ASTRAL_PARTY_APP_ID: &str = "2622000";

#[derive(Debug, Clone)]
pub struct SteamGameManifest {
    pub app_id: String,
    pub name: String,
    pub install_dir: String,
    pub build_id: String,
    pub full_game_path: PathBuf,
}

pub struct SteamDetector;

impl SteamDetector {
    /// Discovers all Steam library paths across all drives using libraryfolders.vdf
    pub fn find_all_steam_libraries() -> Vec<PathBuf> {
        let mut libraries = Vec::new();

        if let Some(steam_root) = find_steam_install_path() {
            libraries.push(steam_root.clone());

            let vdf_path = steam_root.join("steamapps").join("libraryfolders.vdf");
            if vdf_path.exists() {
                if let Ok(content) = fs::read_to_string(&vdf_path) {
                    for line in content.lines() {
                        let trimmed = line.trim();
                        if trimmed.starts_with("\"path\"") {
                            // Extract path between quotes: "path"		"E:\\Steam"
                            let parts: Vec<&str> = trimmed.split('"').collect();
                            if parts.len() >= 4 {
                                let raw_path = parts[3].replace("\\\\", "\\");
                                let lib_path = PathBuf::from(raw_path);
                                if lib_path.exists() && !libraries.contains(&lib_path) {
                                    libraries.push(lib_path);
                                }
                            }
                        }
                    }
                }
            }
        }

        // Also check common default paths if not yet included
        let common_fallbacks = [
            r"C:\Program Files (x86)\Steam",
            r"C:\Program Files\Steam",
            r"D:\Steam",
            r"D:\SteamLibrary",
            r"E:\Steam",
            r"E:\SteamLibrary",
        ];
        for fb in common_fallbacks {
            let p = PathBuf::from(fb);
            if p.exists() && !libraries.contains(&p) {
                libraries.push(p);
            }
        }

        libraries
    }

    /// Finds Astral Party manifest in any Steam library
    pub fn find_astral_party_manifest() -> Option<SteamGameManifest> {
        let libraries = Self::find_all_steam_libraries();
        info!("Scanning {} Steam library locations for App ID {}", libraries.len(), ASTRAL_PARTY_APP_ID);

        for lib in libraries {
            let acf_path = lib
                .join("steamapps")
                .join(format!("appmanifest_{ASTRAL_PARTY_APP_ID}.acf"));

            if acf_path.exists() {
                if let Ok(content) = fs::read_to_string(&acf_path) {
                    if let Some(manifest) = Self::parse_manifest(&content, &lib) {
                        info!("Found Astral Party via Steam manifest at {}", acf_path.display());
                        return Some(manifest);
                    }
                }
            }
        }

        None
    }

    fn parse_manifest(content: &str, library_root: &Path) -> Option<SteamGameManifest> {
        let mut app_id = String::new();
        let mut name = String::new();
        let mut install_dir = String::new();
        let mut build_id = String::new();

        for line in content.lines() {
            let trimmed = line.trim();
            let parts: Vec<&str> = trimmed.split('"').collect();
            if parts.len() >= 4 {
                let key = parts[1].to_lowercase();
                let val = parts[3];
                match key.as_str() {
                    "appid" => app_id = val.to_string(),
                    "name" => name = val.to_string(),
                    "installdir" => install_dir = val.to_string(),
                    "buildid" => build_id = val.to_string(),
                    _ => {}
                }
            }
        }

        if !install_dir.is_empty() {
            let full_game_path = library_root.join("steamapps").join("common").join(&install_dir);
            Some(SteamGameManifest {
                app_id,
                name,
                install_dir,
                build_id,
                full_game_path,
            })
        } else {
            None
        }
    }
}
