use sysinfo::{ProcessRefreshKind, RefreshKind, System};

pub const ASTRAL_PARTY_PROCESS_NAMES: &[&str] = &[
    "AstralParty_CN.exe",
    "AstralParty_INT.exe",
    "AstralParty.exe",
    "astral-mod-manager.exe",
    "astral-updater.exe",
];

pub fn find_running_game_processes() -> Vec<String> {
    let mut system = System::new_with_specifics(
        RefreshKind::nothing().with_processes(ProcessRefreshKind::nothing()),
    );
    system.refresh_processes(sysinfo::ProcessesToUpdate::All, true);

    let mut found = Vec::new();

    for (_pid, process) in system.processes() {
        let name = process.name().to_string_lossy().to_string();
        for target in ASTRAL_PARTY_PROCESS_NAMES {
            if name.eq_ignore_ascii_case(target) {
                if !found.contains(&name) {
                    found.push(name.clone());
                }
            }
        }
    }

    found
}

/// Finds a specific game process by executable name (e.g. AstralParty_CN.exe or AstralParty_CN).
/// Returns Some((pid, matching_process_name)) if found, or None if not running.
pub fn find_game_process(executable_name: &str) -> Option<(u32, String)> {
    let mut system = System::new_with_specifics(
        RefreshKind::nothing().with_processes(ProcessRefreshKind::nothing()),
    );
    system.refresh_processes(sysinfo::ProcessesToUpdate::All, true);

    let clean_target = executable_name.trim();
    let bare_target = if clean_target.to_lowercase().ends_with(".exe") {
        &clean_target[..clean_target.len() - 4]
    } else {
        clean_target
    };

    for (pid, process) in system.processes() {
        let name = process.name().to_string_lossy().to_string();
        if name.eq_ignore_ascii_case(clean_target) || name.eq_ignore_ascii_case(bare_target) {
            return Some((pid.as_u32(), name));
        }
    }

    None
}

