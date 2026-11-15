//! Mod / Card / Skin Workshop downloader, SHA-256 verifier, atomic installer, and restorer.
use std::collections::HashSet;
use std::fs;
use std::io::{Read, Write};
use std::path::{Path, PathBuf};
use std::time::Duration;

use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use tauri::State;

use crate::application::install_service::InstallService;
use crate::domain::error::{HubError, HubResult};
use crate::domain::installation::GameInstallation;
use crate::extension_center::{game_root, get_game, ExtensionCenter};
use crate::install::gates::ProcessGate;
use crate::security::hash::compute_file_sha256;
use crate::security::path_guard::PathGuard;

const MAX_PAYLOAD_BYTES: usize = 64 * 1024 * 1024;
const MAX_ARCHIVE_BYTES: usize = 64 * 1024 * 1024;
const MAX_ZIP_UNCOMPRESSED_BYTES: u64 = 160 * 1024 * 1024;
const MAX_ZIP_ENTRIES: usize = 512;
const BUNDLE_TARGET_PREFIX: &str = "AstralParty_CN_Data/StreamingAssets/aa/StandaloneWindows64/";
const FORBIDDEN_ARCHIVE_EXTS: &[&str] = &[
    ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jse", ".wsf", ".msi", ".msp",
    ".sh", ".scr", ".com", ".pif", ".lnk",
];

#[derive(Debug, Clone, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct WorkshopDownloadFile {
    pub target: String,
    pub sha256: String,
    #[serde(default)]
    pub original_sha256: String,
    #[serde(default)]
    pub size: u64,
    #[serde(default)]
    pub payload_url: Option<String>,
}

#[derive(Debug, Clone, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct WorkshopInstallRequest {
    pub installation_id: String,
    pub api_base: String,
    pub mod_id: String,
    pub name: String,
    pub install_kind: String,
    #[serde(default)]
    pub game_version: String,
    #[serde(default)]
    pub platforms: Vec<String>,
    #[serde(default)]
    pub download_url: Option<String>,
    #[serde(default)]
    pub sha256: String,
    #[serde(default)]
    pub size_bytes: u64,
    #[serde(default)]
    pub files: Vec<WorkshopDownloadFile>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WorkshopLocalItemState {
    pub mod_id: String,
    pub phase: String,
    pub install_kind: String,
    pub transaction_id: Option<String>,
    pub downloaded_path: Option<String>,
    pub can_restore: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WorkshopInstallResult {
    pub mod_id: String,
    pub phase: String,
    pub message: String,
    pub downloaded_path: Option<String>,
    pub transaction_id: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct WorkshopReceiptFile {
    index: usize,
    target: String,
    mod_sha256: String,
    baseline_sha256: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct WorkshopReceipt {
    mod_id: String,
    name: String,
    install_kind: String,
    phase: String,
    transaction_id: Option<String>,
    downloaded_path: Option<String>,
    files: Vec<WorkshopReceiptFile>,
}

pub(crate) struct StagedBundle {
    target_rel: String,
    expected_sha256: String,
    expected_original_sha256: String,
    bytes: Vec<u8>,
}

fn net_error(msg: impl Into<String>) -> HubError {
    HubError::new("WORKSHOP_NETWORK", msg, "WORKSHOP").with_recoverable(true)
}

pub(crate) fn sanitize_mod_id(mod_id: &str) -> HubResult<String> {
    let trimmed = mod_id.trim();
    let len = trimmed.len();
    let bytes = trimmed.as_bytes();
    if !(3..=65).contains(&len)
        || !bytes[0].is_ascii_alphanumeric()
        || !bytes
            .iter()
            .all(|b| b.is_ascii_alphanumeric() || matches!(b, b'.' | b'_' | b'-'))
        || trimmed.contains("..")
    {
        return Err(HubError::security_violation("无效的工坊模组标识。"));
    }
    Ok(trimmed.to_string())
}

fn is_hex64(s: &str) -> bool {
    s.len() == 64 && s.bytes().all(|b| b.is_ascii_hexdigit())
}

pub(crate) fn sha256_hex(data: &[u8]) -> String {
    hex::encode(Sha256::digest(data))
}

/// Validates download URLs: enforces `https://` for remote origins and allows `http://` only for loopback dev servers.
pub(crate) fn resolve_and_validate_url(api_base: &str, url_or_path: &str) -> HubResult<String> {
    let raw = url_or_path.trim();
    if raw.is_empty() {
        return Err(HubError::invalid_manifest("缺少模组下载地址。"));
    }
    if raw.len() > 1000 || raw.chars().any(|c| c.is_control() || c == '\\') {
        return Err(HubError::security_violation("模组下载地址包含非法字符。"));
    }
    let combined = if raw.starts_with('/') {
        let base = api_base.trim().trim_end_matches('/');
        if base.is_empty() {
            return Err(net_error("未配置服务端 API 地址，无法拼接相对下载路径。"));
        }
        format!("{base}{raw}")
    } else {
        raw.to_string()
    };

    let (scheme, rest) = combined
        .split_once("://")
        .ok_or_else(|| HubError::security_violation("下载地址缺少合法协议头。"))?;
    let scheme_lower = scheme.to_ascii_lowercase();
    let authority = rest.split('/').next().unwrap_or("");
    if authority.is_empty() || authority.contains('@') {
        return Err(HubError::security_violation("下载地址不允许包含认证凭据或空主机。"));
    }

    let host_no_port = if let Some(bracket_end) = authority.strip_prefix('[').and_then(|s| s.find(']')) {
        &authority[1..=bracket_end]
    } else {
        authority.split(':').next().unwrap_or("")
    };
    let host_lower = host_no_port.to_ascii_lowercase();

    match scheme_lower.as_str() {
        "https" => Ok(combined),
        "http" if matches!(host_lower.as_str(), "127.0.0.1" | "localhost" | "::1") => Ok(combined),
        "http" => Err(HubError::security_violation(
            "安全限制：桌面端仅允许通过 HTTPS（或本机环回测试地址）下载模组文件。",
        )),
        _ => Err(HubError::security_violation("不支持的下载协议，仅允许 HTTPS。")),
    }
}

fn confined(root: &Path, relative: &str) -> HubResult<PathBuf> {
    let clean = PathGuard::sanitize_relative_path(relative)?;
    PathGuard::ensure_within_root(root, &root.join(clean))
}

pub(crate) fn validate_bundle_target(root: &Path, target_rel: &str) -> HubResult<(String, PathBuf)> {
    let clean = target_rel.trim().replace('\\', "/");
    let file_name = clean
        .strip_prefix(BUNDLE_TARGET_PREFIX)
        .ok_or_else(|| HubError::security_violation("模组目标不在已核验的游戏资源目录中。"))?;
    let stem = file_name.strip_suffix(".bundle").unwrap_or("");
    if stem.len() != 32 || !stem.bytes().all(|c| c.is_ascii_hexdigit()) {
        return Err(HubError::security_violation("模组目标 Bundle 文件名格式无效。"));
    }
    let resolved = confined(root, &clean)?;
    Ok((clean, resolved))
}

fn atomic_write(path: &Path, data: &[u8]) -> HubResult<()> {
    let parent = path
        .parent()
        .ok_or_else(|| HubError::storage_error("缺少写入目标父目录。"))?;
    fs::create_dir_all(parent).map_err(|e| HubError::storage_error(e.to_string()))?;
    let mut temp =
        tempfile::NamedTempFile::new_in(parent).map_err(|e| HubError::storage_error(e.to_string()))?;
    temp.write_all(data)
        .and_then(|_| temp.as_file().sync_all())
        .map_err(|e| HubError::storage_error(e.to_string()))?;
    temp.persist(path)
        .map_err(|e| HubError::storage_error(e.to_string()))?;
    Ok(())
}

fn receipt_path(root: &Path, mod_id: &str) -> HubResult<PathBuf> {
    let clean_id = sanitize_mod_id(mod_id)?;
    confined(root, &format!(".xingshu-mods/workshop-receipts/{clean_id}.json"))
}

fn backup_file_path(root: &Path, mod_id: &str, index: usize) -> HubResult<PathBuf> {
    let clean_id = sanitize_mod_id(mod_id)?;
    confined(
        root,
        &format!(".xingshu-mods/workshop-backups/{clean_id}/baseline/{index}.bundle"),
    )
}

fn read_workshop_receipt(root: &Path, mod_id: &str) -> HubResult<Option<WorkshopReceipt>> {
    let path = receipt_path(root, mod_id)?;
    if !path.is_file() {
        return Ok(None);
    }
    let raw = fs::read(&path).map_err(|e| HubError::storage_error(e.to_string()))?;
    let receipt: WorkshopReceipt =
        serde_json::from_slice(&raw).map_err(|e| HubError::storage_error(e.to_string()))?;
    if receipt.mod_id != mod_id
        || !matches!(
            receipt.phase.as_str(),
            "prepared" | "installed" | "restored" | "downloaded"
        )
    {
        return Err(HubError::security_violation("工坊安装收据校验无效。"));
    }
    Ok(Some(receipt))
}

fn save_workshop_receipt(root: &Path, receipt: &WorkshopReceipt) -> HubResult<()> {
    let path = receipt_path(root, &receipt.mod_id)?;
    let bytes = serde_json::to_vec_pretty(receipt).map_err(|e| HubError::storage_error(e.to_string()))?;
    atomic_write(&path, &bytes)
}

fn operation_lock(root: &Path) -> HubResult<fs::File> {
    let path = confined(root, ".xingshu-mods/operation.lock")?;
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).map_err(|e| HubError::storage_error(e.to_string()))?;
    }
    let mut options = fs::OpenOptions::new();
    options.write(true).create(true).truncate(false);
    #[cfg(windows)]
    {
        use std::os::windows::fs::OpenOptionsExt;
        options.share_mode(0);
    }
    options
        .open(path)
        .map_err(|_| HubError::storage_error("另一个模组或扩展安装任务正在进行中，请稍后重试。"))
}

fn game_closed(game: &GameInstallation) -> HubResult<()> {
    ProcessGate::check_target(Path::new(
        game.executable_path.as_deref().unwrap_or_default(),
    ))
}

fn local_mod_library_root() -> Option<PathBuf> {
    std::env::current_exe()
        .ok()?
        .ancestors()
        .skip(1)
        .take(4)
        .map(|p| p.join("ModLibrary"))
        .find(|p| p.join("catalog.json").is_file())
}

async fn download_verified_bytes(
    client: &reqwest::Client,
    url: &str,
    expected_sha256: &str,
    max_bytes: usize,
) -> HubResult<Vec<u8>> {
    let resp = client
        .get(url)
        .send()
        .await
        .map_err(|e| net_error(format!("下载模组文件失败：{e}")))?;
    let status = resp.status();
    if !status.is_success() {
        return Err(net_error(format!("服务端返回错误状态 HTTP {}", status.as_u16())));
    }
    if let Some(len) = resp.content_length() {
        if len > max_bytes as u64 {
            return Err(HubError::security_violation("下载文件体积超出安全限制。"));
        }
    }
    let bytes = resp
        .bytes()
        .await
        .map_err(|e| net_error(format!("读取下载流失败：{e}")))?;
    if bytes.is_empty() || bytes.len() > max_bytes {
        return Err(HubError::security_violation("下载文件为空或超出安全体积上限。"));
    }
    let data = bytes.to_vec();
    if !expected_sha256.is_empty() {
        let actual = sha256_hex(&data);
        if !actual.eq_ignore_ascii_case(expected_sha256) {
            return Err(HubError::security_violation(format!(
                "SHA-256 校验不匹配（期望 {expected_sha256}，实际 {actual}），已拒绝安装。"
            )));
        }
    }
    Ok(data)
}

fn extract_bundle_entries_from_zip(
    root: &Path,
    archive_bytes: &[u8],
) -> HubResult<Vec<StagedBundle>> {
    let cursor = std::io::Cursor::new(archive_bytes);
    let mut zf = zip::ZipArchive::new(cursor)
        .map_err(|_| HubError::invalid_manifest("下载的模组压缩包已损坏，无法读取。"))?;
    if zf.is_empty() || zf.len() > MAX_ZIP_ENTRIES {
        return Err(HubError::security_violation("压缩包文件条目数量异常。"));
    }

    let mut staged = Vec::new();
    let mut total_uncompressed: u64 = 0;
    let mut seen_targets = HashSet::new();

    for idx in 0..zf.len() {
        let mut entry = zf
            .by_index(idx)
            .map_err(|e| HubError::invalid_manifest(format!("读取压缩包条目失败：{e}")))?;
        if entry.is_symlink() {
            return Err(HubError::security_violation("出于安全限制，压缩包禁止包含符号链接。"));
        }
        let raw_name = entry.name().replace('\\', "/");
        if entry.is_dir() {
            let _ = PathGuard::sanitize_relative_path(&raw_name)?;
            continue;
        }
        total_uncompressed = total_uncompressed.saturating_add(entry.size());
        if total_uncompressed > MAX_ZIP_UNCOMPRESSED_BYTES {
            return Err(HubError::security_violation("压缩包解压体积超过 160MB 安全上限。"));
        }
        let clean_entry = PathGuard::sanitize_relative_path(&raw_name)?
            .to_string_lossy()
            .replace('\\', "/");
        let lower = clean_entry.to_ascii_lowercase();
        if FORBIDDEN_ARCHIVE_EXTS.iter().any(|ext| lower.ends_with(ext)) {
            return Err(HubError::security_violation("压缩包禁止包含可执行脚本或二进制文件。"));
        }
        if !lower.ends_with(".bundle") {
            continue;
        }
        let candidate_target = if clean_entry.starts_with(BUNDLE_TARGET_PREFIX) {
            clean_entry.clone()
        } else {
            let base_name = Path::new(&clean_entry)
                .file_name()
                .and_then(|s| s.to_str())
                .unwrap_or("");
            format!("{BUNDLE_TARGET_PREFIX}{base_name}")
        };
        let (target_rel, target_abs) = validate_bundle_target(root, &candidate_target)?;
        if !target_abs.is_file() {
            return Err(HubError::invalid_manifest(format!(
                "游戏目录中不存在对应的原始资产 {}，版本可能不匹配。",
                Path::new(&target_rel)
                    .file_name()
                    .and_then(|s| s.to_str())
                    .unwrap_or(&target_rel)
            )));
        }
        if !seen_targets.insert(target_rel.clone()) {
            return Err(HubError::invalid_manifest("压缩包内包含重复的 Bundle 替换目标。"));
        }
        let mut data = Vec::new();
        entry
            .by_ref()
            .take((MAX_PAYLOAD_BYTES + 1) as u64)
            .read_to_end(&mut data)
            .map_err(|e| HubError::storage_error(e.to_string()))?;
        if data.is_empty() || data.len() > MAX_PAYLOAD_BYTES {
            return Err(HubError::security_violation("单个 Bundle 文件体积异常。"));
        }
        let digest = sha256_hex(&data);
        staged.push(StagedBundle {
            target_rel,
            expected_sha256: digest,
            expected_original_sha256: String::new(),
            bytes: data,
        });
    }
    if staged.is_empty() {
        return Err(HubError::invalid_manifest(
            "压缩包中未发现合法的 StandaloneWindows64 Bundle 资源。",
        ));
    }
    Ok(staged)
}

fn check_zip_contains_xspkg_manifest(archive_bytes: &[u8]) -> HubResult<bool> {
    let cursor = std::io::Cursor::new(archive_bytes);
    let mut zf = zip::ZipArchive::new(cursor)
        .map_err(|_| HubError::invalid_manifest("下载的模组压缩包格式无效。"))?;
    let has_manifest = zf.by_name("manifest.json").is_ok();
    Ok(has_manifest)
}

pub(crate) fn install_bundle_entries(
    game: &GameInstallation,
    root: &Path,
    mod_id: &str,
    mod_name: &str,
    staged: &[StagedBundle],
) -> HubResult<WorkshopReceipt> {
    let clean_id = sanitize_mod_id(mod_id)?;
    if staged.is_empty() {
        return Err(HubError::invalid_manifest("模组不含任何可安装的文件。"));
    }
    game_closed(game)?;

    if let Some(existing) = read_workshop_receipt(root, &clean_id)? {
        if existing.phase != "restored" {
            return Err(HubError::invalid_manifest("该模组当前已安装或处于未完成状态，请先执行还原。"));
        }
    }

    // Check legacy ModLibrary receipts too so we never double-install over an active ModLibrary entry.
    if clean_id.starts_with("local.") {
        let legacy_receipt = confined(root, &format!(".xingshu-mods/receipts/{clean_id}/receipt.json"))?;
        if legacy_receipt.is_file() {
            let text = fs::read_to_string(&legacy_receipt).unwrap_or_default();
            if text.contains("\"installed\"") || text.contains("\"prepared\"") {
                return Err(HubError::invalid_manifest("此模组已通过内置模组库启用，请勿重复安装。"));
            }
        }
    }

    let mut unique = HashSet::new();
    let mut targets_and_baselines: Vec<(PathBuf, String)> = Vec::with_capacity(staged.len());

    // Preflight every file and backup before mutating any game file.
    for entry in staged {
        let (norm_target, target_abs) = validate_bundle_target(root, &entry.target_rel)?;
        if !unique.insert(norm_target) {
            return Err(HubError::invalid_manifest("模组内包含重复的目标路径。"));
        }
        if !target_abs.is_file() {
            return Err(HubError::invalid_manifest(
                "游戏目标资产文件不存在，当前游戏版本或渠道可能尚未适配。",
            ));
        }
        if sha256_hex(&entry.bytes) != entry.expected_sha256 {
            return Err(HubError::security_violation("暂存的模组字节校验失败。"));
        }
        let current_hash = compute_file_sha256(&target_abs)?;
        if !entry.expected_original_sha256.is_empty()
            && !current_hash.eq_ignore_ascii_case(&entry.expected_original_sha256)
        {
            return Err(HubError::security_violation(
                "游戏原始资产哈希与适配基线不一致（可能版本不匹配或已被其他模组修改），已停止安装。",
            ));
        }
        if current_hash.eq_ignore_ascii_case(&entry.expected_sha256) {
            return Err(HubError::invalid_manifest(
                "检测到游戏目录已存在相同外部模组文件且缺少备份，请先还原原版文件后再用星枢安装。",
            ));
        }
        targets_and_baselines.push((target_abs, current_hash));
    }

    let mut receipt_files = Vec::with_capacity(staged.len());
    for (idx, ((target_abs, baseline_hash), entry)) in
        targets_and_baselines.iter().zip(staged.iter()).enumerate()
    {
        let backup_dest = backup_file_path(root, &clean_id, idx)?;
        let baseline_bytes = fs::read(target_abs).map_err(|e| HubError::storage_error(e.to_string()))?;
        if sha256_hex(&baseline_bytes) != *baseline_hash {
            return Err(HubError::security_violation("备份期间原游戏文件发生变化，已终止。"));
        }
        atomic_write(&backup_dest, &baseline_bytes)?;
        if compute_file_sha256(&backup_dest)? != *baseline_hash {
            return Err(HubError::security_violation("安装前备份校验失败。"));
        }
        receipt_files.push(WorkshopReceiptFile {
            index: idx,
            target: entry.target_rel.clone(),
            mod_sha256: entry.expected_sha256.clone(),
            baseline_sha256: baseline_hash.clone(),
        });
    }

    let mut receipt = WorkshopReceipt {
        mod_id: clean_id,
        name: mod_name.to_string(),
        install_kind: "bundle_mod".into(),
        phase: "prepared".into(),
        transaction_id: None,
        downloaded_path: None,
        files: receipt_files,
    };

    game_closed(game)?;
    save_workshop_receipt(root, &receipt)?;

    let commit = (|| -> HubResult<()> {
        for (idx, ((target_abs, _baseline_hash), entry)) in
            targets_and_baselines.iter().zip(staged.iter()).enumerate()
        {
            atomic_write(target_abs, &entry.bytes)?;
            let post_hash = compute_file_sha256(target_abs)?;
            if !post_hash.eq_ignore_ascii_case(&receipt.files[idx].mod_sha256) {
                return Err(HubError::security_violation("写入后的模组文件哈希校验失败。"));
            }
        }
        receipt.phase = "installed".into();
        save_workshop_receipt(root, &receipt)
    })();

    if let Err(err) = commit {
        return match restore_bundle_receipt(root, &receipt) {
            Ok(()) => Err(err),
            Err(rollback_err) => Err(HubError::storage_error(format!(
                "{err}；自动回滚未完成：{rollback_err}。已保留备份，请重试还原。"
            ))),
        };
    }

    Ok(receipt)
}

pub(crate) fn restore_bundle_receipt(root: &Path, receipt: &WorkshopReceipt) -> HubResult<()> {
    if receipt.phase == "restored" {
        return Ok(());
    }
    if receipt.files.is_empty() {
        return Err(HubError::storage_error("没有可还原的文件记录。"));
    }

    // Verify all backups and current target states before restoring any file.
    for f in &receipt.files {
        let (_, target_abs) = validate_bundle_target(root, &f.target)?;
        let backup_abs = backup_file_path(root, &receipt.mod_id, f.index)?;
        if !backup_abs.is_file() {
            return Err(HubError::storage_error("安装前备份文件缺失，无法还原。"));
        }
        if !compute_file_sha256(&backup_abs)?.eq_ignore_ascii_case(&f.baseline_sha256) {
            return Err(HubError::security_violation("安装前备份文件校验失败，停止还原。"));
        }
        let current_hash = if target_abs.is_file() {
            compute_file_sha256(&target_abs)?
        } else {
            String::new()
        };
        if !current_hash.eq_ignore_ascii_case(&f.mod_sha256)
            && !current_hash.eq_ignore_ascii_case(&f.baseline_sha256)
        {
            return Err(HubError::security_violation(
                "目标文件已被外部修改或游戏已更新，已停止还原以防覆盖。",
            ));
        }
    }

    for f in &receipt.files {
        let (_, target_abs) = validate_bundle_target(root, &f.target)?;
        let backup_abs = backup_file_path(root, &receipt.mod_id, f.index)?;
        let baseline_bytes = fs::read(&backup_abs).map_err(|e| HubError::storage_error(e.to_string()))?;
        if !sha256_hex(&baseline_bytes).eq_ignore_ascii_case(&f.baseline_sha256) {
            return Err(HubError::security_violation("读取备份时校验不一致，停止还原。"));
        }
        atomic_write(&target_abs, &baseline_bytes)?;
        if !compute_file_sha256(&target_abs)?.eq_ignore_ascii_case(&f.baseline_sha256) {
            return Err(HubError::security_violation("还原后的文件校验失败，保留收据以便重试。"));
        }
    }

    let mut updated = receipt.clone();
    updated.phase = "restored".into();
    save_workshop_receipt(root, &updated)
}

#[tauri::command]
pub async fn workshop_list_local_state(
    installation_id: String,
    center: State<'_, ExtensionCenter>,
) -> HubResult<Vec<WorkshopLocalItemState>> {
    if installation_id.trim().is_empty() {
        return Ok(Vec::new());
    }
    let game = get_game(&center.db, &installation_id)?;
    tokio::task::spawn_blocking(move || {
        let root = game_root(&game)?;
        let mut out = Vec::new();
        let mut seen = HashSet::new();

        let ws_dir = confined(&root, ".xingshu-mods/workshop-receipts")?;
        if ws_dir.is_dir() {
            for entry in fs::read_dir(&ws_dir).map_err(|e| HubError::storage_error(e.to_string()))? {
                let Ok(entry) = entry else { continue };
                let path = entry.path();
                if path.extension().and_then(|s| s.to_str()) != Some("json") {
                    continue;
                }
                if let Ok(raw) = fs::read(&path) {
                    if let Ok(r) = serde_json::from_slice::<WorkshopReceipt>(&raw) {
                        seen.insert(r.mod_id.clone());
                        let can_restore = matches!(r.phase.as_str(), "installed" | "prepared");
                        out.push(WorkshopLocalItemState {
                            mod_id: r.mod_id,
                            phase: r.phase,
                            install_kind: r.install_kind,
                            transaction_id: r.transaction_id,
                            downloaded_path: r.downloaded_path,
                            can_restore,
                        });
                    }
                }
            }
        }

        // Also surface existing ModLibrary receipts (e.g. from ExternalModsPanel) cleanly.
        let legacy_dir = confined(&root, ".xingshu-mods/receipts")?;
        if legacy_dir.is_dir() {
            if let Ok(dirs) = fs::read_dir(&legacy_dir) {
                for entry in dirs.flatten() {
                    let rpath = entry.path().join("receipt.json");
                    if !rpath.is_file() {
                        continue;
                    }
                    if let Ok(raw) = fs::read(&rpath) {
                        if let Ok(val) = serde_json::from_slice::<serde_json::Value>(&raw) {
                            let item_id = val["item_id"].as_str().unwrap_or("").to_string();
                            let phase = val["phase"].as_str().unwrap_or("").to_string();
                            if !item_id.is_empty() && !seen.contains(&item_id) {
                                let can_restore = matches!(phase.as_str(), "installed" | "prepared");
                                out.push(WorkshopLocalItemState {
                                    mod_id: item_id,
                                    phase,
                                    install_kind: "bundle_mod".into(),
                                    transaction_id: None,
                                    downloaded_path: None,
                                    can_restore,
                                });
                            }
                        }
                    }
                }
            }
        }

        Ok(out)
    })
    .await
    .map_err(|e| HubError::storage_error(e.to_string()))?
}

#[tauri::command]
pub async fn workshop_download_and_install(
    request: WorkshopInstallRequest,
    center: State<'_, ExtensionCenter>,
    install_service: State<'_, InstallService>,
) -> HubResult<WorkshopInstallResult> {
    let mod_id = sanitize_mod_id(&request.mod_id)?;
    let game = get_game(&center.db, &request.installation_id)?;
    if !request.platforms.is_empty() && !request.platforms.iter().any(|p| p == &game.platform) {
        return Err(HubError::invalid_manifest(format!(
            "此模组适配 {}，与当前所选渠道 {} 不符。",
            request.platforms.join(" / "),
            game.platform
        )));
    }
    let root = game_root(&game)?;
    game_closed(&game)?;

    let client = reqwest::Client::builder()
        .timeout(Duration::from_secs(120))
        .build()
        .map_err(|e| net_error(format!("初始化下载客户端失败：{e}")))?;

    // Path 1: Explicit verified bundle file mapping (Seed items & curated bundle manifests).
    if request.install_kind == "bundle_mod" && !request.files.is_empty() {
        let local_lib = local_mod_library_root();
        let mut staged = Vec::with_capacity(request.files.len());
        for f in &request.files {
            if !is_hex64(&f.sha256) {
                return Err(HubError::security_violation("模组文件缺少有效的 SHA-256 指纹。"));
            }
            let (norm_target, _) = validate_bundle_target(&root, &f.target)?;
            let bytes = if let Some(ref url_or_path) = f.payload_url {
                let url = resolve_and_validate_url(&request.api_base, url_or_path)?;
                download_verified_bytes(&client, &url, &f.sha256, MAX_PAYLOAD_BYTES).await?
            } else if let Some(ref lib) = local_lib {
                let local_file = confined(lib, &format!("payload/{}.bundle", f.sha256))?;
                if !local_file.is_file() {
                    return Err(HubError::storage_error(
                        "服务端尚未上传此条目的资产包，且本机 ModLibrary 中缺少对应文件。",
                    ));
                }
                let data = fs::read(&local_file).map_err(|e| HubError::storage_error(e.to_string()))?;
                if sha256_hex(&data) != f.sha256.to_ascii_lowercase() {
                    return Err(HubError::security_violation("本地 ModLibrary 文件指纹不匹配。"));
                }
                data
            } else {
                return Err(HubError::storage_error(
                    "服务端未提供该条目的资产下载，且本机未附带 ModLibrary/payload；请先补充服务端资产或前往发布页下载。",
                ));
            };
            if f.size > 0 && bytes.len() as u64 != f.size {
                return Err(HubError::security_violation("下载的资产体积与清单声明不符。"));
            }
            staged.push(StagedBundle {
                target_rel: norm_target,
                expected_sha256: f.sha256.to_ascii_lowercase(),
                expected_original_sha256: f.original_sha256.to_ascii_lowercase(),
                bytes,
            });
        }

        let name = request.name.clone();
        let receipt = tokio::task::spawn_blocking(move || {
            let _lock = operation_lock(&root)?;
            install_bundle_entries(&game, &root, &mod_id, &name, &staged)
        })
        .await
        .map_err(|e| HubError::storage_error(e.to_string()))??;

        return Ok(WorkshopInstallResult {
            mod_id: receipt.mod_id,
            phase: receipt.phase,
            message: "已下载并通过 SHA-256 核验、原子备份原版资产后完成安装。".into(),
            downloaded_path: None,
            transaction_id: None,
        });
    }

    // Path 2: Single downloadable package archive (.xspkg or .zip).
    let dl_raw = request
        .download_url
        .as_deref()
        .ok_or_else(|| HubError::invalid_manifest("此工坊条目没有提供可下载的模组包地址。"))?;
    let dl_url = resolve_and_validate_url(&request.api_base, dl_raw)?;
    if !request.sha256.is_empty() && !is_hex64(&request.sha256) {
        return Err(HubError::security_violation("工坊条目声明的 SHA-256 格式无效。"));
    }
    if request.sha256.is_empty() && request.install_kind != "external_archive" {
        return Err(HubError::security_violation(
            "安全策略：直接安装的在线模组包必须提供 SHA-256 摘要，请从发布页核验后再手动导入。",
        ));
    }

    let archive_bytes =
        download_verified_bytes(&client, &dl_url, &request.sha256, MAX_ARCHIVE_BYTES).await?;

    if request.install_kind == "xspkg" || check_zip_contains_xspkg_manifest(&archive_bytes)? {
        let dl_dir = confined(&root, &format!(".xingshu-mods/downloads/{mod_id}"))?;
        fs::create_dir_all(&dl_dir).map_err(|e| HubError::storage_error(e.to_string()))?;
        let pkg_path = dl_dir.join(format!("{mod_id}.xspkg"));
        atomic_write(&pkg_path, &archive_bytes)?;

        let svc = install_service.inner();
        let pkg_for_install = pkg_path.clone();
        let inst_id = request.installation_id.clone();
        let mod_name = request.name.clone();
        let receipt = tokio::task::block_in_place(|| -> HubResult<WorkshopReceipt> {
            let _lock = operation_lock(&root)?;
            let tx = svc.install_package_from_zip(&pkg_for_install, &inst_id)?;
            let receipt = WorkshopReceipt {
                mod_id: mod_id.clone(),
                name: mod_name,
                install_kind: "xspkg".into(),
                phase: "installed".into(),
                transaction_id: Some(tx.id.clone()),
                downloaded_path: Some(pkg_for_install.display().to_string()),
                files: Vec::new(),
            };
            save_workshop_receipt(&root, &receipt)?;
            Ok(receipt)
        })?;

        return Ok(WorkshopInstallResult {
            mod_id: receipt.mod_id,
            phase: receipt.phase,
            message: "已完成 .xspkg 安全验证并安装，可在模组页面或安装事务中一键回滚。".into(),
            downloaded_path: receipt.downloaded_path,
            transaction_id: receipt.transaction_id,
        });
    }

    if request.install_kind == "bundle_mod" {
        let root_for_zip = root.clone();
        let name = request.name.clone();
        let receipt = tokio::task::spawn_blocking(move || {
            let _lock = operation_lock(&root_for_zip)?;
            let staged = extract_bundle_entries_from_zip(&root_for_zip, &archive_bytes)?;
            install_bundle_entries(&game, &root_for_zip, &mod_id, &name, &staged)
        })
        .await
        .map_err(|e| HubError::storage_error(e.to_string()))??;

        return Ok(WorkshopInstallResult {
            mod_id: receipt.mod_id,
            phase: receipt.phase,
            message: "已解析 Bundle 资源包、备份原版文件并完成安装。".into(),
            downloaded_path: None,
            transaction_id: None,
        });
    }

    // external_archive fallback: verify zip, store safely under .xingshu-mods/downloads, never blind-extract.
    let dl_dir = confined(&root, &format!(".xingshu-mods/downloads/{mod_id}"))?;
    let zip_path = dl_dir.join(format!("{mod_id}.zip"));
    atomic_write(&zip_path, &archive_bytes)?;
    let receipt = WorkshopReceipt {
        mod_id: mod_id.clone(),
        name: request.name,
        install_kind: "external_archive".into(),
        phase: "downloaded".into(),
        transaction_id: None,
        downloaded_path: Some(zip_path.display().to_string()),
        files: Vec::new(),
    };
    save_workshop_receipt(&root, &receipt)?;

    Ok(WorkshopInstallResult {
        mod_id: receipt.mod_id,
        phase: receipt.phase,
        message: format!(
            "已下载并校验压缩包至 {}（该包为通用压缩包格式，请按作者说明对照使用）。",
            zip_path.display()
        ),
        downloaded_path: receipt.downloaded_path,
        transaction_id: None,
    })
}

#[tauri::command]
pub async fn workshop_restore_mod(
    installation_id: String,
    mod_id: String,
    center: State<'_, ExtensionCenter>,
    install_service: State<'_, InstallService>,
) -> HubResult<String> {
    let clean_id = sanitize_mod_id(&mod_id)?;
    let game = get_game(&center.db, &installation_id)?;
    let root = game_root(&game)?;
    game_closed(&game)?;

    if let Some(receipt) = read_workshop_receipt(&root, &clean_id)? {
        if receipt.install_kind == "xspkg" {
            let tx_id = receipt
                .transaction_id
                .clone()
                .ok_or_else(|| HubError::storage_error("缺少 .xspkg 安装事务标识，请前往安装事务页回滚。"))?;
            let svc = install_service.inner();
            tokio::task::block_in_place(|| -> HubResult<()> {
                let _lock = operation_lock(&root)?;
                svc.rollback_transaction(&tx_id)?;
                let mut updated = receipt;
                updated.phase = "restored".into();
                save_workshop_receipt(&root, &updated)
            })?;
            return Ok("已通过安装事务回滚 .xspkg 模组，原文件已恢复。".into());
        }
        tokio::task::spawn_blocking(move || {
            let _lock = operation_lock(&root)?;
            restore_bundle_receipt(&root, &receipt)
        })
        .await
        .map_err(|e| HubError::storage_error(e.to_string()))??;
        return Ok("已还原安装前备份文件。".into());
    }

    if clean_id.starts_with("local.") {
        tokio::task::spawn_blocking(move || {
            let _lock = operation_lock(&root)?;
            crate::external_mods::restore_item_by_id(&root, &clean_id)
        })
        .await
        .map_err(|e| HubError::storage_error(e.to_string()))??;
        return Ok("已还原内置模组库条目的安装前备份文件。".into());
    }

    Err(HubError::storage_error("未找到该工坊条目的安装备份记录。"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn validates_https_and_rejects_unsafe_download_urls() {
        assert_eq!(
            resolve_and_validate_url("https://astralparty.example.com", "/api/workshop/mods/abc/download").unwrap(),
            "https://astralparty.example.com/api/workshop/mods/abc/download"
        );
        assert_eq!(
            resolve_and_validate_url("http://127.0.0.1:8769", "/api/workshop/mods/abc/download").unwrap(),
            "http://127.0.0.1:8769/api/workshop/mods/abc/download"
        );
        assert!(resolve_and_validate_url("", "/api/workshop/mods/abc/download").is_err());
        assert!(resolve_and_validate_url("", "http://example.com/mod.zip").is_err());
        assert!(resolve_and_validate_url("", "file:///C:/Windows/win.ini").is_err());
        assert!(resolve_and_validate_url("", "https://user:pass@example.com/mod.zip").is_err());
        assert!(resolve_and_validate_url("https://example.com", "/api/..\\evil").is_err());
    }

    #[test]
    fn sanitizes_mod_ids_and_confines_bundle_targets() {
        assert_eq!(sanitize_mod_id("local.658bfbc2cf723c8b").unwrap(), "local.658bfbc2cf723c8b");
        assert!(sanitize_mod_id("../escape").is_err());
        assert!(sanitize_mod_id("sub.1234/evil").is_err());
        let dir = tempfile::tempdir().unwrap();
        let valid_target = "AstralParty_CN_Data/StreamingAssets/aa/StandaloneWindows64/0123456789abcdef0123456789abcdef.bundle";
        assert!(validate_bundle_target(dir.path(), valid_target).is_ok());
        assert!(validate_bundle_target(dir.path(), "AstralParty_CN_Data/StreamingAssets/aa/StandaloneWindows64/bad.bundle").is_err());
        assert!(validate_bundle_target(dir.path(), "../../etc/passwd").is_err());
    }

    fn fixture_game(root: &Path) -> (GameInstallation, String, Vec<u8>, Vec<u8>) {
        let exe = root.join("AstralParty_CN.exe");
        fs::write(&exe, b"exe").unwrap();
        fs::write(root.join("GameAssembly.dll"), b"dll").unwrap();
        let rel = "AstralParty_CN_Data/StreamingAssets/aa/StandaloneWindows64/0123456789abcdef0123456789abcdef.bundle";
        let abs = root.join(rel);
        fs::create_dir_all(abs.parent().unwrap()).unwrap();
        let orig = b"original-bundle-content".to_vec();
        let modded = b"workshop-skin-content".to_vec();
        fs::write(&abs, &orig).unwrap();
        let game = GameInstallation {
            id: "inst-1".into(),
            platform: "STEAM_CN".into(),
            platform_app_id: None,
            install_path: root.display().to_string(),
            executable_path: Some(exe.display().to_string()),
            detected_build_id: None,
            detected_version: None,
            detection_method: "TEST".into(),
            is_valid: true,
            is_primary: true,
            last_verified_at: None,
            created_at: chrono::Utc::now(),
            updated_at: chrono::Utc::now(),
        };
        (game, rel.into(), orig, modded)
    }

    #[test]
    fn bundle_install_and_restore_roundtrip_with_backup_verification() {
        let tmp = tempfile::tempdir().unwrap();
        let (game, rel, orig, modded) = fixture_game(tmp.path());
        let staged = vec![StagedBundle {
            target_rel: rel.clone(),
            expected_sha256: sha256_hex(&modded),
            expected_original_sha256: sha256_hex(&orig),
            bytes: modded.clone(),
        }];

        let receipt = install_bundle_entries(&game, tmp.path(), "sub.test001", "Test Skin", &staged).unwrap();
        assert_eq!(receipt.phase, "installed");
        assert_eq!(fs::read(tmp.path().join(&rel)).unwrap(), modded);

        // Duplicate install before restore is rejected.
        assert!(install_bundle_entries(&game, tmp.path(), "sub.test001", "Test Skin", &staged).is_err());

        restore_bundle_receipt(tmp.path(), &receipt).unwrap();
        assert_eq!(fs::read(tmp.path().join(&rel)).unwrap(), orig);
        let stored = read_workshop_receipt(tmp.path(), "sub.test001").unwrap().unwrap();
        assert_eq!(stored.phase, "restored");
    }

    #[test]
    fn baseline_mismatch_aborts_before_creating_backups() {
        let tmp = tempfile::tempdir().unwrap();
        let (game, rel, _orig, modded) = fixture_game(tmp.path());
        let staged = vec![StagedBundle {
            target_rel: rel.clone(),
            expected_sha256: sha256_hex(&modded),
            expected_original_sha256: "f".repeat(64),
            bytes: modded,
        }];
        assert!(install_bundle_entries(&game, tmp.path(), "sub.test002", "Bad Baseline", &staged).is_err());
        assert!(!tmp.path().join(".xingshu-mods/workshop-backups").exists());
    }

    #[test]
    fn running_game_process_blocks_install() {
        let tmp = tempfile::tempdir().unwrap();
        let (mut game, rel, orig, modded) = fixture_game(tmp.path());
        game.executable_path = Some(std::env::current_exe().unwrap().display().to_string());
        let staged = vec![StagedBundle {
            target_rel: rel,
            expected_sha256: sha256_hex(&modded),
            expected_original_sha256: sha256_hex(&orig),
            bytes: modded,
        }];
        assert!(install_bundle_entries(&game, tmp.path(), "sub.test003", "Running Game", &staged).is_err());
    }

    #[test]
    fn extracts_only_safe_bundle_entries_from_zip() {
        let tmp = tempfile::tempdir().unwrap();
        let (_game, rel, _orig, modded) = fixture_game(tmp.path());
        let mut buf = std::io::Cursor::new(Vec::new());
        {
            let mut z = zip::ZipWriter::new(&mut buf);
            let opts = zip::write::SimpleFileOptions::default();
            z.start_file("0123456789abcdef0123456789abcdef.bundle", opts).unwrap();
            z.write_all(&modded).unwrap();
            z.finish().unwrap();
        }
        let staged = extract_bundle_entries_from_zip(tmp.path(), buf.get_ref()).unwrap();
        assert_eq!(staged.len(), 1);
        assert_eq!(staged[0].target_rel, rel);

        let mut bad_buf = std::io::Cursor::new(Vec::new());
        {
            let mut z = zip::ZipWriter::new(&mut bad_buf);
            let opts = zip::write::SimpleFileOptions::default();
            z.start_file("evil.dll", opts).unwrap();
            z.write_all(b"mz").unwrap();
            z.finish().unwrap();
        }
        assert!(extract_bundle_entries_from_zip(tmp.path(), bad_buf.get_ref()).is_err());
    }
}
