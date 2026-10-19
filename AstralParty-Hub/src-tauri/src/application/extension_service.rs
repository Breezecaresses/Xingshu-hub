use std::path::Path;
use crate::domain::error::HubResult;
use crate::domain::mod_package::ModPackage;
use crate::domain::plugin::Plugin;
use crate::packages::manifest::HubPackageManifestV1;
use crate::packages::parser::PackageParser;
use crate::storage::database::Database;
use crate::storage::repositories::ExtensionRepository;

pub struct ExtensionService {
    extension_repo: ExtensionRepository,
}

impl ExtensionService {
    pub fn new(db: Database) -> Self {
        Self {
            extension_repo: ExtensionRepository::new(db),
        }
    }

    pub fn list_plugins(&self) -> HubResult<Vec<Plugin>> {
        self.extension_repo.list_plugins()
    }

    pub fn list_mods(&self) -> HubResult<Vec<ModPackage>> {
        self.extension_repo.list_mods()
    }

    pub fn inspect_package_archive<P: AsRef<Path>>(&self, zip_path: P) -> HubResult<HubPackageManifestV1> {
        PackageParser::parse_archive(zip_path)
    }

    pub fn uninstall_extension(&self, extension_id: &str, is_plugin: bool) -> HubResult<()> {
        let files = self.extension_repo.get_installed_files_by_package(extension_id)?;
        for file in files {
            let p = Path::new(&file.target_path);
            if p.exists() {
                std::fs::remove_file(p).ok();
            }
        }
        self.extension_repo.delete_installed_files_by_package(extension_id)?;
        if is_plugin {
            self.extension_repo.delete_plugin(extension_id)?;
        }
        Ok(())
    }
}
