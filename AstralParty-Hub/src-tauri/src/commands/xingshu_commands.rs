use std::path::PathBuf;
use std::sync::Arc;
use tauri::State;

use crate::xing_shu::bridge::protocol::XINGSHU_BRIDGE_LOCALHOST_PORT;
use crate::xing_shu::bridge::server::BridgeServer;
use crate::xing_shu::package_manager::format::XspkgManifest;
use crate::xing_shu::package_manager::service::{PackageVerificationResult, XingShuPackageManager};
use crate::xing_shu::spectator::session::SpectatorSessionInfo;
use crate::xing_shu::spectator::stream::{SpectatorHandPayload, SpectatorMatchSnapshot};
use crate::xing_shu::spectator::service::SpectatorService;
use serde::{Deserialize, Serialize};

#[derive(Debug, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BridgeStatusResponse {
    pub is_running: bool,
    pub named_pipe_connected: bool,
    pub named_pipe: String,
    pub localhost_port: u16,
    pub runtime_connected: bool,
    pub protocol_version: String,
    pub game_pid: Option<u32>,
    pub game_version: Option<String>,
    pub runtime_version: Option<String>,
    pub last_heartbeat_timestamp: Option<i64>,
    pub last_heartbeat_latency_ms: Option<u64>,
    pub connected_at: Option<i64>,
    pub total_messages_received: u64,
    pub total_messages_sent: u64,
    pub recent_messages: Vec<crate::xing_shu::bridge::server::BridgeLogEntry>,
    pub active_transport: crate::xing_shu::bridge::server::ActiveTransport,
    pub tcp_connected: bool,
    pub tcp_addr: Option<String>,
}

#[tauri::command]
pub async fn get_bridge_status(
    bridge_server: State<'_, Arc<BridgeServer>>,
) -> Result<BridgeStatusResponse, String> {
    let s = bridge_server.get_status();
    Ok(BridgeStatusResponse {
        is_running: s.is_running,
        named_pipe_connected: s.pipe_connected,
        named_pipe: s.named_pipe_name,
        localhost_port: XINGSHU_BRIDGE_LOCALHOST_PORT,
        runtime_connected: s.runtime_connected,
        protocol_version: s.protocol_version,
        game_pid: s.game_pid,
        game_version: s.game_version,
        runtime_version: s.runtime_version,
        last_heartbeat_timestamp: s.last_heartbeat_timestamp,
        last_heartbeat_latency_ms: s.last_heartbeat_latency_ms,
        connected_at: s.connected_at,
        total_messages_received: s.total_messages_received,
        total_messages_sent: s.total_messages_sent,
        recent_messages: s.recent_messages,
        active_transport: s.active_transport,
        tcp_connected: s.tcp_connected,
        tcp_addr: s.tcp_addr,
    })
}

#[tauri::command]
pub async fn send_bridge_test_event(
    message: Option<String>,
    bridge_server: State<'_, Arc<BridgeServer>>,
) -> Result<(), String> {
    bridge_server.send_test_event(message)
}

#[tauri::command]
pub async fn send_watch_target(
    connection_id: String,
    target: serde_json::Value,
    expected_spectator_player_id: u64,
    bridge_server: State<'_, Arc<BridgeServer>>,
) -> Result<String, String> {
    bridge_server.send_watch_target(&connection_id, target, expected_spectator_player_id)
}


#[tauri::command]
pub async fn clear_bridge_logs(
    bridge_server: State<'_, Arc<BridgeServer>>,
) -> Result<(), String> {
    bridge_server.clear_logs();
    Ok(())
}

#[tauri::command]
pub async fn get_runtime_sessions(
    bridge_server: State<'_, Arc<BridgeServer>>,
) -> Result<Vec<crate::xing_shu::bridge::session::RuntimeSession>, String> {
    Ok(bridge_server.get_runtime_sessions())
}

#[tauri::command]
pub async fn configure_runtime_bindings(
    bindings: Vec<crate::xing_shu::bridge::session::RuntimeBinding>,
    bridge_server: State<'_, Arc<BridgeServer>>,
) -> Result<(), String> {
    bridge_server.configure_runtime_bindings(bindings)
}

#[tauri::command]
pub async fn get_spectator_session(
    spectator_service: State<'_, Arc<SpectatorService>>,
) -> Result<SpectatorSessionInfo, String> {
    Ok(spectator_service.get_session_info())
}

#[tauri::command]
pub async fn get_spectator_snapshot(
    spectator_service: State<'_, Arc<SpectatorService>>,
) -> Result<Option<SpectatorMatchSnapshot>, String> {
    Ok(spectator_service.get_latest_snapshot())
}

#[tauri::command]
pub async fn get_spectator_hand_payload(
    spectator_service: State<'_, Arc<SpectatorService>>,
) -> Result<Option<SpectatorHandPayload>, String> {
    Ok(spectator_service.get_latest_hand_payload())
}

#[tauri::command]
pub async fn login_spectator(
    account_name: String,
    spectator_service: State<'_, Arc<SpectatorService>>,
) -> Result<SpectatorSessionInfo, String> {
    spectator_service
        .login_spectator(account_name)
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn start_spectating(
    room_id: String,
    spectator_service: State<'_, Arc<SpectatorService>>,
) -> Result<SpectatorSessionInfo, String> {
    spectator_service
        .start_spectating(room_id)
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn stop_spectating(
    spectator_service: State<'_, Arc<SpectatorService>>,
) -> Result<SpectatorSessionInfo, String> {
    spectator_service
        .stop_spectating()
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn inspect_xspkg(
    file_path: String,
    package_mgr: State<'_, Arc<XingShuPackageManager>>,
) -> Result<XspkgManifest, String> {
    package_mgr
        .inspect_package(PathBuf::from(file_path))
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn verify_xspkg(
    file_path: String,
    package_mgr: State<'_, Arc<XingShuPackageManager>>,
) -> Result<PackageVerificationResult, String> {
    package_mgr
        .verify_package(PathBuf::from(file_path))
        .map_err(|e| e.to_string())
}

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct StartupLocale(pub Option<String>);

impl StartupLocale {
    pub fn new(locale: Option<String>) -> Self {
        Self(locale)
    }

    pub fn get(&self) -> Option<&str> {
        self.0.as_deref()
    }
}

/// Parses `--locale <val>` or `--locale=<val>` from command line arguments.
pub fn parse_locale_from_args<I, S>(args: I) -> Option<String>
where
    I: IntoIterator<Item = S>,
    S: AsRef<str>,
{
    let args: Vec<String> = args.into_iter().map(|s| s.as_ref().to_string()).collect();
    let mut startup_locale = None;
    let mut i = 0;
    while i < args.len() {
        if args[i] == "--locale" {
            if i + 1 < args.len() {
                let val = args[i + 1].trim();
                if !val.is_empty() && !val.starts_with("--") {
                    startup_locale = Some(val.to_string());
                    i += 2;
                    continue;
                }
            }
        } else if let Some(val) = args[i].strip_prefix("--locale=") {
            let val = val.trim();
            if !val.is_empty() {
                startup_locale = Some(val.to_string());
            }
        }
        i += 1;
    }
    startup_locale
}

/// Parses `--eval <val>` or `--eval=<val>` from command line arguments.
pub fn parse_eval_from_args<I, S>(args: I) -> Option<String>
where
    I: IntoIterator<Item = S>,
    S: AsRef<str>,
{
    let args: Vec<String> = args.into_iter().map(|s| s.as_ref().to_string()).collect();
    let mut startup_eval = None;
    let mut i = 0;
    while i < args.len() {
        if args[i] == "--eval" {
            if i + 1 < args.len() {
                let val = args[i + 1].trim();
                if !val.is_empty() && !val.starts_with("--") {
                    startup_eval = Some(val.to_string());
                    i += 2;
                    continue;
                }
            }
        } else if let Some(val) = args[i].strip_prefix("--eval=") {
            let val = val.trim();
            if !val.is_empty() {
                startup_eval = Some(val.to_string());
            }
        }
        i += 1;
    }
    startup_eval
}

#[tauri::command]
pub async fn get_startup_locale(
    locale_state: State<'_, StartupLocale>,
) -> Result<Option<String>, String> {
    Ok(locale_state.0.clone())
}

#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct StartupRoute(pub Option<String>);

impl StartupRoute {
    pub fn new(route: Option<String>) -> Self {
        Self(route)
    }

    pub fn get(&self) -> Option<&str> {
        self.0.as_deref()
    }
}

/// Parses `--route <val>`, `--page <val>`, or defaults to `/diagnostics` on `--offline-replay`.
pub fn parse_route_from_args<I, S>(args: I) -> Option<String>
where
    I: IntoIterator<Item = S>,
    S: AsRef<str>,
{
    let args: Vec<String> = args.into_iter().map(|s| s.as_ref().to_string()).collect();
    let mut startup_route = None;
    let mut is_offline_replay = false;
    let mut i = 0;
    while i < args.len() {
        if args[i] == "--route" || args[i] == "--page" {
            if i + 1 < args.len() {
                let val = args[i + 1].trim();
                if !val.is_empty() && !val.starts_with("--") {
                    startup_route = Some(val.to_string());
                    i += 2;
                    continue;
                }
            }
        } else if let Some(val) = args[i].strip_prefix("--route=") {
            let val = val.trim();
            if !val.is_empty() {
                startup_route = Some(val.to_string());
            }
        } else if let Some(val) = args[i].strip_prefix("--page=") {
            let val = val.trim();
            if !val.is_empty() {
                startup_route = Some(val.to_string());
            }
        } else if args[i] == "--offline-replay" {
            is_offline_replay = true;
        }
        i += 1;
    }
    if startup_route.is_none() && is_offline_replay {
        Some("/diagnostics".to_string())
    } else {
        startup_route
    }
}

#[tauri::command]
pub async fn get_startup_route(
    route_state: State<'_, StartupRoute>,
) -> Result<Option<String>, String> {
    Ok(route_state.0.clone())
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct GameProcessStatus {
    pub is_running: bool,
    pub pid: Option<u32>,
    pub process_name: String,
    pub platform_id: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct GameLaunchResult {
    pub success: bool,
    pub uri: String,
    pub already_running: bool,
    pub message: String,
}

/// Validates and resolves the launch URI against the canonical platform profile.
/// Strictly enforces that only the canonical Steam App ID URI (e.g. steam://run/2622000) is accepted.
pub fn validate_and_resolve_launch_uri(
    uri: Option<&str>,
    platform_id: Option<&str>,
) -> Result<(String, String, String), String> {
    use crate::xing_shu::platform::profile::{get_profile, PlatformId};
    use std::str::FromStr;

    let p_id = match platform_id {
        Some(s) if !s.trim().is_empty() => {
            PlatformId::from_str(s).map_err(|e| format!("Invalid platform ID: {e}"))?
        }
        _ => PlatformId::SteamCn,
    };

    let profile = get_profile(p_id)
        .ok_or_else(|| format!("Platform profile for '{}' not found", p_id))?;

    let canonical_app_id = profile.steam_app_id.unwrap_or(2622000);
    let canonical_uri = format!("steam://run/{}", canonical_app_id);
    let exec_name = profile
        .process_executable
        .clone()
        .unwrap_or_else(|| "AstralParty_CN.exe".to_string());

    let target_uri = match uri {
        Some(u) if !u.trim().is_empty() => {
            let trimmed = u.trim();
            if trimmed != canonical_uri && trimmed != "steam://run/2622000" {
                return Err(format!(
                    "Security violation: URI '{trimmed}' rejected by whitelist. Only canonical URI '{canonical_uri}' is permitted."
                ));
            }
            trimmed.to_string()
        }
        _ => canonical_uri,
    };

    Ok((target_uri, exec_name, p_id.to_string()))
}

#[tauri::command]
pub async fn check_game_process_status(
    platform_id: Option<String>,
) -> Result<GameProcessStatus, String> {
    use crate::platform::windows::process::find_game_process;
    use crate::xing_shu::platform::profile::{get_profile, PlatformId};
    use std::str::FromStr;

    let p_id = match platform_id.as_deref() {
        Some(s) if !s.trim().is_empty() => {
            PlatformId::from_str(s).map_err(|e| format!("Invalid platform ID: {e}"))?
        }
        _ => PlatformId::SteamCn,
    };

    let profile = get_profile(p_id)
        .ok_or_else(|| format!("Platform profile for '{}' not found", p_id))?;
    let exec_name = profile
        .process_executable
        .as_deref()
        .unwrap_or("AstralParty_CN.exe");

    if let Some((pid, name)) = find_game_process(exec_name) {
        Ok(GameProcessStatus {
            is_running: true,
            pid: Some(pid),
            process_name: name,
            platform_id: p_id.to_string(),
        })
    } else {
        Ok(GameProcessStatus {
            is_running: false,
            pid: None,
            process_name: exec_name.to_string(),
            platform_id: p_id.to_string(),
        })
    }
}

#[tauri::command]
pub async fn launch_game_uri(
    uri: Option<String>,
    platform_id: Option<String>,
    executable_path: Option<String>,
) -> Result<GameLaunchResult, String> {
    use crate::platform::windows::process::find_game_process;
    use crate::xing_shu::platform::profile::{get_profile, PlatformId};
    use std::str::FromStr;
    let id = PlatformId::from_str(platform_id.as_deref().unwrap_or("STEAM_CN"))?;
    let profile = get_profile(id).ok_or("找不到平台配置")?;
    if profile.steam_app_id.is_none() {
        if uri.as_ref().is_some_and(|u| !u.trim().is_empty()) {
            return Err("非 Steam 平台请配置游戏程序路径".into());
        }
        let name = profile.process_executable.as_deref().ok_or("平台未配置游戏程序名")?;
        let path = executable_path.filter(|p| !p.trim().is_empty()).map(std::path::PathBuf::from)
            .or_else(|| profile.install_root.as_ref().map(|root| std::path::Path::new(root).join(name)))
            .ok_or("请在游戏账号中心配置此平台的游戏程序路径")?;
        if !path.is_absolute() || !path.is_file() || !path.file_name().is_some_and(|n| n.to_string_lossy().eq_ignore_ascii_case(name)) {
            return Err(format!("请选择此平台现有的 {} 完整路径", name));
        }
        let path = path.canonicalize().map_err(|e| e.to_string())?;
        let mut system = sysinfo::System::new_all();
        system.refresh_all();
        if let Some((pid, _)) = system.processes().iter().find(|(_, p)| p.exe().and_then(|p| p.canonicalize().ok()).as_ref() == Some(&path)) {
            return Ok(GameLaunchResult { success: false, uri: path.to_string_lossy().into(), already_running: true,
                message: format!("对应平台的游戏已运行 (PID: {})", pid) });
        }
        std::process::Command::new(&path).current_dir(path.parent().ok_or("游戏路径无父目录")?)
            .spawn().map_err(|e| format!("启动 {} 失败: {}", id, e))?;
        return Ok(GameLaunchResult { success: true, uri: path.to_string_lossy().into(), already_running: false,
            message: format!("已启动 {} 官方客户端", id) });
    }

    let (target_uri, exec_name, _) =
        validate_and_resolve_launch_uri(uri.as_deref(), platform_id.as_deref())?;

    if let Some((pid, name)) = find_game_process(&exec_name) {
        return Ok(GameLaunchResult {
            success: false,
            uri: target_uri,
            already_running: true,
            message: format!("游戏已在运行中 (PID: {}, 进程: {})，禁止重复启动", pid, name),
        });
    }

    #[cfg(target_os = "windows")]
    {
        let status = std::process::Command::new("cmd")
            .args(["/c", "start", "", &target_uri])
            .spawn();

        match status {
            Ok(_) => Ok(GameLaunchResult {
                success: true,
                uri: target_uri,
                already_running: false,
                message: "成功调用系统协议启动 Steam 游戏".to_string(),
            }),
            Err(e) => Err(format!("调用系统启动命令失败: {e}")),
        }
    }

    #[cfg(not(target_os = "windows"))]
    {
        Err("当前非 Windows 平台不支持启动 Steam 游戏".to_string())
    }
}


#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_startup_locale_state_initializes_and_returns_locale() {
        // Test zh-CN initialization and query
        let zh_state = StartupLocale::new(Some("zh-CN".to_string()));
        assert_eq!(zh_state.get(), Some("zh-CN"));
        assert_eq!(zh_state.0, Some("zh-CN".to_string()));

        // Test en-US initialization and query
        let en_state = StartupLocale::new(Some("en-US".to_string()));
        assert_eq!(en_state.get(), Some("en-US"));
        assert_eq!(en_state.0, Some("en-US".to_string()));

        // Test None / default initialization
        let default_state = StartupLocale::default();
        assert_eq!(default_state.get(), None);
        assert_eq!(default_state.0, None);
    }

    #[test]
    fn test_parse_locale_from_args_space_separated() {
        let args = vec!["xingshu-hub.exe", "--locale", "zh-CN"];
        assert_eq!(parse_locale_from_args(args), Some("zh-CN".to_string()));

        let args_en = vec!["xingshu-hub.exe", "--locale", "en-US"];
        assert_eq!(parse_locale_from_args(args_en), Some("en-US".to_string()));
    }

    #[test]
    fn test_parse_locale_from_args_equals_separated() {
        let args = vec!["xingshu-hub.exe", "--locale=zh-CN"];
        assert_eq!(parse_locale_from_args(args), Some("zh-CN".to_string()));

        let args_en = vec!["xingshu-hub.exe", "--locale=en-US"];
        assert_eq!(parse_locale_from_args(args_en), Some("en-US".to_string()));
    }

    #[test]
    fn test_parse_locale_from_args_edge_cases() {
        // No locale flag
        let args_none = vec!["xingshu-hub.exe", "--other-arg", "val"];
        assert_eq!(parse_locale_from_args(args_none), None);

        // Trailing --locale with no value
        let args_trailing = vec!["xingshu-hub.exe", "--locale"];
        assert_eq!(parse_locale_from_args(args_trailing), None);

        // --locale followed by another flag
        let args_flag_after = vec!["xingshu-hub.exe", "--locale", "--port", "8080"];
        assert_eq!(parse_locale_from_args(args_flag_after), None);

        // Empty --locale=
        let args_empty = vec!["xingshu-hub.exe", "--locale="];
        assert_eq!(parse_locale_from_args(args_empty), None);

        // Multiple overrides, latter should take precedence
        let args_multiple = vec!["xingshu-hub.exe", "--locale=zh-CN", "--locale", "en-US"];
        assert_eq!(parse_locale_from_args(args_multiple), Some("en-US".to_string()));
    }

    #[test]
    fn test_validate_and_resolve_launch_uri_canonical_defaults() {
        let (uri, exec, platform) =
            validate_and_resolve_launch_uri(None, Some("STEAM_CN")).expect("STEAM_CN default URI");
        assert_eq!(uri, "steam://run/2622000");
        assert_eq!(exec, "AstralParty_CN.exe");
        assert_eq!(platform, "STEAM_CN");

        let (uri_default, _, _) =
            validate_and_resolve_launch_uri(None, None).expect("Default platform is STEAM_CN");
        assert_eq!(uri_default, "steam://run/2622000");
    }

    #[test]
    fn test_validate_and_resolve_launch_uri_whitelist_enforcement() {
        // Valid exact match passes
        let (uri, _, _) = validate_and_resolve_launch_uri(
            Some("steam://run/2622000"),
            Some("STEAM_CN"),
        )
        .expect("Valid URI passes");
        assert_eq!(uri, "steam://run/2622000");

        // Non-canonical Steam App ID rejected
        let err1 = validate_and_resolve_launch_uri(
            Some("steam://run/123456"),
            Some("STEAM_CN"),
        )
        .unwrap_err();
        assert!(err1.contains("Security violation"));

        // Arbitrary shell command rejected
        let err2 = validate_and_resolve_launch_uri(
            Some("calc.exe"),
            Some("STEAM_CN"),
        )
        .unwrap_err();
        assert!(err2.contains("Security violation"));

        // HTTP URI rejected
        let err3 = validate_and_resolve_launch_uri(
            Some("https://example.com"),
            Some("STEAM_CN"),
        )
        .unwrap_err();
        assert!(err3.contains("Security violation"));
    }

    #[test]
    fn test_game_process_status_serialization() {
        let status = GameProcessStatus {
            is_running: true,
            pid: Some(12345),
            process_name: "AstralParty_CN.exe".to_string(),
            platform_id: "STEAM_CN".to_string(),
        };
        let json = serde_json::to_string(&status).unwrap();
        assert!(json.contains("\"isRunning\":true"));
        assert!(json.contains("\"pid\":12345"));
        assert!(json.contains("\"processName\":\"AstralParty_CN.exe\""));
        assert!(json.contains("\"platformId\":\"STEAM_CN\""));
    }
}

