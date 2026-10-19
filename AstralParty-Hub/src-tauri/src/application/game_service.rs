use chrono::Utc;
use std::path::Path;
use uuid::Uuid;
use crate::domain::error::{HubError, HubResult};
use crate::domain::installation::GameInstallation;
use crate::game::detector::GameDetector;
use crate::storage::database::Database;
use crate::storage::repositories::GameRepository;

pub struct GameService {
    game_repo: GameRepository,
}

impl GameService {
    pub fn new(db: Database) -> Self {
        Self {
            game_repo: GameRepository::new(db),
        }
    }

    pub fn auto_detect_game(&self) -> HubResult<GameInstallation> {
        if let Some(detected) = GameDetector::detect_game() {
            let inst = GameInstallation {
                id: Uuid::new_v4().to_string(),
                platform: "steam".into(),
                platform_app_id: Some("2622000".into()),
                install_path: detected.install_path,
                executable_path: detected.executable_path,
                detected_build_id: detected.build_id,
                detected_version: detected.version,
                detection_method: "AUTO_STEAM".into(),
                is_valid: detected.is_valid,
                is_primary: true,
                last_verified_at: Some(Utc::now()),
                created_at: Utc::now(),
                updated_at: Utc::now(),
            };
            self.game_repo.save(&inst)?;
            Ok(inst)
        } else {
            Err(HubError::game_not_found("Automatic Steam detection found no library folder containing Astral Party."))
        }
    }

    pub fn set_manual_game_path(&self, manual_path_str: &str) -> HubResult<GameInstallation> {
        let path = Path::new(manual_path_str);
        let validated = GameDetector::validate_game_directory(path);

        if !validated.is_valid {
            return Err(HubError::new(
                "INVALID_GAME_PATH",
                format!("Directory '{}' does not contain valid Astral Party signatures.", manual_path_str),
                "GAME",
            ));
        }

        let inst = GameInstallation {
            id: Uuid::new_v4().to_string(),
            platform: "steam".into(),
            platform_app_id: Some("2622000".into()),
            install_path: validated.install_path,
            executable_path: validated.executable_path,
            detected_build_id: validated.build_id,
            detected_version: validated.version,
            detection_method: "MANUAL".into(),
            is_valid: true,
            is_primary: true,
            last_verified_at: Some(Utc::now()),
            created_at: Utc::now(),
            updated_at: Utc::now(),
        };

        self.game_repo.save(&inst)?;
        Ok(inst)
    }

    pub fn get_primary_game(&self) -> HubResult<Option<GameInstallation>> {
        if let Some(existing) = self.game_repo.get_primary()? {
            return Ok(Some(existing));
        }

        // Single canonical source of truth: auto-detect and persist on demand if not yet recorded
        if let Ok(detected) = self.auto_detect_game() {
            return Ok(Some(detected));
        }

        Ok(None)
    }
}
