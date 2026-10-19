use std::path::Path;
use crate::domain::error::{HubError, HubResult};
use crate::packages::manifest::HubPackageManifestV1;
use crate::platform::windows::filesystem::get_available_disk_space;
use crate::platform::windows::process::find_running_game_processes;
use crate::security::hash::compute_file_sha256;
use crate::security::path_guard::PathGuard;

pub struct ProcessGate;

impl ProcessGate {
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
        // Section 37: PHASE 0 defines the gate interface; full APX active directory check will activate once protocol confirmed.
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
