use std::fs::File;
use std::path::Path;
use zip::ZipArchive;
use crate::domain::error::{HubError, HubResult};

pub struct ArchiveLimits {
    pub max_file_count: usize,
    pub max_total_uncompressed_bytes: u64,
    pub max_single_file_uncompressed_bytes: u64,
    pub max_compression_ratio: f64,
}

impl Default for ArchiveLimits {
    fn default() -> Self {
        Self {
            max_file_count: 2000,
            max_total_uncompressed_bytes: 1024 * 1024 * 1024,      // 1 GB
            max_single_file_uncompressed_bytes: 250 * 1024 * 1024, // 250 MB
            max_compression_ratio: 50.0,
        }
    }
}

pub struct ArchiveGuard;

impl ArchiveGuard {
    pub fn inspect_zip<P: AsRef<Path>>(zip_path: P, limits: &ArchiveLimits) -> HubResult<()> {
        let file = File::open(zip_path.as_ref()).map_err(|e| {
            HubError::new("ARCHIVE_OPEN_FAILED", format!("Failed to open package archive: {e}"), "SECURITY")
        })?;

        let mut archive = ZipArchive::new(file).map_err(|e| {
            HubError::new("ARCHIVE_CORRUPT", format!("Failed to parse zip central directory: {e}"), "SECURITY")
        })?;

        let entry_count = archive.len();
        if entry_count > limits.max_file_count {
            return Err(HubError::security_violation(format!(
                "Archive contains {entry_count} files, exceeding safe limit of {}",
                limits.max_file_count
            )));
        }

        let mut total_uncompressed_bytes: u64 = 0;
        let mut total_compressed_bytes: u64 = 0;

        for i in 0..entry_count {
            let entry = archive.by_index_raw(i).map_err(|e| {
                HubError::new("ARCHIVE_CORRUPT", format!("Failed to read archive entry index {i}: {e}"), "SECURITY")
            })?;

            let size = entry.size();
            let compressed_size = entry.compressed_size();

            if size > limits.max_single_file_uncompressed_bytes {
                return Err(HubError::security_violation(format!(
                    "Archive file '{}' exceeds maximum allowed single file size ({} bytes)",
                    entry.name(),
                    size
                )));
            }

            total_uncompressed_bytes = total_uncompressed_bytes.saturating_add(size);
            total_compressed_bytes = total_compressed_bytes.saturating_add(compressed_size);

            if total_uncompressed_bytes > limits.max_total_uncompressed_bytes {
                return Err(HubError::security_violation(format!(
                    "Archive total uncompressed size exceeds maximum allowed limit ({} bytes)",
                    limits.max_total_uncompressed_bytes
                )));
            }
        }

        if total_compressed_bytes > 0 {
            let ratio = (total_uncompressed_bytes as f64) / (total_compressed_bytes as f64);
            if ratio > limits.max_compression_ratio && total_uncompressed_bytes > 10 * 1024 * 1024 {
                return Err(HubError::security_violation(format!(
                    "Potential Zip Bomb detected: compression ratio {ratio:.1}x exceeds maximum allowed threshold of {}x",
                    limits.max_compression_ratio
                )));
            }
        }

        Ok(())
    }
}
