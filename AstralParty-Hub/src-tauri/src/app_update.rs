//! Application updates are separate from game runtime installation.
use crate::domain::error::{HubError, HubResult};
use serde_json::{json, Value};
use std::{path::PathBuf, process::Stdio, time::Duration};
use tauri::State;
use tokio::{io::{AsyncBufReadExt, AsyncWriteExt, BufReader}, process::Command, sync::Mutex};

#[derive(Default)]
pub struct AppUpdater {
    gate: Mutex<()>,
    prepared: Mutex<Option<PathBuf>>,
}
const BUILD: &str = include_str!("update_build.json");
const WORKER: &[u8] = include_bytes!(concat!(env!("OUT_DIR"), "/XingShuUpdate.exe"));
fn error(s: impl Into<String>) -> HubError { HubError::new("APP_UPDATE", s, "HUB").with_recoverable(true) }
fn identity() -> Value { serde_json::from_str(BUILD).expect("invalid build identity") }
fn package_root() -> HubResult<PathBuf> {
    let exe = std::env::current_exe().map_err(|e| error(e.to_string()))?;
    let hub = exe.parent().ok_or_else(|| error("Hub 目录无效"))?;
    let root = hub.parent().ok_or_else(|| error("发布包目录无效"))?;
    if !hub.file_name().is_some_and(|p| p.to_string_lossy().eq_ignore_ascii_case("Hub")) || !root.join("release.json").is_file() {
        return Err(error("请先完整解压电脑版发布包，再从 Hub 文件夹启动。"));
    }
    dunce::canonicalize(root).map_err(|e| error(e.to_string()))
}
fn worker() -> HubResult<(tempfile::TempDir, PathBuf)> {
    let dir = tempfile::Builder::new().prefix("xingshu-update-").tempdir().map_err(|e| error(e.to_string()))?;
    let exe = dir.path().join("XingShuUpdate.exe");
    std::fs::write(&exe, WORKER).map_err(|e| error(e.to_string()))?;
    Ok((dir, exe))
}
async fn run(mode: &str, request: Value, seconds: u64) -> HubResult<Value> {
    let (_dir, exe) = worker()?;
    let mut command = Command::new(exe);
    command.arg(mode).stdin(Stdio::piped()).stdout(Stdio::piped()).stderr(Stdio::null()).kill_on_drop(true);
    #[cfg(windows)] command.creation_flags(0x08000000);
    let mut child = command.spawn().map_err(|e| error(e.to_string()))?;
    let mut input = child.stdin.take().ok_or_else(|| error("无法连接更新工具"))?;
    input.write_all(format!("{}\n", request).as_bytes()).await.map_err(|e| error(e.to_string()))?;
    drop(input);
    let output = tokio::time::timeout(Duration::from_secs(seconds), child.wait_with_output()).await
        .map_err(|_| error("更新服务响应超时，请稍后重试。"))?.map_err(|e| error(e.to_string()))?;
    let mut result: Value = serde_json::from_slice(&output.stdout).map_err(|_| error("更新服务返回无效内容。"))?;
    if let Some(e) = result.get("error").and_then(Value::as_str) { return Err(error(e)); }
    if !output.status.success() { return Err(error("更新服务未能完成操作。")); }
    result["currentVersion"] = identity()["version"].clone();
    result["canApply"] = Value::Bool(package_root().is_ok());
    Ok(result)
}
#[tauri::command]
pub async fn check_app_update(state: State<'_, AppUpdater>, preview: bool) -> HubResult<Value> {
    let _guard = state.gate.try_lock().map_err(|_| error("已有更新任务正在处理。"))?;
    run("--check", json!({"current":identity()["sequence"],"preview":preview}), 180).await
}
#[tauri::command]
pub async fn prepare_app_update(state: State<'_, AppUpdater>, preview: bool, sequence: i64) -> HubResult<Value> {
    let _guard = state.gate.try_lock().map_err(|_| error("已有更新任务正在处理。"))?;
    package_root()?;
    *state.prepared.lock().await = None;
    let mut result = run("--download", json!({"current":identity()["sequence"],"preview":preview,"sequence":sequence}), 1800).await?;
    let cache = result.get("cache").and_then(Value::as_str).ok_or_else(|| error("更新未完成暂存。"))?;
    *state.prepared.lock().await = Some(PathBuf::from(cache));
    result.as_object_mut().unwrap().remove("cache");
    Ok(result)
}
#[tauri::command]
pub async fn apply_app_update(app: tauri::AppHandle, state: State<'_, AppUpdater>) -> HubResult<()> {
    let _guard = state.gate.try_lock().map_err(|_| error("已有更新任务正在处理。"))?;
    let cache = state.prepared.lock().await.clone().ok_or_else(|| error("请先下载并校验更新。"))?;
    let root = package_root()?;
    let (dir, exe) = worker()?;
    let mut command = Command::new(exe);
    command.arg("--apply").stdin(Stdio::piped()).stdout(Stdio::piped()).stderr(Stdio::null());
    #[cfg(windows)] command.creation_flags(0x08000000);
    let mut child = command.spawn().map_err(|e| error(e.to_string()))?;
    let mut input = child.stdin.take().ok_or_else(|| error("无法连接更新工具"))?;
    input.write_all(format!("{}\n", json!({"cache":cache,"root":root,"pid":std::process::id(),"current":identity()["sequence"]})).as_bytes())
        .await.map_err(|e| error(e.to_string()))?;
    drop(input);
    let mut line = String::new();
    let stdout = child.stdout.take().ok_or_else(|| error("无法读取更新工具状态"))?;
    tokio::time::timeout(Duration::from_secs(120), BufReader::new(stdout).read_line(&mut line)).await
        .map_err(|_| error("更新准备超时，Hub 保持运行。"))?.map_err(|e| error(e.to_string()))?;
    let ready: Value = serde_json::from_str(&line).map_err(|_| error("更新工具未就绪。"))?;
    if ready.get("ready").and_then(Value::as_bool) != Some(true) {
        return Err(error(ready.get("error").and_then(Value::as_str).unwrap_or("更新工具未就绪。")));
    }
    // The child is now waiting for this process to exit. Keep its embedded helper on disk.
    let _ = dir.keep();
    app.exit(0);
    Ok(())
}
#[tauri::command]
pub fn get_app_build() -> Value { identity() }
