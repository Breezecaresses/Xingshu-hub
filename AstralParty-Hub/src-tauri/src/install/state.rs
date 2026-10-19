use crate::domain::installation::TransactionState;

pub struct StateMachine;

impl StateMachine {
    pub fn can_cancel(state: &TransactionState) -> bool {
        matches!(
            state,
            TransactionState::Idle
                | TransactionState::PackageReceived
                | TransactionState::ManifestValidated
                | TransactionState::PlanBuilt
                | TransactionState::ProcessGatePassed
                | TransactionState::CatalogGatePassed
                | TransactionState::ActiveChildGatePassed
                | TransactionState::Sha256GatePassed
                | TransactionState::BackupCreated
                | TransactionState::Staging
                | TransactionState::Staged
                | TransactionState::ReadyToCommit
        )
    }

    pub fn requires_rollback_on_failure(state: &TransactionState) -> bool {
        matches!(
            state,
            TransactionState::Committing | TransactionState::PostVerify
        )
    }
}
