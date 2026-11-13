//! QR device login: the Hub receives its own opaque session, never a game SDK credential.
use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine};
use crate::domain::error::{HubError, HubResult};
use serde_json::{json, Value};
use sha2::{Digest, Sha256};
use std::{collections::HashMap, path::{Path, PathBuf}, sync::Mutex as SyncMutex, time::{Duration, Instant}};
use tauri::{Manager, State};
use tokio::sync::Mutex;

#[path = "cloud_hand_sync.rs"]
pub(crate) mod cloud_hand_sync;
use cloud_hand_sync::CloudHandSync;

const ORIGIN: &str = "https://124.221.176.134";
const MAX_RESPONSE: usize = 512 * 1024;
const QQ_SESSION_TTL: Duration = Duration::from_secs(540);

#[derive(Default)]
pub struct AccountLogin {
    gate: Mutex<()>,
    pending: Mutex<Option<Pending>>,
    qq_sessions: Mutex<HashMap<String, ClaimedQqSession>>,
    hand_sync: SyncMutex<CloudHandSync>,
}

struct Pending { request_id: String, verifier: String, started: Instant }
#[derive(Clone)]
struct ClaimedQqSession { cookie_kv: String, csrf: String, claimed_at: Instant }
fn error(text: &str) -> HubError { HubError::new("ACCOUNT_LOGIN", text, "HUB").with_recoverable(true) }
fn pkce(verifier: &str) -> String { URL_SAFE_NO_PAD.encode(Sha256::digest(verifier.as_bytes())) }
fn qq_link_key(request_id: &str, page_token: &str) -> String {
    URL_SAFE_NO_PAD.encode(Sha256::digest(format!("{request_id}.{page_token}").as_bytes()))
}
fn should_drop_qq_session(status: reqwest::StatusCode, msg: &str) -> bool {
    matches!(status, reqwest::StatusCode::FORBIDDEN | reqwest::StatusCode::UNAUTHORIZED | reqwest::StatusCode::NOT_FOUND)
        || msg.contains("过期")
        || msg.contains("已结束")
        || msg.contains("已使用")
}

async fn request(path: &str, body: Option<Value>, token: Option<&str>) -> HubResult<Value> {
    // Destination is fixed in native code; frontend input cannot redirect credentials elsewhere.
    let client = reqwest::Client::builder().timeout(Duration::from_secs(18))
        .connect_timeout(Duration::from_secs(8)).redirect(reqwest::redirect::Policy::none())
        .build().map_err(|_| error("无法初始化安全登录连接。"))?;
    let mut call = if body.is_some() { client.post(format!("{ORIGIN}{path}")) }
        else { client.get(format!("{ORIGIN}{path}")) };
    if let Some(data) = body { call = call.json(&data); }
    if let Some(token) = token { call = call.bearer_auth(token); }
    let mut response = call.send().await.map_err(|_| error("登录服务连接未完成，请检查网络后重试。"))?;
    if response.status() == reqwest::StatusCode::UNAUTHORIZED {
        return Err(HubError::new("ACCOUNT_LOGIN_EXPIRED", "登录已过期，请重新登录。", "HUB").with_recoverable(true));
    }
    let ok = response.status().is_success();
    let mut bytes = Vec::new();
    while let Some(chunk) = response.chunk().await.map_err(|_| error("登录响应读取失败。"))? {
        if bytes.len() + chunk.len() > MAX_RESPONSE { return Err(error("登录响应无效。")); }
        bytes.extend_from_slice(&chunk);
    }
    let parsed: Value = serde_json::from_slice(&bytes).map_err(|_| error(if ok { "登录服务返回了无效响应。" } else { "登录请求未完成，请稍后重试或重新发起登录。" }))?;
    if !ok {
        let msg = parsed["error"].as_str().map(str::trim).filter(|s| !s.is_empty() && s.chars().count() <= 160)
            .unwrap_or("登录请求未完成，请稍后重试或重新发起登录。");
        return Err(error(msg));
    }
    Ok(parsed)
}
fn data_root(app: &tauri::AppHandle) -> HubResult<PathBuf> {
    let path = app.path().app_data_dir().map_err(|_| error("无法读取 Hub 数据目录。"))?.join("account-login");
    std::fs::create_dir_all(&path).map_err(|_| error("无法准备登录状态目录。"))?;
    Ok(path)
}
fn identity(root: &Path) -> HubResult<String> {
    let file = root.join("device-id.txt");
    if let Ok(value) = std::fs::read_to_string(&file) {
        if uuid::Uuid::parse_str(&value).is_ok() { return Ok(value); }
    }
    let value = uuid::Uuid::new_v4().to_string();
    std::fs::write(file, &value).map_err(|_| error("无法保存 Hub 设备标识。"))?;
    Ok(value)
}

#[cfg(windows)]
fn protect(input: &[u8], decrypt: bool) -> HubResult<Vec<u8>> {
    #[repr(C)] struct Blob { size: u32, data: *mut u8 }
    #[link(name = "Crypt32")]
    unsafe extern "system" {
        fn CryptProtectData(input: *const Blob, description: *const u16, entropy: *const Blob,
            reserved: *mut std::ffi::c_void, prompt: *mut std::ffi::c_void, flags: u32, output: *mut Blob) -> i32;
        fn CryptUnprotectData(input: *const Blob, description: *mut *mut u16, entropy: *const Blob,
            reserved: *mut std::ffi::c_void, prompt: *mut std::ffi::c_void, flags: u32, output: *mut Blob) -> i32;
    }
    #[link(name = "Kernel32")]
    unsafe extern "system" { fn LocalFree(value: *mut std::ffi::c_void) -> *mut std::ffi::c_void; }
    let source = Blob { size: input.len() as u32, data: input.as_ptr() as *mut u8 };
    let mut output = Blob { size: 0, data: std::ptr::null_mut() };
    // CRYPTPROTECT_UI_FORBIDDEN, current Windows user; do not use machine-wide encryption.
    let ok = unsafe {
        if decrypt { CryptUnprotectData(&source, std::ptr::null_mut(), std::ptr::null(), std::ptr::null_mut(), std::ptr::null_mut(), 1, &mut output) }
        else { CryptProtectData(&source, std::ptr::null(), std::ptr::null(), std::ptr::null_mut(), std::ptr::null_mut(), 1, &mut output) }
    };
    if ok == 0 { return Err(error("Windows 无法保护此登录状态，请重新登录。")); }
    let result = unsafe { std::slice::from_raw_parts(output.data, output.size as usize).to_vec() };
    unsafe { LocalFree(output.data.cast()); }
    Ok(result)
}
#[cfg(not(windows))]
fn protect(_input: &[u8], _decrypt: bool) -> HubResult<Vec<u8>> { Err(error("此 Hub 登录版本需要 Windows。")) }

fn save_token(root: &Path, token: &str) -> HubResult<()> {
    if !(32..=200).contains(&token.len()) { return Err(error("登录凭据无效。")); }
    let encrypted = protect(token.as_bytes(), false)?;
    let temporary = root.join(format!("session.{}.tmp", uuid::Uuid::new_v4()));
    std::fs::write(&temporary, encrypted).map_err(|_| error("无法保存受保护的登录状态。"))?;
    let destination = root.join("session.dat");
    // Replace only this login's DPAPI file, never game files or other Hub account data.
    if destination.exists() { std::fs::remove_file(&destination).map_err(|_| error("无法更新登录状态。"))?; }
    std::fs::rename(temporary, destination).map_err(|_| error("无法完成登录状态保存。"))
}
fn token(root: &Path) -> HubResult<Option<String>> {
    let file = root.join("session.dat");
    if !file.exists() { return Ok(None); }
    let bytes = std::fs::read(file).map_err(|_| error("无法读取登录状态。"))?;
    if bytes.len() > 4096 { return Err(error("登录状态无效，请退出后重新登录。")); }
    let value = String::from_utf8(protect(&bytes, true)?).map_err(|_| error("登录状态无效。"))?;
    Ok(Some(value))
}

#[tauri::command]
pub async fn get_cloud_account(app: tauri::AppHandle) -> HubResult<Value> {
    let root = data_root(&app)?;
    let Some(token) = token(&root)? else { return Ok(json!({"status":"signed_out"})); };
    let result = request("/auth/me", None, Some(&token)).await?;
    Ok(json!({"status":"signed_in","account":result["account"]}))
}
#[tauri::command]
pub async fn start_cloud_login(app: tauri::AppHandle, state: State<'_, AccountLogin>) -> HubResult<Value> {
    let _gate = state.gate.try_lock().map_err(|_| error("登录请求正在处理。"))?;
    let root = data_root(&app)?;
    let verifier = format!("{}{}", uuid::Uuid::new_v4().simple(), uuid::Uuid::new_v4().simple());
    let result = request("/auth/device", Some(json!({"device_id":identity(&root)?, "code_challenge":pkce(&verifier)})), None).await?;
    let request_id = result["request_id"].as_str().ok_or_else(|| error("未生成有效的登录二维码。"))?.to_string();
    let url = result["login_url"].as_str().unwrap_or("");
    if !url.starts_with(&format!("{ORIGIN}/login#")) || !result["qr_png"].is_string() { return Err(error("登录二维码来源无效。")); }
    *state.pending.lock().await = Some(Pending { request_id, verifier, started: Instant::now() });
    Ok(json!({"loginUrl":url,"qrPng":result["qr_png"],"expiresIn":result["expires_in"]}))
}
#[tauri::command]
pub async fn poll_cloud_login(app: tauri::AppHandle, state: State<'_, AccountLogin>) -> HubResult<Value> {
    let _gate = state.gate.try_lock().map_err(|_| error("登录请求正在处理。"))?;
    let mut pending = state.pending.lock().await;
    let Some(flow) = pending.as_ref() else { return Ok(json!({"status":"expired"})); };
    if flow.started.elapsed() > Duration::from_secs(600) { *pending = None; return Ok(json!({"status":"expired"})); }
    let result = request("/auth/device/poll", Some(json!({"request_id":flow.request_id,"code_verifier":flow.verifier})), None).await?;
    if result["status"] == "succeeded" {
        let token = result["access_token"].as_str().ok_or_else(|| error("登录未能建立会话。"))?;
        let root = data_root(&app)?;
        save_token(&root, token)?;
        if let Err(err) = state.hand_sync.lock().unwrap().restore(app, &root, Some(token.to_owned())) {
            tracing::warn!(code = %err.code, "Unable to restore cloud hands after login");
        }
        *pending = None;
        return Ok(json!({"status":"succeeded","account":result["account"]}));
    }
    if result["status"] == "expired" || result["status"] == "superseded" || result["status"] == "failed" { *pending = None; }
    if let Some(msg) = result["message"].as_str().map(str::trim).filter(|s| !s.is_empty() && s.chars().count() <= 160) {
        return Ok(json!({"status":result["status"],"message":msg}));
    }
    Ok(json!({"status":result["status"]}))
}
#[tauri::command]
pub async fn logout_cloud_account(app: tauri::AppHandle, state: State<'_, AccountLogin>) -> HubResult<()> {
    let _gate = state.gate.try_lock().map_err(|_| error("登录请求正在处理。"))?;
    state.hand_sync.lock().unwrap().stop();
    *state.pending.lock().await = None;
    let root = data_root(&app)?;
    if let Some(token) = token(&root)? { request("/auth/logout", Some(json!({})), Some(&token)).await?; }
    let file = root.join("session.dat");
    if file.exists() { std::fs::remove_file(file).map_err(|_| error("无法清除本机登录状态。"))?; }
    Ok(())
}

#[tauri::command]
pub async fn get_cloud_game_library(app: tauri::AppHandle) -> HubResult<Value> {
    let Some(token) = token(&data_root(&app)?)? else { return Ok(json!({"account":null,"identity":null})); };
    request("/account/data", None, Some(&token)).await
}

#[tauri::command]
pub async fn sync_cloud_game_library(app: tauri::AppHandle) -> HubResult<Value> {
    let token = token(&data_root(&app)?)?.ok_or_else(|| error("请先在游戏账号页登录。"))?;
    request("/account/data/sync", Some(json!({"confirm_session_switch":true})), Some(&token)).await
}

#[tauri::command]
pub async fn get_cloud_match_detail(app: tauri::AppHandle, time: i64, hero_id: i32, map_id: i32, read_replay: Option<bool>) -> HubResult<Value> {
    if time <= 0 || hero_id <= 0 { return Err(error("战绩标识无效。")); }
    let token = token(&data_root(&app)?)?.ok_or_else(|| error("请先在游戏账号页登录。"))?;
    request("/account/data/match", Some(json!({"time":time,"heroId":hero_id,"mapId":map_id,"read_replay":read_replay.unwrap_or(false)})), Some(&token)).await
}

struct ChannelSpec {
    platform: &'static str,
    app_id: &'static str,
    label: &'static str,
}

const CHANNELS: &[ChannelSpec] = &[
    ChannelSpec { platform: "TAPTAP", app_id: "110001958", label: "TapTap" },
    ChannelSpec { platform: "BILIBILI", app_id: "110001957", label: "哔哩哔哩 (B站)" },
    ChannelSpec { platform: "WEGAME", app_id: "110001949", label: "WeGame" },
    ChannelSpec { platform: "STEAM_CN", app_id: "110001933", label: "Steam 国服 (110001933)" },
    ChannelSpec { platform: "STEAM_CN", app_id: "110001959", label: "Steam 国服 (110001959)" },
];

fn validate_channel(platform: &str, app_id: &str) -> HubResult<&'static ChannelSpec> {
    CHANNELS
        .iter()
        .find(|spec| spec.platform == platform && spec.app_id == app_id)
        .ok_or_else(|| error("不支持的官方客户端渠道。"))
}

fn is_token_byte(b: u8) -> bool {
    b.is_ascii_alphanumeric() || matches!(b, b'_' | b'.' | b'=' | b'-')
}

/// Extract the latest valid non-`_sh` BnSdk `accessToken` from a single log string.
pub(crate) fn extract_access_token_from_log_text(raw: &str) -> Option<String> {
    let bytes = raw.as_bytes();
    let lower = raw.to_ascii_lowercase();
    let lbytes = lower.as_bytes();
    let mut best: Option<String> = None;

    for needle in ["accesstoken", "access_token"] {
        let mut pos = 0;
        while let Some(rel) = lower[pos..].find(needle) {
            let mut idx = pos + rel + needle.len();
            pos = idx;
            while idx < lbytes.len() && (lbytes[idx].is_ascii_whitespace() || lbytes[idx] == b'\\' || lbytes[idx] == b'"') {
                idx += 1;
            }
            if idx >= lbytes.len() || lbytes[idx] != b':' {
                continue;
            }
            idx += 1;
            while idx < lbytes.len() && (lbytes[idx].is_ascii_whitespace() || lbytes[idx] == b'\\') {
                idx += 1;
            }
            if idx >= lbytes.len() || lbytes[idx] != b'"' {
                continue;
            }
            idx += 1;
            let start = idx;
            while idx < bytes.len() && is_token_byte(bytes[idx]) {
                idx += 1;
            }
            let len = idx - start;
            if (40..=4096).contains(&len) {
                let candidate = &raw[start..idx];
                if !candidate.ends_with("_sh") {
                    best = Some(candidate.to_string());
                }
            }
        }
    }
    best
}

fn local_bnsdk_token(app_id: &str) -> Option<(String, u64)> {
    let home = dirs::home_dir()?;
    let feimo_dir = home.join("AppData").join("LocalLow").join("feimo");
    let entries = std::fs::read_dir(&feimo_dir).ok()?;
    let mut log_files: Vec<(std::time::SystemTime, PathBuf)> = Vec::new();
    for entry in entries.flatten() {
        let log_dir = entry.path().join("BnSdk").join(app_id).join("Log");
        let Ok(logs) = std::fs::read_dir(&log_dir) else { continue };
        for log in logs.flatten() {
            let path = log.path();
            if path.extension().and_then(|s| s.to_str()).map(|s| s.eq_ignore_ascii_case("log")).unwrap_or(false) {
                if let Ok(meta) = log.metadata() {
                    if meta.is_file() && meta.len() <= 16 * 1024 * 1024 {
                        let mtime = meta.modified().unwrap_or(std::time::SystemTime::UNIX_EPOCH);
                        log_files.push((mtime, path));
                    }
                }
            }
        }
    }
    log_files.sort_by(|a, b| b.0.cmp(&a.0));
    for (mtime, path) in log_files.into_iter().take(16) {
        if let Ok(bytes) = std::fs::read(&path) {
            let text = String::from_utf8_lossy(&bytes);
            if let Some(tok) = extract_access_token_from_log_text(&text) {
                let secs = mtime.duration_since(std::time::SystemTime::UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0);
                return Some((tok, secs));
            }
        }
    }
    None
}

fn parse_qq_login_link(raw_url: &str) -> HubResult<(String, String)> {
    let trimmed = raw_url.trim();
    let prefix = format!("{ORIGIN}/login#");
    let Some(fragment) = trimmed.strip_prefix(&prefix) else {
        return Err(error("仅支持星枢官方授权链接（请复制 Bot 发出的完整登录链接）。"));
    };
    let Some((request_id, page_token)) = fragment.split_once('.') else {
        return Err(error("授权链接格式无效，缺少授权参数。"));
    };
    if request_id.len() < 8 || request_id.len() > 128 || page_token.len() < 16 || page_token.len() > 256
        || !request_id.bytes().all(is_token_byte) || !page_token.bytes().all(is_token_byte) {
        return Err(error("授权链接参数格式无效。"));
    }
    Ok((request_id.to_string(), page_token.to_string()))
}

fn sort_detected_channels(items: &mut Vec<(u64, Value)>) -> Vec<Value> {
    items.sort_by(|a, b| b.0.cmp(&a.0));
    items.drain(..).map(|(_, v)| v).collect()
}

#[tauri::command]
pub async fn detect_local_channel_tokens() -> HubResult<Value> {
    // Only returns safe channel metadata, sorted newest first; never returns raw BnSdk accessToken to the UI.
    let detected: Vec<Value> = tokio::task::spawn_blocking(|| {
        let mut items: Vec<(u64, Value)> = CHANNELS
            .iter()
            .filter_map(|spec| {
                local_bnsdk_token(spec.app_id).map(|(_, updated_at)| {
                    (
                        updated_at,
                        json!({
                            "platform": spec.platform,
                            "appId": spec.app_id,
                            "label": spec.label,
                            "updatedAt": updated_at
                        }),
                    )
                })
            })
            .collect();
        sort_detected_channels(&mut items)
    })
    .await
    .map_err(|_| error("检测本机渠道日志失败。"))?;
    Ok(json!({ "channels": detected }))
}

#[tauri::command]
pub async fn start_channel_cloud_login(
    app: tauri::AppHandle,
    state: State<'_, AccountLogin>,
    platform: String,
    app_id: String,
) -> HubResult<Value> {
    let _gate = state.gate.try_lock().map_err(|_| error("登录请求正在处理。"))?;
    let spec = validate_channel(&platform, &app_id)?;
    let app_id_owned = spec.app_id.to_string();
    let (sdk_token, _) = tokio::task::spawn_blocking(move || local_bnsdk_token(&app_id_owned))
        .await
        .map_err(|_| error("读取本机渠道票据失败。"))?
        .ok_or_else(|| error("未找到该渠道有效的本机 BnSdk 登录票据，请先启动该渠道服并进入大厅后再试。"))?;

    let root = data_root(&app)?;
    let verifier = format!("{}{}", uuid::Uuid::new_v4().simple(), uuid::Uuid::new_v4().simple());
    let result = request(
        "/auth/channel/device",
        Some(json!({
            "device_id": identity(&root)?,
            "code_challenge": pkce(&verifier),
            "platform": spec.platform,
            "app_id": spec.app_id,
            "game_sdk_token": sdk_token
        })),
        None,
    )
    .await?;
    let request_id = result["request_id"].as_str().ok_or_else(|| error("未生成渠道授权请求。"))?.to_string();
    *state.pending.lock().await = Some(Pending { request_id, verifier, started: Instant::now() });
    Ok(json!({ "status": "pending", "expiresIn": result["expires_in"] }))
}

#[tauri::command]
pub async fn complete_qq_channel_login(
    state: State<'_, AccountLogin>,
    login_url: String,
    platform: String,
    app_id: String,
) -> HubResult<Value> {
    let spec = validate_channel(&platform, &app_id)?;
    let (request_id, page_token) = parse_qq_login_link(&login_url)?;
    let app_id_owned = spec.app_id.to_string();
    let (sdk_token, _) = tokio::task::spawn_blocking(move || local_bnsdk_token(&app_id_owned))
        .await
        .map_err(|_| error("读取本机渠道票据失败。"))?
        .ok_or_else(|| error("未找到该渠道有效的本机 BnSdk 登录票据，请先启动该渠道服并进入大厅后再试。"))?;

    let client = reqwest::Client::builder()
        .timeout(Duration::from_secs(25))
        .connect_timeout(Duration::from_secs(8))
        .redirect(reqwest::redirect::Policy::none())
        .build()
        .map_err(|_| error("无法初始化安全登录连接。"))?;

    let link_key = qq_link_key(&request_id, &page_token);
    let cached_session = {
        let mut sessions = state.qq_sessions.lock().await;
        sessions.retain(|_, s| s.claimed_at.elapsed() < QQ_SESSION_TTL);
        sessions.get(&link_key).cloned()
    };

    let (cookie_kv, csrf) = if let Some(cached) = cached_session {
        (cached.cookie_kv, cached.csrf)
    } else {
        let claim_res = client
            .post(format!("{ORIGIN}/auth/page/claim"))
            .header("Origin", ORIGIN)
            .json(&json!({ "request_id": request_id, "page_token": page_token }))
            .send()
            .await
            .map_err(|_| error("连接星枢授权服务失败，请检查网络。"))?;
        let cookie_pair = claim_res
            .headers()
            .get_all(reqwest::header::SET_COOKIE)
            .iter()
            .filter_map(|v| v.to_str().ok())
            .find_map(|raw| raw.split(';').next().map(str::trim).filter(|kv| kv.starts_with("xingshu_auth=")).map(str::to_string));
        let claim_ok = claim_res.status().is_success();
        let claim_json: Value = claim_res.json().await.map_err(|_| error("授权页响应无效。"))?;
        if !claim_ok {
            let msg = claim_json["error"].as_str().filter(|s| s.chars().count() <= 160).unwrap_or("授权链接已失效或已过期。");
            return Err(error(msg));
        }
        let Some(cookie_kv) = cookie_pair else {
            return Err(error("未能建立授权页会话 Cookie。"));
        };
        let Some(csrf) = claim_json["csrf"].as_str().filter(|s| !s.is_empty()).map(str::to_string) else {
            return Err(error("授权页缺少 CSRF 令牌。"));
        };
        // Keep the claimed page session in RAM so switching ports or retrying after exiting the game
        // does not hit the single-use /auth/page/claim endpoint a second time.
        let mut sessions = state.qq_sessions.lock().await;
        if sessions.len() >= 16 {
            sessions.clear();
        }
        sessions.insert(link_key.clone(), ClaimedQqSession {
            cookie_kv: cookie_kv.clone(),
            csrf: csrf.clone(),
            claimed_at: Instant::now(),
        });
        (cookie_kv, csrf)
    };

    let ch_res = client
        .post(format!("{ORIGIN}/auth/page/channel"))
        .header("Origin", ORIGIN)
        .header("X-XingShu-CSRF", &csrf)
        .header(reqwest::header::COOKIE, &cookie_kv)
        .json(&json!({ "platform": spec.platform, "app_id": spec.app_id, "sdk_token": sdk_token }))
        .send()
        .await
        .map_err(|_| error("提交渠道服授权请求失败。"))?;
    let ch_status = ch_res.status();
    let ch_ok = ch_status.is_success();
    let ch_json: Value = ch_res.json().await.map_err(|_| error("渠道登录响应无效。"))?;
    if !ch_ok {
        let msg = ch_json["error"].as_str().filter(|s| s.chars().count() <= 160).unwrap_or("渠道服授权未成功，请确认游戏内已登录并重试。");
        if should_drop_qq_session(ch_status, msg) {
            state.qq_sessions.lock().await.remove(&link_key);
        }
        return Err(error(msg));
    }
    state.qq_sessions.lock().await.remove(&link_key);
    Ok(json!({ "status": "succeeded", "account": ch_json["account"] }))
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn pkce_uses_s256_known_vector() {
        assert_eq!(pkce("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"), "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
    }
    #[cfg(windows)]
    #[test]
    fn dpapi_session_roundtrip_and_corruption_rejected() {
        let encrypted = protect(b"test-opaque-hub-session-never-game-token", false).unwrap();
        assert!(!encrypted.windows(12).any(|s| s == b"test-opaque-"));
        assert_eq!(protect(&encrypted, true).unwrap(), b"test-opaque-hub-session-never-game-token");
        let mut corrupted = encrypted; let n = corrupted.len(); corrupted[n-1] ^= 0xff;
        assert!(protect(&corrupted, true).is_err());
    }
    #[test]
    fn extracts_bnsdk_token_and_rejects_sh_or_short() {
        let valid = "eyJhbGciOiJIUzI1NiJ9.abcdef0123456789_ABCDEF-0123456789";
        let log = format!(
            r#"{{"accessToken":"{}_sh"}} {{"accessToken":"too_short"}} {{\"access_token\":\"{}\"}}"#,
            valid, valid
        );
        assert_eq!(extract_access_token_from_log_text(&log).as_deref(), Some(valid));
        let only_sh = format!(r#"{{"accessToken":"{}_sh"}}"#, valid);
        assert_eq!(extract_access_token_from_log_text(&only_sh), None);
    }
    #[test]
    fn qq_login_link_validates_fixed_origin_and_fragment() {
        assert_eq!(
            parse_qq_login_link("https://124.221.176.134/login#req12345678.tok_0123456789abcdef").unwrap(),
            ("req12345678".to_string(), "tok_0123456789abcdef".to_string())
        );
        assert!(parse_qq_login_link("https://evil.example/login#req12345678.tok_0123456789abcdef").is_err());
        assert!(parse_qq_login_link("https://124.221.176.134/login#short.t").is_err());
    }
    #[test]
    fn detected_channels_sort_newest_first_and_session_drop_predicate() {
        let mut items = vec![
            (100, json!({"appId": "110001958"})),
            (500, json!({"appId": "110001933"})),
            (250, json!({"appId": "110001957"})),
        ];
        let sorted = sort_detected_channels(&mut items);
        assert_eq!(sorted[0]["appId"], "110001933");
        assert_eq!(sorted[1]["appId"], "110001957");
        assert_eq!(sorted[2]["appId"], "110001958");
        assert!(!should_drop_qq_session(reqwest::StatusCode::BAD_REQUEST, "请先退出渠道服游戏客户端后重试"));
        assert!(should_drop_qq_session(reqwest::StatusCode::FORBIDDEN, ""));
        assert!(should_drop_qq_session(reqwest::StatusCode::BAD_REQUEST, "授权页面已过期，请重新扫码登录"));
    }
}
