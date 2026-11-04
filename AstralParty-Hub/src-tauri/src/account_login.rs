//! QR device login: the Hub receives its own opaque session, never a game SDK credential.
use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine};
use crate::domain::error::{HubError, HubResult};
use serde_json::{json, Value};
use sha2::{Digest, Sha256};
use std::{path::{Path, PathBuf}, sync::Mutex as SyncMutex, time::{Duration, Instant}};
use tauri::{Manager, State};
use tokio::sync::Mutex;

#[path = "cloud_hand_sync.rs"]
pub(crate) mod cloud_hand_sync;
use cloud_hand_sync::CloudHandSync;

const ORIGIN: &str = "https://124.221.176.134";
const MAX_RESPONSE: usize = 512 * 1024;

#[derive(Default)]
pub struct AccountLogin {
    gate: Mutex<()>,
    pending: Mutex<Option<Pending>>,
    hand_sync: SyncMutex<CloudHandSync>,
}

struct Pending { request_id: String, verifier: String, started: Instant }
fn error(text: &str) -> HubError { HubError::new("ACCOUNT_LOGIN", text, "HUB").with_recoverable(true) }
fn pkce(verifier: &str) -> String { URL_SAFE_NO_PAD.encode(Sha256::digest(verifier.as_bytes())) }

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
    if !response.status().is_success() { return Err(error("登录请求未完成，请稍后重试或重新发起登录。")); }
    let mut bytes = Vec::new();
    while let Some(chunk) = response.chunk().await.map_err(|_| error("登录响应读取失败。"))? {
        if bytes.len() + chunk.len() > MAX_RESPONSE { return Err(error("登录响应无效。")); }
        bytes.extend_from_slice(&chunk);
    }
    serde_json::from_slice(&bytes).map_err(|_| error("登录服务返回了无效响应。"))
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
    if result["status"] == "expired" || result["status"] == "superseded" { *pending = None; }
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
}
