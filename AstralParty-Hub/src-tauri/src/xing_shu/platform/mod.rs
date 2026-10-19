pub mod profile;

pub use profile::{
    get_default_profiles, get_profile, load_profiles_from_json,
    verify_hs4_a_baseline_governance, CompatibilityStatus, PlatformId, PlatformProfile,
    EMBEDDED_PROFILES_JSON,
};
