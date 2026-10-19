use crate::diagnostics::collector::DiagnosticCollector;
use crate::diagnostics::report::{DiagnosticReport, DiagnosticRuleEngine};
use crate::domain::error::HubResult;
use crate::storage::database::Database;
use crate::storage::repositories::{GameRepository, TransactionRepository};

pub struct DiagnosticService {
    game_repo: GameRepository,
    tx_repo: TransactionRepository,
}

impl DiagnosticService {
    pub fn new(db: Database) -> Self {
        Self {
            game_repo: GameRepository::new(db.clone()),
            tx_repo: TransactionRepository::new(db),
        }
    }

    pub fn generate_report(&self) -> HubResult<DiagnosticReport> {
        let snapshot = DiagnosticCollector::collect(&self.game_repo, &self.tx_repo);
        let report = DiagnosticRuleEngine::evaluate(snapshot);
        Ok(report)
    }
}
