use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize, thiserror::Error)]
#[error("[{code}] {message}")]
pub struct HubError {
    pub code: String,
    pub message: String,
    pub category: String,
    pub recoverable: bool,
    pub user_action: Option<String>,
    pub context: Option<serde_json::Value>,
}

impl HubError {
    pub fn new(code: impl Into<String>, message: impl Into<String>, category: impl Into<String>) -> Self {
        Self {
            code: code.into(),
            message: message.into(),
            category: category.into(),
            recoverable: false,
            user_action: None,
            context: None,
        }
    }

    pub fn with_user_action(mut self, action: impl Into<String>) -> Self {
        self.user_action = Some(action.into());
        self
    }

    pub fn with_recoverable(mut self, recoverable: bool) -> Self {
        self.recoverable = recoverable;
        self
    }

    pub fn with_context(mut self, ctx: serde_json::Value) -> Self {
        self.context = Some(ctx);
        self
    }

    pub fn game_not_found(detail: &str) -> Self {
        Self::new("GAME_NOT_FOUND", format!("Astral Party installation not found: {detail}"), "GAME")
            .with_user_action("Please verify Steam is installed or manually specify the Astral Party directory in Settings.")
            .with_recoverable(true)
    }

    pub fn game_running(process_names: &[String]) -> Self {
        Self::new(
            "INSTALL_GAME_RUNNING",
            format!("Astral Party or associated process is currently running: {}", process_names.join(", ")),
            "SAFETY_GATE",
        )
        .with_user_action("Please exit the game and updater before installing or modifying plugins.")
        .with_recoverable(true)
    }

    pub fn security_violation(detail: impl Into<String>) -> Self {
        Self::new("SECURITY_VIOLATION", detail, "SECURITY")
            .with_user_action("The package has been blocked due to potential security risks.")
            .with_recoverable(false)
    }

    pub fn invalid_manifest(detail: impl Into<String>) -> Self {
        Self::new("INVALID_MANIFEST", detail, "PACKAGE")
            .with_user_action("Please check that the manifest.json conforms to Hub Manifest V1.")
            .with_recoverable(true)
    }

    pub fn storage_error(detail: impl Into<String>) -> Self {
        Self::new("STORAGE_ERROR", detail, "STORAGE")
            .with_recoverable(false)
    }

    pub fn transaction_error(detail: impl Into<String>) -> Self {
        Self::new("TRANSACTION_ERROR", detail, "INSTALL")
            .with_user_action("Review transaction logs or execute recovery rollback.")
            .with_recoverable(true)
    }
}

pub type HubResult<T> = Result<T, HubError>;
