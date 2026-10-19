use std::path::{Component, Path, PathBuf};
use crate::domain::error::{HubError, HubResult};

pub struct PathGuard;

impl PathGuard {
    /// Validates an archive relative path or logical target path against Zip Slip and traversal attempts.
    pub fn sanitize_relative_path(path_str: &str) -> HubResult<PathBuf> {
        let clean_str = path_str.replace('\\', "/");

        // Reject UNC paths
        if clean_str.starts_with("//") || clean_str.starts_with("\\\\") {
            return Err(HubError::security_violation("UNC paths are strictly prohibited in packages."));
        }

        // Reject Windows drive letters
        if clean_str.len() >= 2 && clean_str.chars().nth(1) == Some(':') {
            return Err(HubError::security_violation("Absolute drive paths are strictly prohibited in packages."));
        }

        let path = Path::new(&clean_str);

        // Disallow absolute paths
        if path.is_absolute() {
            return Err(HubError::security_violation("Absolute target paths are strictly prohibited."));
        }

        // Inspect components for directory traversal
        let mut normalized = PathBuf::new();
        for component in path.components() {
            match component {
                Component::Normal(part) => {
                    let s = part.to_string_lossy();
                    if s == ".." || s == "." {
                        return Err(HubError::security_violation("Directory traversal tokens are strictly prohibited."));
                    }
                    normalized.push(part);
                }
                Component::ParentDir | Component::RootDir | Component::Prefix(_) => {
                    return Err(HubError::security_violation("Path escape components are strictly prohibited."));
                }
                Component::CurDir => {}
            }
        }

        if normalized.as_os_str().is_empty() {
            return Err(HubError::security_violation("Empty path after sanitization."));
        }

        Ok(normalized)
    }

    /// Confines a resolved path strictly within a base directory.
    pub fn ensure_within_root(base: &Path, candidate: &Path) -> HubResult<PathBuf> {
        let canonical_base = dunce::canonicalize(base).unwrap_or_else(|_| base.to_path_buf());
        
        let candidate_full = if candidate.is_relative() {
            canonical_base.join(candidate)
        } else {
            candidate.to_path_buf()
        };

        // If the candidate exists, canonicalize it; otherwise normalize
        let canonical_candidate = if candidate_full.exists() {
            dunce::canonicalize(&candidate_full).unwrap_or(candidate_full)
        } else {
            candidate_full
        };

        if !canonical_candidate.starts_with(&canonical_base) {
            return Err(HubError::security_violation(format!(
                "Path boundary escape detected: '{}' is outside base '{}'",
                canonical_candidate.display(),
                canonical_base.display()
            )));
        }

        Ok(canonical_candidate)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_rejects_zip_slip() {
        assert!(PathGuard::sanitize_relative_path("../escape.dll").is_err());
        assert!(PathGuard::sanitize_relative_path("foo/../../bar.dll").is_err());
        assert!(PathGuard::sanitize_relative_path(r"..\windows\system32").is_err());
    }

    #[test]
    fn test_rejects_absolute_and_unc() {
        assert!(PathGuard::sanitize_relative_path("C:\\Windows\\notepad.exe").is_err());
        assert!(PathGuard::sanitize_relative_path("/etc/passwd").is_err());
        assert!(PathGuard::sanitize_relative_path("\\\\server\\share\\mod.dll").is_err());
    }

    #[test]
    fn test_accepts_clean_relative() {
        let res = PathGuard::sanitize_relative_path("apx/plugins/my_plugin.dll");
        assert!(res.is_ok());
        let res = PathGuard::sanitize_relative_path("game/mods/texture.png");
        assert!(res.is_ok());
    }
}
