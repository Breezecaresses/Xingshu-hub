use std::path::Path;
use crate::domain::error::{HubError, HubResult};
use crate::packages::manifest::HubPackageManifestV1;
use crate::platform::windows::filesystem::get_available_disk_space;
use crate::platform::windows::process::find_running_game_processes;
use crate::security::hash::compute_file_sha256;
use crate::security::path_guard::PathGuard;

pub struct ProcessGate;

impl ProcessGate {
    /// Other installations may remain open; only the selected executable is blocked.
    pub fn check_target(executable: &Path) -> HubResult<()> {
        use sysinfo::{ProcessRefreshKind, RefreshKind, System, UpdateKind};
        let system = System::new_with_specifics(RefreshKind::nothing().with_processes(
            ProcessRefreshKind::nothing().with_exe(UpdateKind::Always),
        ));
        let expected = dunce::canonicalize(executable).map_err(|e| HubError::game_not_found(&e.to_string()))?;
        let normalized = |p: &Path| p.to_string_lossy().replace('\\', "/").to_lowercase();
        let mut running = Vec::new();
        for process in system.processes().values() {
            let matches = match process.exe() {
                Some(path) => dunce::canonicalize(path).map(|p| normalized(&p) == normalized(&expected)).unwrap_or(false),
                None => executable.file_name().is_some_and(|name| name.to_string_lossy().eq_ignore_ascii_case(&process.name().to_string_lossy())),
            };
            if matches { running.push(process.name().to_string_lossy().to_string()); }
        }
        if running.is_empty() { Ok(()) } else { Err(HubError::game_running(&running)) }
    }

    pub fn check(requires_game_closed: bool) -> HubResult<()> {
        if !requires_game_closed {
            return Ok(());
        }

        let running = find_running_game_processes();
        if !running.is_empty() {
            return Err(HubError::game_running(&running));
        }

        Ok(())
    }
}

pub struct CatalogGate;

impl CatalogGate {
    pub fn check(manifest: &HubPackageManifestV1, allowed_logical_roots: &[&str]) -> HubResult<()> {
        for file in &manifest.files {
            let valid = allowed_logical_roots.iter().any(|prefix| file.target.starts_with(prefix));
            if !valid {
                return Err(HubError::new(
                    "CATALOG_GATE_REJECTED",
                    format!("Target file '{}' is not within declared catalog boundaries.", file.target),
                    "SAFETY_GATE",
                ));
            }
        }
        Ok(())
    }
}

pub struct ActiveChildGate;

impl ActiveChildGate {
    pub fn check(_manifest: &HubPackageManifestV1, _target_root: &Path) -> HubResult<()> {
        // Section 37: PHASE 0 defines the gate interface; full XingShu active directory check will activate once protocol confirmed.
        Ok(())
    }
}

pub struct Sha256Gate;

impl Sha256Gate {
    pub fn verify_file<P: AsRef<Path>>(path: P, expected_sha256: &str) -> HubResult<()> {
        let actual = compute_file_sha256(path.as_ref())?;
        if !actual.eq_ignore_ascii_case(expected_sha256) {
            return Err(HubError::new(
                "SHA256_MISMATCH",
                format!(
                    "Checksum validation failed for '{}'. Expected: {}, Actual: {}",
                    path.as_ref().display(),
                    expected_sha256,
                    actual
                ),
                "SAFETY_GATE",
            ));
        }
        Ok(())
    }
}

pub struct DiskSpaceGate;

impl DiskSpaceGate {
    pub fn check<P: AsRef<Path>>(path: P, required_bytes: u64) -> HubResult<()> {
        if let Some(free_space) = get_available_disk_space(path.as_ref()) {
            // Require at least 2x the package size or minimum 50MB
            let threshold = required_bytes.saturating_mul(2).max(50 * 1024 * 1024);
            if free_space < threshold {
                return Err(HubError::new(
                    "INSUFFICIENT_DISK_SPACE",
                    format!(
                        "Insufficient disk space on target drive: {} MB free, required at least {} MB.",
                        free_space / (1024 * 1024),
                        threshold / (1024 * 1024)
                    ),
                    "SAFETY_GATE",
                ));
            }
        }
        Ok(())
    }
}

pub struct PathSafetyGate;

impl PathSafetyGate {
    pub fn check(manifest: &HubPackageManifestV1) -> HubResult<()> {
        for file in &manifest.files {
            PathGuard::sanitize_relative_path(&file.source)?;
            PathGuard::sanitize_relative_path(&file.target)?;
        }
        Ok(())
    }
}
