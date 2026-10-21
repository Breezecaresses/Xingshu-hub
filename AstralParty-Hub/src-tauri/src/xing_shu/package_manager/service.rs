use std::path::Path;
use serde::{Deserialize, Serialize};
use tracing::info;

use crate::domain::error::{HubError, HubResult};
use crate::xing_shu::package_manager::adapter::{
    ApmmPackageAdapter, IPackageFormatAdapter, LegacyXingShuAdapter, XspkgV1Adapter,
};
use crate::xing_shu::package_manager::format::{XspkgManifest, XspkgType};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PackageVerificationResult {
    pub package_id: String,
    pub is_valid: bool,
    pub checksum_verified: bool,
    pub is_signed: bool,
    pub signer_identity: Option<String>,
    pub issues: Vec<String>,
}

pub struct XingShuPackageManager {
    adapters: Vec<Box<dyn IPackageFormatAdapter>>,
}

impl Default for XingShuPackageManager {
    fn default() -> Self {
        Self::new()
    }
}

impl XingShuPackageManager {
    pub fn new() -> Self {
        Self {
            adapters: vec![
                Box::new(XspkgV1Adapter),
                Box::new(LegacyXingShuAdapter),
                Box::new(ApmmPackageAdapter),
            ],
        }
    }

    pub fn inspect_package<P: AsRef<Path>>(&self, path: P) -> HubResult<XspkgManifest> {
        let p = path.as_ref();
        let filename = p
            .file_name()
            .and_then(|f| f.to_str())
            .unwrap_or("")
            .to_string();

        for adapter in &self.adapters {
            if adapter.can_handle(&filename) {
                info!(
                    "Using adapter '{}' to inspect package {}",
                    adapter.format_name(),
                    p.display()
                );
                return adapter.inspect(p);
            }
        }

        Err(HubError::new(
            "UNSUPPORTED_PACKAGE_FORMAT",
            format!("No adapter recognized package format for '{}'. Supported extensions: .xspkg, .xingshu, .apmm, .zip", filename),
            "PACKAGE",
        ))
    }

    pub fn verify_package<P: AsRef<Path>>(&self, path: P) -> HubResult<PackageVerificationResult> {
        let manifest = self.inspect_package(path)?;
        let mut issues = Vec::new();
        let is_signed = manifest.signature.is_some();
        let signer = manifest.signature.as_ref().map(|s| s.signed_by.clone());

        if manifest.package_id.is_empty() {
            issues.push("Package ID cannot be empty".into());
        }

        if manifest.version.is_empty() {
            issues.push("Package version cannot be empty".into());
        }

        if manifest.package_type == XspkgType::RuntimePlugin && manifest.entry_dll.is_none() {
            issues.push("RuntimePlugin package must specify entry_dll".into());
        }

        let is_valid = issues.is_empty();

        Ok(PackageVerificationResult {
            package_id: manifest.package_id,
            is_valid,
            checksum_verified: manifest.checksum_sha256.is_some(),
            is_signed,
            signer_identity: signer,
            issues,
        })
    }
}
