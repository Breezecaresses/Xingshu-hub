use serde_json::Value;

pub struct SecretFilter;

impl SecretFilter {
    const REDACTED_MARKER: &'static str = "[REDACTED]";
    const SENSITIVE_KEYS: &'static [&'static str] = &[
        "authorization",
        "bearer",
        "cookie",
        "session",
        "session_token",
        "access_token",
        "refresh_token",
        "password",
        "secret",
        "token",
    ];

    pub fn scrub_string(input: &str) -> String {
        let mut result = input.to_string();
        for key in Self::SENSITIVE_KEYS {
            let patterns = [
                format!("{key}="),
                format!("{key}:"),
                format!("\"{}\":", key),
                format!("{key} "),
            ];
            for p in &patterns {
                while let Some(pos) = result.to_lowercase().find(p) {
                    let mut start = pos + p.len();
                    // Skip leading whitespace and quotes
                    while start < result.len() && (result.as_bytes()[start] == b' ' || result.as_bytes()[start] == b'"') {
                        start += 1;
                    }
                    // If followed by "bearer ", skip that as well
                    if result[start..].to_lowercase().starts_with("bearer ") {
                        start += 7;
                        while start < result.len() && result.as_bytes()[start] == b' ' {
                            start += 1;
                        }
                    }
                    if start >= result.len() {
                        break;
                    }
                    let end = result[start..]
                        .find(|c: char| c.is_whitespace() || c == '"' || c == ',' || c == '&' || c == ';')
                        .map(|idx| start + idx)
                        .unwrap_or(result.len());
                    if start == end {
                        break;
                    }
                    result.replace_range(start..end, Self::REDACTED_MARKER);
                    break;
                }
            }
        }
        result
    }

    pub fn scrub_json(value: &mut Value) {
        match value {
            Value::Object(map) => {
                for (k, v) in map.iter_mut() {
                    let lower = k.to_lowercase();
                    if Self::SENSITIVE_KEYS.iter().any(|s| lower.contains(s)) {
                        *v = Value::String(Self::REDACTED_MARKER.into());
                    } else {
                        Self::scrub_json(v);
                    }
                }
            }
            Value::Array(arr) => {
                for item in arr.iter_mut() {
                    Self::scrub_json(item);
                }
            }
            Value::String(s) => {
                *s = Self::scrub_string(s);
            }
            _ => {}
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn test_scrubs_token_strings() {
        let input = "Server responded with Authorization: Bearer secret_token_12345; status=200";
        let scrubbed = SecretFilter::scrub_string(input);
        assert!(!scrubbed.contains("secret_token_12345"));
        assert!(scrubbed.contains("[REDACTED]"));
    }

    #[test]
    fn test_scrubs_json_objects() {
        let mut data = json!({
            "username": "astral_player",
            "password": "super_secret_password",
            "nested": {
                "access_token": "token_abc_xyz",
                "game_id": 2622000
            }
        });
        SecretFilter::scrub_json(&mut data);
        assert_eq!(data["password"], "[REDACTED]");
        assert_eq!(data["nested"]["access_token"], "[REDACTED]");
        assert_eq!(data["nested"]["game_id"], 2622000);
    }
}

