pub struct PermissionsGuard;

impl PermissionsGuard {
    pub fn assert_valid_package_type(pkg_type: &str) -> bool {
        matches!(pkg_type, "xingshu-plugin" | "mod" | "resource-pack")
    }

    pub fn assert_valid_logical_prefix(target_str: &str) -> bool {
        target_str.starts_with("xingshu/plugins/")
            || target_str.starts_with("game/mods/")
            || target_str.starts_with("game/resources/")
            || target_str.starts_with("hub/data/")
    }
}
