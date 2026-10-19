use std::path::Path;
use crate::domain::error::{HubError, HubResult};
use crate::packages::manifest::HubPackageManifestV1;
use crate::packages::parser::PackageParser;

pub trait PackageFormatAdapter {
    fn format_name(&self) -> &'static str;
    fn can_handle(&self, zip_path: &Path) -> bool;
    fn extract_manifest(&self, zip_path: &Path) -> HubResult<HubPackageManifestV1>;
}

pub struct HubManifestV1Adapter;

impl PackageFormatAdapter for HubManifestV1Adapter {
    fn format_name(&self) -> &'static str {
        "HubManifestV1"
    }

    fn can_handle(&self, zip_path: &Path) -> bool {
        PackageParser::parse_archive(zip_path).is_ok()
    }

    fn extract_manifest(&self, zip_path: &Path) -> HubResult<HubPackageManifestV1> {
        PackageParser::parse_archive(zip_path)
    }
}

pub struct ApmmPackageAdapter;

impl PackageFormatAdapter for ApmmPackageAdapter {
    fn format_name(&self) -> &'static str {
        "APMMPackage"
    }

    fn can_handle(&self, _zip_path: &Path) -> bool {
        false
    }

    fn extract_manifest(&self, _zip_path: &Path) -> HubResult<HubPackageManifestV1> {
        Err(HubError::new(
            "APMM_ADAPTER_UNSUPPORTED",
            "APMM package adapter is currently awaiting upstream protocol confirmation (Phase 0 spec section 35).",
            "PACKAGE",
        ))
    }
}
