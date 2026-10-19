use sha2::{Digest, Sha256};
use std::fs::File;
use std::io::{BufReader, Read};
use std::path::Path;
use crate::domain::error::{HubError, HubResult};

pub fn compute_sha256_bytes(bytes: &[u8]) -> String {
    let mut hasher = Sha256::new();
    hasher.update(bytes);
    hex::encode(hasher.finalize())
}

pub fn compute_file_sha256<P: AsRef<Path>>(path: P) -> HubResult<String> {
    let path = path.as_ref();
    let file = File::open(path).map_err(|e| {
        HubError::new("FILE_READ_ERROR", format!("Failed to open file for SHA256 computation: {e}"), "SECURITY")
    })?;
    let mut reader = BufReader::new(file);
    let mut hasher = Sha256::new();
    let mut buffer = [0u8; 64 * 1024];

    loop {
        let count = reader.read(&mut buffer).map_err(|e| {
            HubError::new("FILE_READ_ERROR", format!("Failed to read stream for SHA256 computation: {e}"), "SECURITY")
        })?;
        if count == 0 {
            break;
        }
        hasher.update(&buffer[..count]);
    }

    Ok(hex::encode(hasher.finalize()))
}
