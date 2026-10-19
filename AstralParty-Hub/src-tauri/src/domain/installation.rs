use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct GameInstallation {
    pub id: String,
    pub platform: String,
    pub platform_app_id: Option<String>,
    pub install_path: String,
    pub executable_path: Option<String>,
    pub detected_build_id: Option<String>,
    pub detected_version: Option<String>,
    pub detection_method: String,
    pub is_valid: bool,
    pub is_primary: bool,
    pub last_verified_at: Option<DateTime<Utc>>,
    pub created_at: DateTime<Utc>,
    pub updated_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub enum TransactionState {
    Idle,
    PackageReceived,
    ManifestValidated,
    PlanBuilt,
    ProcessGatePassed,
    CatalogGatePassed,
    ActiveChildGatePassed,
    Sha256GatePassed,
    BackupCreated,
    Staging,
    Staged,
    ReadyToCommit,
    Committing,
    PostVerify,
    Completed,
    RollbackRequired,
    RollingBack,
    RolledBack,
    RollbackFailed,
    Failed,
}

impl ToString for TransactionState {
    fn to_string(&self) -> String {
        match self {
            TransactionState::Idle => "IDLE".into(),
            TransactionState::PackageReceived => "PACKAGE_RECEIVED".into(),
            TransactionState::ManifestValidated => "MANIFEST_VALIDATED".into(),
            TransactionState::PlanBuilt => "PLAN_BUILT".into(),
            TransactionState::ProcessGatePassed => "PROCESS_GATE_PASSED".into(),
            TransactionState::CatalogGatePassed => "CATALOG_GATE_PASSED".into(),
            TransactionState::ActiveChildGatePassed => "ACTIVE_CHILD_GATE_PASSED".into(),
            TransactionState::Sha256GatePassed => "SHA256_GATE_PASSED".into(),
            TransactionState::BackupCreated => "BACKUP_CREATED".into(),
            TransactionState::Staging => "STAGING".into(),
            TransactionState::Staged => "STAGED".into(),
            TransactionState::ReadyToCommit => "READY_TO_COMMIT".into(),
            TransactionState::Committing => "COMMITTING".into(),
            TransactionState::PostVerify => "POST_VERIFY".into(),
            TransactionState::Completed => "COMPLETED".into(),
            TransactionState::RollbackRequired => "ROLLBACK_REQUIRED".into(),
            TransactionState::RollingBack => "ROLLING_BACK".into(),
            TransactionState::RolledBack => "ROLLED_BACK".into(),
            TransactionState::RollbackFailed => "ROLLBACK_FAILED".into(),
            TransactionState::Failed => "FAILED".into(),
        }
    }
}

impl From<&str> for TransactionState {
    fn from(s: &str) -> Self {
        match s {
            "IDLE" => TransactionState::Idle,
            "PACKAGE_RECEIVED" => TransactionState::PackageReceived,
            "MANIFEST_VALIDATED" => TransactionState::ManifestValidated,
            "PLAN_BUILT" => TransactionState::PlanBuilt,
            "PROCESS_GATE_PASSED" => TransactionState::ProcessGatePassed,
            "CATALOG_GATE_PASSED" => TransactionState::CatalogGatePassed,
            "ACTIVE_CHILD_GATE_PASSED" => TransactionState::ActiveChildGatePassed,
            "SHA256_GATE_PASSED" => TransactionState::Sha256GatePassed,
            "BACKUP_CREATED" => TransactionState::BackupCreated,
            "STAGING" => TransactionState::Staging,
            "STAGED" => TransactionState::Staged,
            "READY_TO_COMMIT" => TransactionState::ReadyToCommit,
            "COMMITTING" => TransactionState::Committing,
            "POST_VERIFY" => TransactionState::PostVerify,
            "COMPLETED" => TransactionState::Completed,
            "ROLLBACK_REQUIRED" => TransactionState::RollbackRequired,
            "ROLLING_BACK" => TransactionState::RollingBack,
            "ROLLED_BACK" => TransactionState::RolledBack,
            "ROLLBACK_FAILED" => TransactionState::RollbackFailed,
            _ => TransactionState::Failed,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct InstallTransaction {
    pub id: String,
    pub package_id: String,
    pub package_version: String,
    pub operation: String,
    pub state: String,
    pub game_installation_id: Option<String>,
    pub backup_id: Option<String>,
    pub started_at: DateTime<Utc>,
    pub finished_at: Option<DateTime<Utc>>,
    pub error_code: Option<String>,
    pub error_message: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct InstallTransactionStep {
    pub id: String,
    pub transaction_id: String,
    pub step_index: i32,
    pub step_name: String,
    pub state: String,
    pub started_at: Option<DateTime<Utc>>,
    pub finished_at: Option<DateTime<Utc>>,
    pub detail_json: Option<serde_json::Value>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Backup {
    pub id: String,
    pub backup_type: String,
    pub target_id: Option<String>,
    pub storage_path: String,
    pub manifest_json: serde_json::Value,
    pub created_at: DateTime<Utc>,
    pub verified_at: Option<DateTime<Utc>>,
    pub status: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DiagnosticEvent {
    pub id: String,
    pub severity: String,
    pub category: String,
    pub code: Option<String>,
    pub message: String,
    pub context_json: Option<serde_json::Value>,
    pub created_at: DateTime<Utc>,
}
