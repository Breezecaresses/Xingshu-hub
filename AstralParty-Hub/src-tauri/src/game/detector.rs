use std::path::{Path, PathBuf};
use crate::game::installation::{DetectedGameDetails, DetectionEvidence};
use crate::game::steam::SteamDetector;

pub struct GameDetector;

impl GameDetector {
    /// Attempts automatic detection via Steam libraries
    pub fn detect_game() -> Option<DetectedGameDetails> {
        if let Some(steam_manifest) = SteamDetector::find_astral_party_manifest() {
            let mut details = Self::validate_game_directory(&steam_manifest.full_game_path);
            details.build_id = Some(steam_manifest.build_id.clone());
            details.evidences.insert(
                0,
                DetectionEvidence {
                    source: "STEAM_APPMANIFEST".into(),
                    path: steam_manifest.full_game_path.display().to_string(),
                    detail: format!("AppID: {}, BuildID: {}", steam_manifest.app_id, steam_manifest.build_id),
                    passed: true,
                },
            );
            return Some(details);
        }

        // Try direct fallback checks
        let common_paths = [
            r"E:\steam\steamapps\common\Astral Party",
            r"C:\Program Files (x86)\Steam\steamapps\common\Astral Party",
            r"D:\SteamLibrary\steamapps\common\Astral Party",
        ];

        for p_str in common_paths {
            let p = Path::new(p_str);
            if p.exists() {
                let details = Self::validate_game_directory(p);
                if details.is_valid {
                    return Some(details);
                }
            }
        }

        None
    }

    /// Validates an arbitrary game directory (automatic or user-specified)
    pub fn validate_game_directory<P: AsRef<Path>>(dir: P) -> DetectedGameDetails {
        let root = dir.as_ref();
        let mut evidences = Vec::new();

        // 1. Check directory existence
        let exists = root.exists() && root.is_dir();
        evidences.push(DetectionEvidence {
            source: "DIRECTORY_EXISTENCE".into(),
            path: root.display().to_string(),
            detail: if exists { "Directory exists".into() } else { "Directory not found".into() },
            passed: exists,
        });

        if !exists {
            return DetectedGameDetails {
                install_path: root.display().to_string(),
                executable_path: None,
                edition: "UNKNOWN".into(),
                build_id: None,
                version: None,
                evidences,
                is_valid: false,
            };
        }

        // 2. Check Astral Party sub-editions
        // Astral Party on Steam typically houses:
        // - 8vJXn6CN\AstralParty_CN.exe (Chinese edition)
        // - 8vJXnINT\AstralParty_INT.exe (International edition)
        let cn_exe = root.join("8vJXn6CN").join("AstralParty_CN.exe");
        let int_exe = root.join("8vJXnINT").join("AstralParty_INT.exe");
        let root_exe = root.join("AstralParty.exe");

        let cn_exists = cn_exe.exists();
        let int_exists = int_exe.exists();
        let root_exists = root_exe.exists();

        let mut primary_exe: Option<PathBuf> = None;
        let mut edition = "UNKNOWN".to_string();

        if cn_exists && int_exists {
            edition = "DUAL".into();
            primary_exe = Some(cn_exe.clone());
        } else if cn_exists {
            edition = "CN".into();
            primary_exe = Some(cn_exe.clone());
        } else if int_exists {
            edition = "INT".into();
            primary_exe = Some(int_exe.clone());
        } else if root_exists {
            edition = "STANDARD".into();
            primary_exe = Some(root_exe.clone());
        }

        evidences.push(DetectionEvidence {
            source: "EXECUTABLE_SIGNATURE".into(),
            path: primary_exe
                .as_ref()
                .map(|p| p.display().to_string())
                .unwrap_or_else(|| root.display().to_string()),
            detail: format!("Edition identified: {edition} (CN: {cn_exists}, INT: {int_exists}, Root: {root_exists})"),
            passed: primary_exe.is_some(),
        });

        // 3. Unity & IL2CPP signatures
        let cn_assembly = root.join("8vJXn6CN").join("GameAssembly.dll");
        let int_assembly = root.join("8vJXnINT").join("GameAssembly.dll");
        let root_assembly = root.join("GameAssembly.dll");
        let assembly_exists = cn_assembly.exists() || int_assembly.exists() || root_assembly.exists();

        evidences.push(DetectionEvidence {
            source: "UNITY_IL2CPP_SIGNATURE".into(),
            path: root.display().to_string(),
            detail: if assembly_exists { "GameAssembly.dll verified" } else { "GameAssembly.dll not found" }.into(),
            passed: assembly_exists,
        });

        let cn_player = root.join("8vJXn6CN").join("UnityPlayer.dll");
        let int_player = root.join("8vJXnINT").join("UnityPlayer.dll");
        let root_player = root.join("UnityPlayer.dll");
        let player_exists = cn_player.exists() || int_player.exists() || root_player.exists();

        evidences.push(DetectionEvidence {
            source: "UNITY_PLAYER_SIGNATURE".into(),
            path: root.display().to_string(),
            detail: if player_exists { "UnityPlayer.dll verified" } else { "UnityPlayer.dll not found" }.into(),
            passed: player_exists,
        });

        // Game is valid if executable exists and core runtime assemblies exist
        let is_valid = primary_exe.is_some() && (assembly_exists || player_exists);

        DetectedGameDetails {
            install_path: root.display().to_string(),
            executable_path: primary_exe.map(|p| p.display().to_string()),
            edition,
            build_id: None,
            version: None,
            evidences,
            is_valid,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_detect_astral_party_local() {
        let details = GameDetector::detect_game();
        if let Some(d) = details {
            assert!(d.is_valid);
            assert!(d.install_path.contains("Astral Party"));
            assert_eq!(d.build_id, Some("25951331".to_string()));
        }
    }
}

