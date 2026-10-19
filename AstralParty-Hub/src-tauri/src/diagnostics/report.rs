use serde::{Deserialize, Serialize};
use crate::diagnostics::collector::SystemDiagnosticSnapshot;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DiagnosticItem {
    pub rule_id: String,
    pub title: String,
    pub severity: String, // "OK", "INFO", "WARNING", "ERROR"
    pub description: String,
    pub recommendation: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DiagnosticReport {
    pub overall_status: String, // "HEALTHY", "WARNING", "DEGRADED"
    pub items: Vec<DiagnosticItem>,
    pub snapshot: SystemDiagnosticSnapshot,
}

pub struct DiagnosticRuleEngine;

impl DiagnosticRuleEngine {
    pub fn evaluate(snapshot: SystemDiagnosticSnapshot) -> DiagnosticReport {
        let mut items = Vec::new();

        // Rule 1: Game installation check
        if let Some(game) = &snapshot.primary_game {
            if game.is_valid {
                items.push(DiagnosticItem {
                    rule_id: "GAME_DETECTED".into(),
                    title: "Astral Party Game Detected".into(),
                    severity: "OK".into(),
                    description: format!(
                        "Installation verified at '{}' (Build ID: {})",
                        game.install_path,
                        game.detected_build_id.as_deref().unwrap_or("N/A")
                    ),
                    recommendation: None,
                });
            } else {
                items.push(DiagnosticItem {
                    rule_id: "GAME_INVALID".into(),
                    title: "Game Path Unverified".into(),
                    severity: "WARNING".into(),
                    description: format!("Directory exists at '{}' but executable or runtime assemblies could not be validated.", game.install_path),
                    recommendation: Some("Verify game files via Steam integrity check or select valid game path in Settings.".into()),
                });
            }
        } else {
            items.push(DiagnosticItem {
                rule_id: "GAME_NOT_FOUND".into(),
                title: "Astral Party Not Found".into(),
                severity: "ERROR".into(),
                description: "Astral Party could not be automatically located in Steam libraries.".into(),
                recommendation: Some("Install Astral Party via Steam or manually set game location in Settings.".into()),
            });
        }

        // Rule 2: Game running check
        if !snapshot.running_processes.is_empty() {
            items.push(DiagnosticItem {
                rule_id: "GAME_RUNNING".into(),
                title: "Astral Party is Currently Running".into(),
                severity: "WARNING".into(),
                description: format!("Active processes detected: {}", snapshot.running_processes.join(", ")),
                recommendation: Some("Exit the game before applying new plugin updates or modifications.".into()),
            });
        } else {
            items.push(DiagnosticItem {
                rule_id: "NO_ACTIVE_LOCKS".into(),
                title: "No Game Process File Locks".into(),
                severity: "OK".into(),
                description: "Game and updaters are closed. Safe for plugin modifications.".into(),
                recommendation: None,
            });
        }

        // Rule 3: APX presence check
        if snapshot.apx_detected {
            items.push(DiagnosticItem {
                rule_id: "APX_DETECTED".into(),
                title: "APX Runtime Environment Active".into(),
                severity: "OK".into(),
                description: format!("APX runtime loader located at {}", snapshot.apx_path.as_deref().unwrap_or("game directory")),
                recommendation: None,
            });
        } else {
            items.push(DiagnosticItem {
                rule_id: "APX_NOT_FOUND".into(),
                title: "APX Runtime Not Detected".into(),
                severity: "INFO".into(),
                description: "APX runtime loader not found in game directory. Standard mods and manual plugins can still be managed.".into(),
                recommendation: Some("Install the APX runtime package to enable advanced in-game plugins.".into()),
            });
        }

        // Rule 4: Incomplete transaction check
        let incomplete_count = snapshot.recent_transactions
            .iter()
            .filter(|tx| !matches!(tx.state.as_str(), "COMPLETED" | "ROLLED_BACK" | "FAILED"))
            .count();

        if incomplete_count > 0 {
            items.push(DiagnosticItem {
                rule_id: "INCOMPLETE_TRANSACTION".into(),
                title: "Previous Installation Incomplete".into(),
                severity: "WARNING".into(),
                description: format!("{incomplete_count} transaction(s) were interrupted before finishing."),
                recommendation: Some("Review Maintenance -> Transactions to inspect or rollback partial installations.".into()),
            });
        }

        let has_error = items.iter().any(|i| i.severity == "ERROR");
        let has_warning = items.iter().any(|i| i.severity == "WARNING");

        let overall_status = if has_error {
            "DEGRADED"
        } else if has_warning {
            "WARNING"
        } else {
            "HEALTHY"
        };

        DiagnosticReport {
            overall_status: overall_status.into(),
            items,
            snapshot,
        }
    }
}
