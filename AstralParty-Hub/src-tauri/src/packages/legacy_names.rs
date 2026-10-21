//! Only import compatibility uses the old branding. Newly serialized packages use XingShu.
use super::manifest::HubPackageManifestV1;

pub(super) fn normalize(manifest: &mut HubPackageManifestV1) {
    if manifest.package.package_type == "apx-plugin" {
        manifest.package.package_type = "xingshu-plugin".into();
    }
    for file in &mut manifest.files {
        if let Some(relative) = file.target.strip_prefix("apx/plugins/") {
            file.target = format!("xingshu/plugins/{relative}");
        }
    }
}
