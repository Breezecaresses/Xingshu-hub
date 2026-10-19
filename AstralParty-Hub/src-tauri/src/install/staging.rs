use std::fs::{self, File};
use std::io;
use std::path::{Path, PathBuf};
use tempfile::TempDir;
use zip::ZipArchive;
use crate::domain::error::{HubError, HubResult};
use crate::install::gates::Sha256Gate;
use crate::packages::manifest::HubPackageManifestV1;
use crate::security::path_guard::PathGuard;

pub struct StagingManager {
    temp_dir: TempDir,
}

impl StagingManager {
    pub fn new() -> HubResult<Self> {
        let temp_dir = tempfile::Builder::new()
            .prefix("astral_staging_")
            .tempdir()
            .map_err(|e| HubError::new("STAGING_CREATE_FAILED", format!("Failed to create staging directory: {e}"), "INSTALL"))?;

        Ok(Self { temp_dir })
    }

    pub fn staging_path(&self) -> &Path {
        self.temp_dir.path()
    }

    pub fn stage_package<P: AsRef<Path>>(
        &self,
        archive_path: P,
        manifest: &HubPackageManifestV1,
    ) -> HubResult<Vec<(PathBuf, PathBuf)>> {
        let file = File::open(archive_path.as_ref()).map_err(|e| {
            HubError::new("ARCHIVE_OPEN_FAILED", format!("Failed to open archive: {e}"), "INSTALL")
        })?;
        let mut zip = ZipArchive::new(file).map_err(|e| {
            HubError::new("ARCHIVE_READ_FAILED", format!("Failed to read archive: {e}"), "INSTALL")
        })?;

        let mut staged_pairs = Vec::new();

        for entry in &manifest.files {
            let sanitized_source = PathGuard::sanitize_relative_path(&entry.source)?;
            let staged_dest = self.temp_dir.path().join(&sanitized_source);

            if let Some(parent) = staged_dest.parent() {
                fs::create_dir_all(parent).map_err(|e| {
                    HubError::new("STAGING_IO_FAILED", format!("Failed to create staging subdirectories: {e}"), "INSTALL")
                })?;
            }

            let mut zip_file = zip.by_name(&entry.source).map_err(|e| {
                HubError::new("ARCHIVE_FILE_MISSING", format!("File '{}' missing from archive: {e}", entry.source), "INSTALL")
            })?;

            let mut out_file = File::create(&staged_dest).map_err(|e| {
                HubError::new("STAGING_IO_FAILED", format!("Failed to create staged output file: {e}"), "INSTALL")
            })?;

            io::copy(&mut zip_file, &mut out_file).map_err(|e| {
                HubError::new("STAGING_IO_FAILED", format!("Failed to write staged file: {e}"), "INSTALL")
            })?;

            // Gate: Verify extracted file SHA256 matches manifest declaration
            Sha256Gate::verify_file(&staged_dest, &entry.sha256)?;

            let sanitized_target = PathGuard::sanitize_relative_path(&entry.target)?;
            staged_pairs.push((staged_dest, sanitized_target));
        }

        Ok(staged_pairs)
    }
}
