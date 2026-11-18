//! Drag-and-drop mod archive / bundle / folder inspector, atomic installer, and restorer.
use std::collections::{HashMap, HashSet};
use std::fs;
use std::io::{Read, Write};
use std::path::{Path, PathBuf};

use chrono::Utc;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use tauri::{Manager, State};
use walkdir::WalkDir;

use crate::application::install_service::InstallService;
use crate::domain::error::{HubError, HubResult};
use crate::domain::installation::{Backup, GameInstallation};
use crate::extension_center::{game_root, get_game, ExtensionCenter};
use crate::install::gates::ProcessGate;
use crate::packages::manifest::HubPackageManifestV1;
use crate::packages::parser::PackageParser;
use crate::security::archive_guard::{ArchiveGuard, ArchiveLimits};
use crate::security::hash::compute_file_sha256;
use crate::security::path_guard::PathGuard;
use crate::storage::repositories::BackupRepository;

const MAX_DROP_ARCHIVE_BYTES: u64 = 256 * 1024 * 1024;
const MAX_DROP_UNCOMPRESSED_BYTES: u64 = 512 * 1024 * 1024;
const MAX_DROP_PAYLOAD_BYTES: usize = 128 * 1024 * 1024;
const MAX_DROP_ENTRIES: usize = 2000;

const FORBIDDEN_EXTS: &[&str] = &[
    ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jse", ".wsf", ".msi", ".msp",
    ".sh", ".scr", ".com", ".pif", ".lnk",
];

const PREVIEW_EXTS: &[&str] = &[".png", ".jpg", ".jpeg", ".webp", ".gif", ".mp4", ".mov", ".webm"];
const DOC_EXTS: &[&str] = &[".txt", ".md", ".pdf"];

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DroppedModTargetMatch {
    pub source_entry: String,
    pub target_rel: String,
    pub bundle_id: String,
    pub target_kind: String,
    pub size: u64,
    pub sha256: String,
    pub current_sha256: String,
    pub already_same: bool,
    pub is_catalog: bool,
    pub risk_level: String,
    pub conflict_with: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DroppedModUnmatchedFile {
    pub source_entry: String,
    pub reason: String,
    pub size: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DroppedModInspectResult {
    pub mod_id: String,
    pub display_name: String,
    pub source_path: String,
    pub source_kind: String,
    pub installation_id: String,
    pub game_platform: String,
    pub game_root: String,
    pub compatible: bool,
    pub can_install: bool,
    pub already_installed: bool,
    pub has_catalog_bundle: bool,
    pub warnings: Vec<String>,
    pub matched_files: Vec<DroppedModTargetMatch>,
    pub unmatched_files: Vec<DroppedModUnmatchedFile>,
    pub conflicts: Vec<String>,
    pub preview_files: Vec<String>,
    pub doc_files: Vec<String>,
    pub xspkg_manifest: Option<HubPackageManifestV1>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DroppedModInstallResult {
    pub mod_id: String,
    pub display_name: String,
    pub phase: String,
    pub message: String,
    pub installed_count: usize,
    pub backup_id: Option<String>,
    pub transaction_id: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DroppedModRecord {
    pub mod_id: String,
    pub name: String,
    pub source_kind: String,
    pub phase: String,
    pub installed_at: String,
    pub file_count: usize,
    pub targets: Vec<String>,
    pub has_catalog: bool,
    pub can_restore: bool,
    pub transaction_id: Option<String>,
    pub backup_id: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct DroppedModReceiptFile {
    index: usize,
    target: String,
    mod_sha256: String,
    baseline_sha256: String,
    #[serde(default)]
    is_catalog: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct DroppedModReceipt {
    mod_id: String,
    name: String,
    source_kind: String,
    phase: String,
    installed_at: String,
    transaction_id: Option<String>,
    backup_id: Option<String>,
    files: Vec<DroppedModReceiptFile>,
}

struct DiscoveredTargets {
    /// Lowercase 32-hex bundle id -> list of `(relative_path_under_root, target_kind)`
    by_hex: HashMap<String, Vec<(String, String)>>,
    /// Relative path under `StreamingAssets/aa/` (lowercased) -> `(relative_path_under_root, target_kind)`
    by_aa_rel: HashMap<String, (String, String)>,
    /// Exact `<DataDir>/StreamingAssets/aa/catalog.bundle` targets
    catalog_targets: Vec<String>,
    /// Protected XingShu runtime bundle ID for the selected channel
    protected_runtime_bundle_id: Option<String>,
}

struct StagedCandidate {
    target_rel: String,
    is_catalog: bool,
    bytes: Vec<u8>,
    sha256: String,
}

fn sha256_hex(data: &[u8]) -> String {
    hex::encode(Sha256::digest(data))
}

fn is_hex32(s: &str) -> bool {
    s.len() == 32 && s.bytes().all(|b| b.is_ascii_hexdigit())
}

fn sanitize_mod_id(mod_id: &str) -> HubResult<String> {
    let trimmed = mod_id.trim();
    let bytes = trimmed.as_bytes();
    if !(3..=64).contains(&trimmed.len())
        || !bytes[0].is_ascii_alphanumeric()
        || !bytes
            .iter()
            .all(|b| b.is_ascii_alphanumeric() || matches!(b, b'.' | b'_' | b'-'))
        || trimmed.contains("..")
    {
        return Err(HubError::security_violation("无效的模组标识。"));
    }
    Ok(trimmed.to_string())
}

fn confined(root: &Path, relative: &str) -> HubResult<PathBuf> {
    let clean = PathGuard::sanitize_relative_path(relative)?;
    PathGuard::ensure_within_root(root, &root.join(clean))
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

fn receipt_path(root: &Path, mod_id: &str) -> HubResult<PathBuf> {
    let clean_id = sanitize_mod_id(mod_id)?;
    confined(root, &format!(".xingshu-mods/dropped-receipts/{clean_id}.json"))
}

fn backup_file_path(root: &Path, mod_id: &str, index: usize) -> HubResult<PathBuf> {
    let clean_id = sanitize_mod_id(mod_id)?;
    confined(
        root,
        &format!(".xingshu-mods/dropped-backups/{clean_id}/baseline/{index}.bundle"),
    )
}

fn read_receipt(root: &Path, mod_id: &str) -> HubResult<Option<DroppedModReceipt>> {
    let path = receipt_path(root, mod_id)?;
    if !path.is_file() {
        return Ok(None);
    }
    let raw = fs::read(&path).map_err(|e| HubError::storage_error(e.to_string()))?;
    let receipt: DroppedModReceipt =
        serde_json::from_slice(&raw).map_err(|e| HubError::storage_error(e.to_string()))?;
    if receipt.mod_id != mod_id
        || !matches!(receipt.phase.as_str(), "prepared" | "installed" | "restored")
    {
        return Err(HubError::security_violation("本地模组收据校验无效。"));
    }
    Ok(Some(receipt))
}

fn save_receipt(root: &Path, receipt: &DroppedModReceipt) -> HubResult<()> {
    let path = receipt_path(root, &receipt.mod_id)?;
    let bytes = serde_json::to_vec_pretty(receipt).map_err(|e| HubError::storage_error(e.to_string()))?;
    atomic_write(&path, &bytes)
}

fn validate_dropped_target_rel(root: &Path, target_rel: &str) -> HubResult<(String, PathBuf)> {
    let clean = target_rel.trim().replace('\\', "/");
    let parts: Vec<&str> = clean.split('/').collect();
    let valid_shape = parts.len() >= 4
        && parts[0].ends_with("_Data")
        && ((parts[1] == "StreamingAssets"
            && parts[2] == "aa"
            && parts.last().is_some_and(|f| f.ends_with(".bundle")))
            || (parts[1] == "com.unity.addressables"
                && parts[2] == "AssetBundles"
                && parts.last() == Some(&"__data")));
    if !valid_shape {
        return Err(HubError::security_violation(format!(
            "安全策略拒绝：目标路径 '{clean}' 不在游戏 Addressables / StreamingAssets 资源目录内。"
        )));
    }
    let resolved = confined(root, &clean)?;
    Ok((clean, resolved))
}

fn find_data_dirs(game: &GameInstallation, root: &Path) -> Vec<PathBuf> {
    let mut dirs = Vec::new();
    let mut seen = HashSet::new();

    if let Some(exe) = game.executable_path.as_deref() {
        if let Some(stem) = Path::new(exe).file_stem().and_then(|s| s.to_str()) {
            let preferred = root.join(format!("{stem}_Data"));
            if preferred.is_dir() && seen.insert(preferred.clone()) {
                dirs.push(preferred);
            }
        }
    }

    if let Ok(entries) = fs::read_dir(root) {
        for entry in entries.flatten() {
            let p = entry.path();
            if p.is_dir()
                && p.file_name().is_some_and(|n| n.to_string_lossy().ends_with("_Data"))
                && (p.join("StreamingAssets").is_dir() || p.join("com.unity.addressables").is_dir())
                && seen.insert(p.clone())
            {
                dirs.push(p);
            }
        }
    }

    dirs
}

fn runtime_bundle_id_for_platform(platform: &str) -> Option<String> {
    let runtimes: Vec<serde_json::Value> =
        serde_json::from_str(include_str!("runtime_inventory.generated.json")).unwrap_or_default();
    runtimes
        .iter()
        .find(|r| r["platform"].as_str() == Some(platform))
        .and_then(|r| {
            r["bundleId"]
                .as_str()
                .map(|s| s.to_ascii_lowercase())
                .or_else(|| {
                    r["bundleRelativePath"].as_str().and_then(|rel| {
                        Path::new(rel)
                            .parent()
                            .and_then(|p| p.file_name())
                            .and_then(|n| n.to_str())
                            .filter(|s| is_hex32(s))
                            .map(|s| s.to_ascii_lowercase())
                    })
                })
        })
}

fn discover_game_targets(game: &GameInstallation, root: &Path) -> HubResult<DiscoveredTargets> {
    let mut by_hex: HashMap<String, Vec<(String, String)>> = HashMap::new();
    let mut by_aa_rel: HashMap<String, (String, String)> = HashMap::new();
    let mut catalog_targets = Vec::new();

    let canonical_root = dunce::canonicalize(root).unwrap_or_else(|_| root.to_path_buf());
    let to_rel = |abs: &Path| -> Option<String> {
        let canonical = dunce::canonicalize(abs).ok()?;
        canonical
            .strip_prefix(&canonical_root)
            .ok()
            .map(|p| p.to_string_lossy().replace('\\', "/"))
    };

    for data_dir in find_data_dirs(game, root) {
        let aa_dir = data_dir.join("StreamingAssets").join("aa");
        if aa_dir.is_dir() {
            let catalog_file = aa_dir.join("catalog.bundle");
            if catalog_file.is_file() {
                if let Some(rel) = to_rel(&catalog_file) {
                    catalog_targets.push(rel);
                }
            }

            for entry in WalkDir::new(&aa_dir)
                .follow_links(false)
                .max_depth(4)
                .into_iter()
                .flatten()
            {
                if !entry.file_type().is_file() {
                    continue;
                }
                let path = entry.path();
                if path.extension().and_then(|e| e.to_str()).is_some_and(|e| e.eq_ignore_ascii_case("bundle")) {
                    let fname = path.file_name().and_then(|s| s.to_str()).unwrap_or("");
                    if fname.eq_ignore_ascii_case("catalog.bundle") {
                        continue;
                    }
                    if let Some(rel) = to_rel(path) {
                        if let Ok(aa_sub) = path.strip_prefix(&aa_dir) {
                            by_aa_rel.insert(
                                aa_sub.to_string_lossy().replace('\\', "/").to_ascii_lowercase(),
                                (rel.clone(), "streaming_assets".into()),
                            );
                        }
                        if let Some(stem) = path.file_stem().and_then(|s| s.to_str()) {
                            if is_hex32(stem) {
                                by_hex
                                    .entry(stem.to_ascii_lowercase())
                                    .or_default()
                                    .push((rel, "streaming_assets".into()));
                            }
                        }
                    }
                }
            }
        }

        let addr_dir = data_dir.join("com.unity.addressables").join("AssetBundles");
        if addr_dir.is_dir() {
            for entry in WalkDir::new(&addr_dir)
                .follow_links(false)
                .max_depth(5)
                .into_iter()
                .flatten()
            {
                if !entry.file_type().is_file() {
                    continue;
                }
                let path = entry.path();
                if path.file_name().and_then(|s| s.to_str()) == Some("__data") {
                    if let Some(parent_hex) = path
                        .parent()
                        .and_then(|p| p.file_name())
                        .and_then(|s| s.to_str())
                        .filter(|s| is_hex32(s))
                    {
                        if let Some(rel) = to_rel(path) {
                            by_hex
                                .entry(parent_hex.to_ascii_lowercase())
                                .or_default()
                                .push((rel, "addressables".into()));
                        }
                    }
                }
            }
        }
    }

    for targets in by_hex.values_mut() {
        targets.sort();
        targets.dedup();
    }

    Ok(DiscoveredTargets {
        by_hex,
        by_aa_rel,
        catalog_targets,
        protected_runtime_bundle_id: runtime_bundle_id_for_platform(&game.platform),
    })
}

fn collect_active_target_owners(root: &Path, current_mod_id: &str) -> HubResult<HashMap<String, String>> {
    let mut owners: HashMap<String, String> = HashMap::new();

    // 1. Dropped mod receipts
    let drop_dir = confined(root, ".xingshu-mods/dropped-receipts")?;
    if drop_dir.is_dir() {
        if let Ok(entries) = fs::read_dir(&drop_dir) {
            for entry in entries.flatten() {
                let p = entry.path();
                if p.extension().and_then(|s| s.to_str()) != Some("json") {
                    continue;
                }
                if let Ok(raw) = fs::read(&p) {
                    if let Ok(r) = serde_json::from_slice::<DroppedModReceipt>(&raw) {
                        if r.mod_id != current_mod_id && matches!(r.phase.as_str(), "installed" | "prepared") {
                            for f in r.files {
                                owners.insert(f.target.replace('\\', "/"), format!("已安装拖入模组「{}」", r.name));
                            }
                        }
                    }
                }
            }
        }
    }

    // 2. Workshop receipts
    let ws_dir = confined(root, ".xingshu-mods/workshop-receipts")?;
    if ws_dir.is_dir() {
        if let Ok(entries) = fs::read_dir(&ws_dir) {
            for entry in entries.flatten() {
                let p = entry.path();
                if p.extension().and_then(|s| s.to_str()) != Some("json") {
                    continue;
                }
                if let Ok(raw) = fs::read(&p) {
                    if let Ok(val) = serde_json::from_slice::<serde_json::Value>(&raw) {
                        let phase = val["phase"].as_str().unwrap_or("");
                        let name = val["name"].as_str().unwrap_or("工坊模组");
                        if matches!(phase, "installed" | "prepared") {
                            if let Some(files) = val["files"].as_array() {
                                for f in files {
                                    if let Some(target) = f["target"].as_str() {
                                        owners.insert(target.replace('\\', "/"), format!("工坊模组「{name}」"));
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    // 3. Built-in ModLibrary receipts
    let legacy_dir = confined(root, ".xingshu-mods/receipts")?;
    if legacy_dir.is_dir() {
        if let Ok(catalog) = serde_json::from_str::<serde_json::Value>(include_str!("mod_library.generated.json")) {
            if let Ok(entries) = fs::read_dir(&legacy_dir) {
                for entry in entries.flatten() {
                    let rpath = entry.path().join("receipt.json");
                    if !rpath.is_file() {
                        continue;
                    }
                    if let Ok(raw) = fs::read(&rpath) {
                        if let Ok(val) = serde_json::from_slice::<serde_json::Value>(&raw) {
                            let phase = val["phase"].as_str().unwrap_or("");
                            let item_id = val["itemId"].as_str().unwrap_or("");
                            if matches!(phase, "installed" | "prepared") && !item_id.is_empty() {
                                if let Some(item) = catalog["items"].as_array().and_then(|arr| arr.iter().find(|i| i["id"].as_str() == Some(item_id))) {
                                    let name = item["name"].as_str().unwrap_or(item_id);
                                    if let Some(files) = item["files"].as_array() {
                                        for f in files {
                                            if let Some(target) = f["target"].as_str() {
                                                owners.insert(target.replace('\\', "/"), format!("内置模组库「{name}」"));
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    Ok(owners)
}

fn check_forbidden_extension(lower_name: &str) -> HubResult<()> {
    if FORBIDDEN_EXTS.iter().any(|ext| lower_name.ends_with(ext)) {
        return Err(HubError::security_violation(format!(
            "安全限制：拒绝包含可执行程序或脚本的文件（{lower_name}）。"
        )));
    }
    Ok(())
}

fn derive_mod_id(source_path: &Path) -> String {
    let canonical = dunce::canonicalize(source_path).unwrap_or_else(|_| source_path.to_path_buf());
    let name_key = canonical
        .file_name()
        .and_then(|s| s.to_str())
        .unwrap_or("dropped-mod")
        .to_lowercase();
    let digest = sha256_hex(name_key.as_bytes());
    format!("drop.{}", &digest[..16])
}

struct RawPayloadEntry {
    source_entry: String,
    bytes: Vec<u8>,
}

fn read_source_entries(source_path: &Path) -> HubResult<(String, String, Vec<RawPayloadEntry>, Vec<String>, Vec<String>)> {
    if !source_path.exists() {
        return Err(HubError::invalid_manifest("拖入的文件或文件夹不存在。"));
    }
    if fs::symlink_metadata(source_path)
        .map_err(|e| HubError::storage_error(e.to_string()))?
        .file_type()
        .is_symlink()
    {
        return Err(HubError::security_violation("安全限制：不支持符号链接文件或目录。"));
    }

    let default_name = source_path
        .file_stem()
        .or_else(|| source_path.file_name())
        .and_then(|s| s.to_str())
        .unwrap_or("本地模组")
        .to_string();

    let mut payloads = Vec::new();
    let mut preview_files = Vec::new();
    let mut doc_files = Vec::new();

    if source_path.is_dir() {
        let canonical_src = dunce::canonicalize(source_path).map_err(|e| HubError::storage_error(e.to_string()))?;
        let mut total_bytes: u64 = 0;
        let mut entry_count: usize = 0;
        for entry in WalkDir::new(&canonical_src).follow_links(false).into_iter() {
            let entry = entry.map_err(|e| HubError::storage_error(e.to_string()))?;
            if entry.file_type().is_symlink() {
                return Err(HubError::security_violation("安全限制：拖入文件夹中禁止包含符号链接。"));
            }
            if !entry.file_type().is_file() {
                continue;
            }
            entry_count += 1;
            if entry_count > MAX_DROP_ENTRIES {
                return Err(HubError::security_violation("拖入文件夹包含文件数量超过安全上限（2000）。"));
            }
            let rel = entry
                .path()
                .strip_prefix(&canonical_src)
                .map_err(|e| HubError::security_violation(e.to_string()))?
                .to_string_lossy()
                .replace('\\', "/");
            let clean = PathGuard::sanitize_relative_path(&rel)?
                .to_string_lossy()
                .replace('\\', "/");
            let lower = clean.to_ascii_lowercase();
            check_forbidden_extension(&lower)?;

            let meta = entry.metadata().map_err(|e| HubError::storage_error(e.to_string()))?;
            total_bytes = total_bytes.saturating_add(meta.len());
            if total_bytes > MAX_DROP_UNCOMPRESSED_BYTES {
                return Err(HubError::security_violation("拖入文件夹总体积超过 512MB 安全上限。"));
            }

            let file_name = Path::new(&lower).file_name().and_then(|s| s.to_str()).unwrap_or("");
            if lower.ends_with(".bundle") || file_name == "__data" {
                if meta.len() == 0 || meta.len() > MAX_DROP_PAYLOAD_BYTES as u64 {
                    return Err(HubError::security_violation(format!(
                        "Bundle 文件体积异常（{clean}）。"
                    )));
                }
                let data = fs::read(entry.path()).map_err(|e| HubError::storage_error(e.to_string()))?;
                payloads.push(RawPayloadEntry { source_entry: clean, bytes: data });
            } else if PREVIEW_EXTS.iter().any(|ext| lower.ends_with(ext)) {
                preview_files.push(clean);
            } else if DOC_EXTS.iter().any(|ext| lower.ends_with(ext)) {
                doc_files.push(clean);
            }
        }
        return Ok(("folder".into(), default_name, payloads, preview_files, doc_files));
    }

    let file_name = source_path
        .file_name()
        .and_then(|s| s.to_str())
        .unwrap_or("")
        .to_string();
    let lower_name = file_name.to_ascii_lowercase();
    check_forbidden_extension(&lower_name)?;

    let meta = fs::metadata(source_path).map_err(|e| HubError::storage_error(e.to_string()))?;
    if meta.len() == 0 || meta.len() > MAX_DROP_ARCHIVE_BYTES {
        return Err(HubError::security_violation("拖入文件为空或超过 256MB 安全上限。"));
    }

    if lower_name.ends_with(".bundle") {
        let data = fs::read(source_path).map_err(|e| HubError::storage_error(e.to_string()))?;
        payloads.push(RawPayloadEntry { source_entry: file_name, bytes: data });
        return Ok(("single_bundle".into(), default_name, payloads, preview_files, doc_files));
    }

    if !(lower_name.ends_with(".zip") || lower_name.ends_with(".xspkg") || lower_name.ends_with(".apmm")) {
        return Err(HubError::invalid_manifest(
            "不支持的文件格式；请拖入 .zip、.xspkg、.apmm、.bundle 或解压后的模组文件夹。",
        ));
    }

    ArchiveGuard::inspect_zip(
        source_path,
        &ArchiveLimits {
            max_file_count: MAX_DROP_ENTRIES,
            max_total_uncompressed_bytes: MAX_DROP_UNCOMPRESSED_BYTES,
            max_single_file_uncompressed_bytes: MAX_DROP_PAYLOAD_BYTES as u64,
            max_compression_ratio: 50.0,
        },
    )?;

    let file = fs::File::open(source_path).map_err(|e| HubError::storage_error(e.to_string()))?;
    let mut zf = zip::ZipArchive::new(file)
        .map_err(|_| HubError::invalid_manifest("压缩包格式无效或已损坏（若为 RAR/7z，请先解压后拖入文件夹或重新打包为 ZIP）。"))?;
    if zf.is_empty() || zf.len() > MAX_DROP_ENTRIES {
        return Err(HubError::security_violation("压缩包条目数量异常。"));
    }

    let mut display_name = default_name;
    if let Ok(mut modinfo) = zf.by_name("modinfo.json") {
        let mut s = String::new();
        if modinfo.read_to_string(&mut s).is_ok() {
            if let Ok(val) = serde_json::from_str::<serde_json::Value>(&s) {
                if let Some(n) = val["name"].as_str().filter(|s| !s.trim().is_empty()) {
                    display_name = n.trim().to_string();
                }
            }
        }
    }

    let mut total_uncompressed: u64 = 0;
    for idx in 0..zf.len() {
        let mut entry = zf
            .by_index(idx)
            .map_err(|e| HubError::invalid_manifest(format!("读取压缩包条目失败：{e}")))?;
        if entry.is_symlink() {
            return Err(HubError::security_violation("安全限制：压缩包内禁止包含符号链接。"));
        }
        let raw_name = entry.name().replace('\\', "/");
        if entry.is_dir() {
            let _ = PathGuard::sanitize_relative_path(&raw_name)?;
            continue;
        }
        total_uncompressed = total_uncompressed.saturating_add(entry.size());
        if total_uncompressed > MAX_DROP_UNCOMPRESSED_BYTES {
            return Err(HubError::security_violation("压缩包解压总体积超过 512MB 安全上限。"));
        }
        let clean = PathGuard::sanitize_relative_path(&raw_name)?
            .to_string_lossy()
            .replace('\\', "/");
        let lower = clean.to_ascii_lowercase();
        check_forbidden_extension(&lower)?;

        let entry_fname = Path::new(&lower).file_name().and_then(|s| s.to_str()).unwrap_or("");
        if lower.ends_with(".bundle") || entry_fname == "__data" {
            let mut data = Vec::new();
            entry
                .by_ref()
                .take((MAX_DROP_PAYLOAD_BYTES + 1) as u64)
                .read_to_end(&mut data)
                .map_err(|e| HubError::storage_error(e.to_string()))?;
            if data.is_empty() || data.len() > MAX_DROP_PAYLOAD_BYTES {
                return Err(HubError::security_violation(format!(
                    "压缩包内 Bundle 文件体积异常（{clean}）。"
                )));
            }
            payloads.push(RawPayloadEntry { source_entry: clean, bytes: data });
        } else if PREVIEW_EXTS.iter().any(|ext| lower.ends_with(ext)) {
            preview_files.push(clean);
        } else if DOC_EXTS.iter().any(|ext| lower.ends_with(ext)) {
            doc_files.push(clean);
        }
    }

    let source_kind = if lower_name.ends_with(".apmm") {
        "apmm"
    } else {
        "community_zip"
    };
    Ok((source_kind.into(), display_name, payloads, preview_files, doc_files))
}

fn is_xspkg_archive(source_path: &Path) -> HubResult<bool> {
    if !source_path.is_file() {
        return Ok(false);
    }
    let lower = source_path
        .file_name()
        .and_then(|s| s.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    if !(lower.ends_with(".xspkg") || lower.ends_with(".zip")) {
        return Ok(false);
    }
    let file = fs::File::open(source_path).map_err(|e| HubError::storage_error(e.to_string()))?;
    let mut zf = match zip::ZipArchive::new(file) {
        Ok(z) => z,
        Err(_) => return Ok(false),
    };
    let has_manifest = zf.by_name("manifest.json").is_ok();
    Ok(has_manifest)
}

fn inspect_against_game(
    game: &GameInstallation,
    installation_id: &str,
    root: &Path,
    source_path: &Path,
) -> HubResult<(DroppedModInspectResult, Vec<StagedCandidate>)> {
    let mod_id = derive_mod_id(source_path);
    let existing_receipt = read_receipt(root, &mod_id)?;
    let already_installed = existing_receipt
        .as_ref()
        .is_some_and(|r| matches!(r.phase.as_str(), "installed" | "prepared"));

    if is_xspkg_archive(source_path)? {
        let manifest = PackageParser::parse_archive(source_path)?;
        let compat_res = crate::install::engine::InstallEngine::check_compatibility(&manifest, game);
        let compatible = compat_res.is_ok();
        let mut warnings = Vec::new();
        if let Err(err) = compat_res {
            warnings.push(err.message);
        }
        let matched_files: Vec<DroppedModTargetMatch> = manifest
            .files
            .iter()
            .map(|f| DroppedModTargetMatch {
                source_entry: f.source.clone(),
                target_rel: f.target.clone(),
                bundle_id: "xspkg".into(),
                target_kind: "xspkg".into(),
                size: f.size,
                sha256: f.sha256.clone(),
                current_sha256: String::new(),
                already_same: false,
                is_catalog: false,
                risk_level: "safe".into(),
                conflict_with: None,
            })
            .collect();
        let display_name = manifest.package.name.clone();
        let res = DroppedModInspectResult {
            mod_id,
            display_name,
            source_path: source_path.display().to_string(),
            source_kind: "xspkg".into(),
            installation_id: installation_id.to_string(),
            game_platform: game.platform.clone(),
            game_root: root.display().to_string(),
            compatible,
            can_install: compatible && !matched_files.is_empty() && !already_installed,
            already_installed,
            has_catalog_bundle: false,
            warnings,
            matched_files,
            unmatched_files: Vec::new(),
            conflicts: Vec::new(),
            preview_files: Vec::new(),
            doc_files: Vec::new(),
            xspkg_manifest: Some(manifest),
        };
        return Ok((res, Vec::new()));
    }

    let (source_kind, display_name, raw_payloads, preview_files, doc_files) =
        read_source_entries(source_path)?;
    let discovered = discover_game_targets(game, root)?;
    let active_owners = collect_active_target_owners(root, &mod_id)?;

    let mut matched_files = Vec::new();
    let mut unmatched_files = Vec::new();
    let mut staged_candidates = Vec::new();
    let mut conflicts = Vec::new();
    let mut warnings = Vec::new();
    let mut seen_targets = HashSet::new();
    let mut has_catalog_bundle = false;

    for raw in raw_payloads {
        let entry_lower = raw.source_entry.to_ascii_lowercase();
        let path_obj = Path::new(&raw.source_entry);
        let file_name = path_obj.file_name().and_then(|s| s.to_str()).unwrap_or("");
        let file_lower = file_name.to_ascii_lowercase();

        let mut candidate_targets: Vec<(String, String, String, bool)> = Vec::new();

        if file_lower == "catalog.bundle" {
            has_catalog_bundle = true;
            if discovered.catalog_targets.is_empty() {
                unmatched_files.push(DroppedModUnmatchedFile {
                    source_entry: raw.source_entry,
                    reason: "当前游戏目录未发现 StreamingAssets/aa/catalog.bundle".into(),
                    size: raw.bytes.len() as u64,
                });
                continue;
            }
            for target_rel in &discovered.catalog_targets {
                candidate_targets.push(("catalog".into(), target_rel.clone(), "catalog".into(), true));
            }
        } else if file_lower == "__data" {
            let parent_hex = path_obj
                .parent()
                .and_then(|p| p.file_name())
                .and_then(|s| s.to_str())
                .filter(|s| is_hex32(s))
                .map(|s| s.to_ascii_lowercase());
            if let Some(hex_id) = parent_hex {
                if let Some(targets) = discovered.by_hex.get(&hex_id) {
                    for (t_rel, t_kind) in targets {
                        if t_kind == "addressables" || targets.len() == 1 {
                            candidate_targets.push((hex_id.clone(), t_rel.clone(), t_kind.clone(), false));
                        }
                    }
                } else {
                    unmatched_files.push(DroppedModUnmatchedFile {
                        source_entry: raw.source_entry,
                        reason: format!("当前游戏目录中未找到 Addressables 容器 {hex_id}"),
                        size: raw.bytes.len() as u64,
                    });
                    continue;
                }
            } else {
                unmatched_files.push(DroppedModUnmatchedFile {
                    source_entry: raw.source_entry,
                    reason: "无法识别 __data 上级 32 位 Bundle Hash 目录名".into(),
                    size: raw.bytes.len() as u64,
                });
                continue;
            }
        } else if let Some(stem) = path_obj.file_stem().and_then(|s| s.to_str()).filter(|s| is_hex32(s)) {
            let hex_id = stem.to_ascii_lowercase();
            if let Some(targets) = discovered.by_hex.get(&hex_id) {
                for (t_rel, t_kind) in targets {
                    candidate_targets.push((hex_id.clone(), t_rel.clone(), t_kind.clone(), false));
                }
            } else {
                unmatched_files.push(DroppedModUnmatchedFile {
                    source_entry: raw.source_entry,
                    reason: format!("当前游戏目录未找到对应 Bundle（{hex_id}.bundle），可能版本或渠道不一致"),
                    size: raw.bytes.len() as u64,
                });
                continue;
            }
        } else if let Some((_, aa_rel)) = entry_lower.split_once("streamingassets/aa/") {
            if let Some((t_rel, t_kind)) = discovered.by_aa_rel.get(aa_rel) {
                candidate_targets.push((file_name.into(), t_rel.clone(), t_kind.clone(), false));
            } else {
                unmatched_files.push(DroppedModUnmatchedFile {
                    source_entry: raw.source_entry,
                    reason: "StreamingAssets/aa 下不存在该名称的原始资源".into(),
                    size: raw.bytes.len() as u64,
                });
                continue;
            }
        } else {
            unmatched_files.push(DroppedModUnmatchedFile {
                source_entry: raw.source_entry,
                reason: "非 32 位 Hash Bundle 文件，且未指定 StreamingAssets/aa 路径".into(),
                size: raw.bytes.len() as u64,
            });
            continue;
        }

        let payload_sha256 = sha256_hex(&raw.bytes);
        let payload_size = raw.bytes.len() as u64;

        for (bundle_id, target_rel, target_kind, is_catalog) in candidate_targets {
            if !seen_targets.insert(target_rel.clone()) {
                warnings.push(format!("模组包内存在重复指向 {target_rel} 的文件，已忽略后续重复条目。"));
                continue;
            }
            let (norm_target, target_abs) = validate_dropped_target_rel(root, &target_rel)?;
            let current_sha256 = if target_abs.is_file() {
                compute_file_sha256(&target_abs)?
            } else {
                String::new()
            };
            let already_same = !current_sha256.is_empty() && current_sha256.eq_ignore_ascii_case(&payload_sha256);

            let mut conflict_with = None;
            if discovered
                .protected_runtime_bundle_id
                .as_deref()
                .is_some_and(|rt_id| rt_id.eq_ignore_ascii_case(&bundle_id))
            {
                let msg = "星枢运行时核心容器（受保护，禁止第三方模组覆盖）".to_string();
                conflicts.push(format!("{norm_target}：{msg}"));
                conflict_with = Some(msg);
            } else if let Some(owner) = active_owners.get(&norm_target) {
                conflicts.push(format!("{norm_target} 已被 {owner} 占用，请先还原冲突模组"));
                conflict_with = Some(owner.clone());
            }

            let risk_level = if is_catalog { "high" } else { "safe" };
            matched_files.push(DroppedModTargetMatch {
                source_entry: raw.source_entry.clone(),
                target_rel: norm_target.clone(),
                bundle_id: bundle_id.clone(),
                target_kind: target_kind.clone(),
                size: payload_size,
                sha256: payload_sha256.clone(),
                current_sha256,
                already_same,
                is_catalog,
                risk_level: risk_level.into(),
                conflict_with,
            });
            staged_candidates.push(StagedCandidate {
                target_rel: norm_target,
                is_catalog,
                bytes: raw.bytes.clone(),
                sha256: payload_sha256.clone(),
            });
        }
    }

    if has_catalog_bundle {
        warnings.push(
            "检测到 catalog.bundle（高风险 · 游戏版本敏感）：仅与作者同版本游戏兼容，跨版本覆盖可能导致游戏白屏或闪退。建议优先仅安装非 catalog 的 Bundle，或确认版本一致后再勾选包含 catalog.bundle。"
                .into(),
        );
    }
    if !unmatched_files.is_empty() && matched_files.is_empty() {
        warnings.push("未能在当前所选游戏目录匹配到任何可替换的资源文件，请确认游戏渠道或版本是否一致。".into());
    } else if !unmatched_files.is_empty() {
        warnings.push(format!(
            "有 {} 个 Bundle 未在当前游戏目录匹配到目标（安装时将自动跳过）。",
            unmatched_files.len()
        ));
    }
    if already_installed {
        warnings.push("此模组当前已处于安装状态；如需重新安装请先还原。".into());
    }

    conflicts.sort();
    conflicts.dedup();
    let has_non_catalog_or_any = !matched_files.is_empty();
    let compatible = has_non_catalog_or_any && conflicts.is_empty();
    let can_install = compatible && !already_installed;

    let result = DroppedModInspectResult {
        mod_id,
        display_name,
        source_path: source_path.display().to_string(),
        source_kind,
        installation_id: installation_id.to_string(),
        game_platform: game.platform.clone(),
        game_root: root.display().to_string(),
        compatible,
        can_install,
        already_installed,
        has_catalog_bundle,
        warnings,
        matched_files,
        unmatched_files,
        conflicts,
        preview_files,
        doc_files,
        xspkg_manifest: None,
    };
    Ok((result, staged_candidates))
}

fn restore_bundle_receipt(root: &Path, receipt: &DroppedModReceipt) -> HubResult<()> {
    if receipt.phase == "restored" {
        return Ok(());
    }
    if receipt.files.is_empty() {
        return Err(HubError::storage_error("没有可还原的模组文件记录。"));
    }

    for f in &receipt.files {
        let (_, target_abs) = validate_dropped_target_rel(root, &f.target)?;
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
        let (_, target_abs) = validate_dropped_target_rel(root, &f.target)?;
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
    save_receipt(root, &updated)
}

pub(crate) fn install_dropped_from_path(
    game: &GameInstallation,
    root: &Path,
    source_path: &Path,
    include_catalog: bool,
    install_service: Option<&InstallService>,
    backup_repo: Option<&BackupRepository>,
) -> HubResult<DroppedModInstallResult> {
    game_closed(game)?;
    let (inspect, candidates) = inspect_against_game(game, &game.id, root, source_path)?;

    if let Some(existing) = read_receipt(root, &inspect.mod_id)? {
        if existing.phase != "restored" {
            return Err(HubError::invalid_manifest("该模组当前已安装或处于未完成状态，请先还原。"));
        }
    }

    if inspect.source_kind == "xspkg" {
        let svc = install_service.ok_or_else(|| HubError::storage_error("安装服务未就绪。"))?;
        let _lock = operation_lock(root)?;
        let tx = svc.install_package_from_zip(source_path, &game.id)?;
        let receipt = DroppedModReceipt {
            mod_id: inspect.mod_id.clone(),
            name: inspect.display_name.clone(),
            source_kind: "xspkg".into(),
            phase: "installed".into(),
            installed_at: Utc::now().to_rfc3339(),
            transaction_id: Some(tx.id.clone()),
            backup_id: tx.backup_id.clone(),
            files: Vec::new(),
        };
        save_receipt(root, &receipt)?;
        return Ok(DroppedModInstallResult {
            mod_id: receipt.mod_id,
            display_name: receipt.name,
            phase: receipt.phase,
            message: "已完成 .xspkg 扩展包安全校验与事务安装。".into(),
            installed_count: inspect.matched_files.len(),
            backup_id: tx.backup_id,
            transaction_id: Some(tx.id),
        });
    }

    let selected: Vec<StagedCandidate> = candidates
        .into_iter()
        .filter(|c| include_catalog || !c.is_catalog)
        .collect();

    if selected.is_empty() {
        return Err(HubError::invalid_manifest(if inspect.has_catalog_bundle && !include_catalog {
            "仅包含 catalog.bundle；如确认与当前游戏版本一致，请勾选「包含 catalog.bundle」后再安装。"
        } else {
            "未匹配到可替换的游戏资源文件，无法安装。"
        }));
    }

    // Re-check conflicts strictly for the selected targets.
    for entry in &inspect.matched_files {
        if !include_catalog && entry.is_catalog {
            continue;
        }
        if let Some(ref conflict) = entry.conflict_with {
            return Err(HubError::security_violation(format!(
                "目标 {} 存在冲突：{conflict}",
                entry.target_rel
            )));
        }
    }

    let _lock = operation_lock(root)?;
    game_closed(game)?;

    let mut targets_and_baselines = Vec::with_capacity(selected.len());
    for c in &selected {
        let (_, target_abs) = validate_dropped_target_rel(root, &c.target_rel)?;
        if !target_abs.is_file() {
            return Err(HubError::invalid_manifest(format!(
                "目标资产 {} 不存在，无法安全替换。",
                c.target_rel
            )));
        }
        let baseline_hash = compute_file_sha256(&target_abs)?;
        targets_and_baselines.push((target_abs, baseline_hash));
    }

    let mut receipt_files = Vec::with_capacity(selected.len());
    for (idx, ((target_abs, baseline_hash), entry)) in
        targets_and_baselines.iter().zip(selected.iter()).enumerate()
    {
        let backup_dest = backup_file_path(root, &inspect.mod_id, idx)?;
        let baseline_bytes = fs::read(target_abs).map_err(|e| HubError::storage_error(e.to_string()))?;
        if sha256_hex(&baseline_bytes) != *baseline_hash {
            return Err(HubError::security_violation("备份期间原游戏文件发生变化，已终止。"));
        }
        atomic_write(&backup_dest, &baseline_bytes)?;
        if compute_file_sha256(&backup_dest)? != *baseline_hash {
            return Err(HubError::security_violation("安装前备份校验失败。"));
        }
        receipt_files.push(DroppedModReceiptFile {
            index: idx,
            target: entry.target_rel.clone(),
            mod_sha256: entry.sha256.clone(),
            baseline_sha256: baseline_hash.clone(),
            is_catalog: entry.is_catalog,
        });
    }

    let backup_id = uuid::Uuid::new_v4().to_string();
    let backup_storage = confined(root, &format!(".xingshu-mods/dropped-backups/{}/baseline", inspect.mod_id))?;
    let mut receipt = DroppedModReceipt {
        mod_id: inspect.mod_id.clone(),
        name: inspect.display_name.clone(),
        source_kind: inspect.source_kind.clone(),
        phase: "prepared".into(),
        installed_at: Utc::now().to_rfc3339(),
        transaction_id: None,
        backup_id: Some(backup_id.clone()),
        files: receipt_files,
    };

    save_receipt(root, &receipt)?;

    let commit = (|| -> HubResult<()> {
        for (idx, ((target_abs, _), entry)) in
            targets_and_baselines.iter().zip(selected.iter()).enumerate()
        {
            atomic_write(target_abs, &entry.bytes)?;
            let post_hash = compute_file_sha256(target_abs)?;
            if !post_hash.eq_ignore_ascii_case(&receipt.files[idx].mod_sha256) {
                return Err(HubError::security_violation("写入后的模组文件哈希校验失败。"));
            }
        }
        receipt.phase = "installed".into();
        save_receipt(root, &receipt)
    })();

    if let Err(err) = commit {
        return match restore_bundle_receipt(root, &receipt) {
            Ok(()) => Err(err),
            Err(rollback_err) => Err(HubError::storage_error(format!(
                "{err}；自动回滚未完成：{rollback_err}。已保留备份，请重试还原。"
            ))),
        };
    }

    if let Some(repo) = backup_repo {
        let now = Utc::now();
        let backup_record = Backup {
            id: backup_id.clone(),
            backup_type: "DROPPED_MOD_PRE_INSTALL".into(),
            target_id: Some(game.id.clone()),
            storage_path: backup_storage.display().to_string(),
            manifest_json: serde_json::json!({
                "modId": receipt.mod_id,
                "name": receipt.name,
                "sourceKind": receipt.source_kind,
                "files": receipt.files.iter().map(|f| serde_json::json!({
                    "target": f.target,
                    "baselineSha256": f.baseline_sha256,
                    "modSha256": f.mod_sha256,
                })).collect::<Vec<_>>()
            }),
            created_at: now,
            verified_at: Some(now),
            status: "VERIFIED".into(),
        };
        let _ = repo.save(&backup_record);
    }

    Ok(DroppedModInstallResult {
        mod_id: receipt.mod_id,
        display_name: receipt.name.clone(),
        phase: receipt.phase,
        message: format!(
            "已自动备份原版文件并成功安装「{}」（共替换 {} 个资源）。",
            receipt.name,
            receipt.files.len()
        ),
        installed_count: receipt.files.len(),
        backup_id: receipt.backup_id,
        transaction_id: None,
    })
}

pub(crate) fn restore_dropped_by_id(
    game: &GameInstallation,
    root: &Path,
    mod_id: &str,
    install_service: Option<&InstallService>,
) -> HubResult<String> {
    let clean_id = sanitize_mod_id(mod_id)?;
    game_closed(game)?;
    let _lock = operation_lock(root)?;
    let receipt = read_receipt(root, &clean_id)?
        .ok_or_else(|| HubError::storage_error("未找到该拖入模组的安装备份记录。"))?;

    if receipt.source_kind == "xspkg" {
        let tx_id = receipt
            .transaction_id
            .clone()
            .ok_or_else(|| HubError::storage_error("缺少 .xspkg 事务标识，请前往安装事务页回滚。"))?;
        let svc = install_service.ok_or_else(|| HubError::storage_error("安装服务未就绪。"))?;
        svc.rollback_transaction(&tx_id)?;
        let mut updated = receipt;
        updated.phase = "restored".into();
        save_receipt(root, &updated)?;
        return Ok("已通过安装事务回滚 .xspkg 模组，原文件已恢复。".into());
    }

    restore_bundle_receipt(root, &receipt)?;
    Ok(format!("已还原「{}」安装前的原版文件，备份保留。", receipt.name))
}

#[tauri::command]
pub async fn inspect_dropped_mod(
    installation_id: String,
    source_path: String,
    center: State<'_, ExtensionCenter>,
) -> HubResult<DroppedModInspectResult> {
    let game = get_game(&center.db, &installation_id)?;
    let root = game_root(&game)?;
    let trimmed = source_path.trim().trim_matches('"').to_string();
    if trimmed.is_empty() {
        return Err(HubError::invalid_manifest("请提供有效的模组文件或文件夹路径。"));
    }
    tokio::task::spawn_blocking(move || {
        let (res, _) = inspect_against_game(&game, &installation_id, &root, Path::new(&trimmed))?;
        Ok(res)
    })
    .await
    .map_err(|e| HubError::storage_error(e.to_string()))?
}

#[tauri::command]
pub async fn install_dropped_mod(
    installation_id: String,
    source_path: String,
    include_catalog: bool,
    center: State<'_, ExtensionCenter>,
    install_service: State<'_, InstallService>,
) -> HubResult<DroppedModInstallResult> {
    let game = get_game(&center.db, &installation_id)?;
    let root = game_root(&game)?;
    let trimmed = source_path.trim().trim_matches('"').to_string();
    if trimmed.is_empty() {
        return Err(HubError::invalid_manifest("请提供有效的模组文件或文件夹路径。"));
    }
    let svc = install_service.inner();
    let backup_repo = BackupRepository::new(center.db.clone());
    tokio::task::block_in_place(|| {
        install_dropped_from_path(
            &game,
            &root,
            Path::new(&trimmed),
            include_catalog,
            Some(svc),
            Some(&backup_repo),
        )
    })
}

#[tauri::command]
pub async fn restore_dropped_mod(
    installation_id: String,
    mod_id: String,
    center: State<'_, ExtensionCenter>,
    install_service: State<'_, InstallService>,
) -> HubResult<String> {
    let game = get_game(&center.db, &installation_id)?;
    let root = game_root(&game)?;
    let svc = install_service.inner();
    tokio::task::block_in_place(|| restore_dropped_by_id(&game, &root, &mod_id, Some(svc)))
}

#[tauri::command]
pub async fn list_dropped_mods(
    installation_id: String,
    center: State<'_, ExtensionCenter>,
) -> HubResult<Vec<DroppedModRecord>> {
    if installation_id.trim().is_empty() {
        return Ok(Vec::new());
    }
    let game = get_game(&center.db, &installation_id)?;
    let root = game_root(&game)?;
    tokio::task::spawn_blocking(move || {
        let dir = confined(&root, ".xingshu-mods/dropped-receipts")?;
        if !dir.is_dir() {
            return Ok(Vec::new());
        }
        let mut records = Vec::new();
        for entry in fs::read_dir(&dir).map_err(|e| HubError::storage_error(e.to_string()))?.flatten() {
            let p = entry.path();
            if p.extension().and_then(|s| s.to_str()) != Some("json") {
                continue;
            }
            if let Ok(raw) = fs::read(&p) {
                if let Ok(r) = serde_json::from_slice::<DroppedModReceipt>(&raw) {
                    let can_restore = matches!(r.phase.as_str(), "installed" | "prepared");
                    let has_catalog = r.files.iter().any(|f| f.is_catalog);
                    let targets: Vec<String> = r.files.iter().map(|f| f.target.clone()).collect();
                    records.push(DroppedModRecord {
                        mod_id: r.mod_id,
                        name: r.name,
                        source_kind: r.source_kind,
                        phase: r.phase,
                        installed_at: r.installed_at,
                        file_count: targets.len(),
                        targets,
                        has_catalog,
                        can_restore,
                        transaction_id: r.transaction_id,
                        backup_id: r.backup_id,
                    });
                }
            }
        }
        records.sort_by(|a, b| b.installed_at.cmp(&a.installed_at));
        Ok(records)
    })
    .await
    .map_err(|e| HubError::storage_error(e.to_string()))?
}

/// Fallback staging endpoint for HTML `<input type="file">` selection when a full path is unavailable.
#[tauri::command]
pub async fn stage_dropped_mod_bytes(
    file_name: String,
    bytes: Vec<u8>,
    app: tauri::AppHandle,
) -> HubResult<String> {
    if bytes.is_empty() || bytes.len() as u64 > MAX_DROP_ARCHIVE_BYTES {
        return Err(HubError::security_violation("选择的文件为空或超过 256MB 安全上限。"));
    }
    let raw_name = Path::new(&file_name)
        .file_name()
        .and_then(|s| s.to_str())
        .ok_or_else(|| HubError::security_violation("文件名无效。"))?;
    let clean_name = raw_name
        .chars()
        .map(|c| if c.is_control() || matches!(c, '/' | '\\' | ':' | '*' | '?' | '"' | '<' | '>' | '|') { '_' } else { c })
        .collect::<String>();
    let lower = clean_name.to_ascii_lowercase();
    check_forbidden_extension(&lower)?;
    if !(lower.ends_with(".zip") || lower.ends_with(".xspkg") || lower.ends_with(".apmm") || lower.ends_with(".bundle")) {
        return Err(HubError::invalid_manifest("仅支持选择 .zip、.xspkg、.apmm 或 .bundle 文件。"));
    }
    let base_dir = app
        .path()
        .app_data_dir()
        .unwrap_or_else(|_| std::env::temp_dir().join("xingshu-hub"));
    let stage_dir = base_dir.join("staged-drops");
    fs::create_dir_all(&stage_dir).map_err(|e| HubError::storage_error(e.to_string()))?;
    let dest = stage_dir.join(format!("{}-{clean_name}", uuid::Uuid::new_v4()));
    atomic_write(&dest, &bytes)?;
    Ok(dest.display().to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    const TEST_HEX_A: &str = "0123456789abcdef0123456789abcdef";
    const TEST_HEX_B: &str = "fedcba9876543210fedcba9876543210";

    fn fixture_game(root: &Path) -> GameInstallation {
        let exe = root.join("AstralParty_CN.exe");
        fs::write(&exe, b"exe").unwrap();
        fs::write(root.join("GameAssembly.dll"), b"dll").unwrap();

        let aa = root.join("AstralParty_CN_Data/StreamingAssets/aa/StandaloneWindows64");
        fs::create_dir_all(&aa).unwrap();
        fs::write(aa.join(format!("{TEST_HEX_A}.bundle")), b"orig-streaming-a").unwrap();
        fs::write(root.join("AstralParty_CN_Data/StreamingAssets/aa/catalog.bundle"), b"orig-catalog").unwrap();

        let addr = root.join(format!("AstralParty_CN_Data/com.unity.addressables/AssetBundles/abc/{TEST_HEX_B}"));
        fs::create_dir_all(&addr).unwrap();
        fs::write(addr.join("__data"), b"orig-addr-b").unwrap();

        GameInstallation {
            id: "inst-drop-test".into(),
            platform: "STEAM_CN".into(),
            platform_app_id: Some("2622000".into()),
            install_path: root.display().to_string(),
            executable_path: Some(exe.display().to_string()),
            detected_build_id: None,
            detected_version: None,
            detection_method: "TEST".into(),
            is_valid: true,
            is_primary: true,
            last_verified_at: None,
            created_at: Utc::now(),
            updated_at: Utc::now(),
        }
    }

    fn write_zip(path: &Path, entries: &[(&str, &[u8])]) {
        let f = fs::File::create(path).unwrap();
        let mut z = zip::ZipWriter::new(f);
        let opts = zip::write::SimpleFileOptions::default();
        for (name, data) in entries {
            z.start_file(*name, opts).unwrap();
            z.write_all(data).unwrap();
        }
        z.finish().unwrap();
    }

    #[test]
    fn inspects_community_zip_with_nested_folders_catalog_previews_and_unmatched() {
        let tmp = tempfile::tempdir().unwrap();
        let game_dir = tmp.path().join("game");
        fs::create_dir_all(&game_dir).unwrap();
        let game = fixture_game(&game_dir);

        let zip_path = tmp.path().join("哈基米doro替换泳装哈基米移动.zip");
        let unknown_hex = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        write_zip(
            &zip_path,
            &[
                (&format!("游戏根目录/AstralParty_CN_Data/StreamingAssets/aa/StandaloneWindows64/{TEST_HEX_A}.bundle"), b"mod-a"),
                (&format!("Mod本体/{TEST_HEX_B}.bundle"), b"mod-b"),
                ("游戏根目录/AstralParty_CN_Data/StreamingAssets/aa/catalog.bundle", b"mod-catalog"),
                (&format!("Mod本体/{unknown_hex}.bundle"), b"unmatched"),
                ("预览图.png", b"png-bytes"),
                ("安装说明.txt", b"readme"),
            ],
        );

        let (result, _) = inspect_against_game(&game, &game.id, &game_dir, &zip_path).unwrap();
        assert_eq!(result.source_kind, "community_zip");
        assert!(result.has_catalog_bundle);
        assert!(result.compatible);
        assert!(result.can_install);
        assert_eq!(result.matched_files.len(), 3);
        assert_eq!(result.unmatched_files.len(), 1);
        assert_eq!(result.preview_files, vec!["预览图.png"]);
        assert_eq!(result.doc_files, vec!["安装说明.txt"]);
        assert!(result.warnings.iter().any(|w| w.contains("catalog.bundle")));

        let a_match = result.matched_files.iter().find(|m| m.bundle_id == TEST_HEX_A).unwrap();
        assert_eq!(a_match.target_kind, "streaming_assets");
        let b_match = result.matched_files.iter().find(|m| m.bundle_id == TEST_HEX_B).unwrap();
        assert_eq!(b_match.target_kind, "addressables");
        assert!(b_match.target_rel.ends_with("__data"));
        let cat_match = result.matched_files.iter().find(|m| m.is_catalog).unwrap();
        assert_eq!(cat_match.risk_level, "high");
    }

    #[test]
    fn rejects_zip_slip_traversal_and_forbidden_executables() {
        let tmp = tempfile::tempdir().unwrap();
        let game_dir = tmp.path().join("game");
        fs::create_dir_all(&game_dir).unwrap();
        let game = fixture_game(&game_dir);

        let slip_zip = tmp.path().join("slip.zip");
        write_zip(&slip_zip, &[("../evil.bundle", b"evil")]);
        assert!(inspect_against_game(&game, &game.id, &game_dir, &slip_zip).is_err());

        let exe_zip = tmp.path().join("payload.zip");
        write_zip(&exe_zip, &[("helper.dll", b"mz")]);
        assert!(inspect_against_game(&game, &game.id, &game_dir, &exe_zip).is_err());
    }

    #[test]
    fn installs_respecting_catalog_toggle_and_restores_cleanly() {
        let tmp = tempfile::tempdir().unwrap();
        let game_dir = tmp.path().join("game");
        fs::create_dir_all(&game_dir).unwrap();
        let game = fixture_game(&game_dir);

        let zip_path = tmp.path().join("魔审汉娜 替换 凛 原皮.zip");
        write_zip(
            &zip_path,
            &[
                (&format!("Mod/{TEST_HEX_A}.bundle"), b"mod-a-v1"),
                ("Mod/catalog.bundle", b"mod-catalog-v1"),
            ],
        );

        // Install excluding catalog.bundle
        let installed = install_dropped_from_path(&game, &game_dir, &zip_path, false, None, None).unwrap();
        assert_eq!(installed.installed_count, 1);
        assert_eq!(
            fs::read(game_dir.join(format!("AstralParty_CN_Data/StreamingAssets/aa/StandaloneWindows64/{TEST_HEX_A}.bundle"))).unwrap(),
            b"mod-a-v1"
        );
        assert_eq!(
            fs::read(game_dir.join("AstralParty_CN_Data/StreamingAssets/aa/catalog.bundle")).unwrap(),
            b"orig-catalog"
        );

        // Duplicate install while active is rejected
        assert!(install_dropped_from_path(&game, &game_dir, &zip_path, false, None, None).is_err());

        // Conflicting second mod targeting same bundle is detected and blocked
        let conflict_zip = tmp.path().join("conflict.zip");
        write_zip(&conflict_zip, &[(&format!("{TEST_HEX_A}.bundle"), b"mod-a-v2")]);
        let (conflict_inspect, _) = inspect_against_game(&game, &game.id, &game_dir, &conflict_zip).unwrap();
        assert!(!conflict_inspect.can_install);
        assert!(!conflict_inspect.conflicts.is_empty());
        assert!(install_dropped_from_path(&game, &game_dir, &conflict_zip, false, None, None).is_err());

        // Restore restores original bytes
        restore_dropped_by_id(&game, &game_dir, &installed.mod_id, None).unwrap();
        assert_eq!(
            fs::read(game_dir.join(format!("AstralParty_CN_Data/StreamingAssets/aa/StandaloneWindows64/{TEST_HEX_A}.bundle"))).unwrap(),
            b"orig-streaming-a"
        );
    }

    #[test]
    fn inspects_and_installs_extracted_folder_and_blocks_running_game() {
        let tmp = tempfile::tempdir().unwrap();
        let game_dir = tmp.path().join("game");
        fs::create_dir_all(&game_dir).unwrap();
        let mut game = fixture_game(&game_dir);

        let mod_folder = tmp.path().join("extracted-mod");
        fs::create_dir_all(mod_folder.join("游戏根目录")).unwrap();
        fs::write(mod_folder.join(format!("游戏根目录/{TEST_HEX_B}.bundle")), b"folder-mod-b").unwrap();

        let (inspect, _) = inspect_against_game(&game, &game.id, &game_dir, &mod_folder).unwrap();
        assert_eq!(inspect.source_kind, "folder");
        assert_eq!(inspect.matched_files.len(), 1);
        assert!(inspect.can_install);

        // Running process blocks install
        game.executable_path = Some(std::env::current_exe().unwrap().display().to_string());
        assert!(install_dropped_from_path(&game, &game_dir, &mod_folder, false, None, None).is_err());

        // Restore valid exe and install
        game.executable_path = Some(game_dir.join("AstralParty_CN.exe").display().to_string());
        let res = install_dropped_from_path(&game, &game_dir, &mod_folder, false, None, None).unwrap();
        assert_eq!(
            fs::read(game_dir.join(format!("AstralParty_CN_Data/com.unity.addressables/AssetBundles/abc/{TEST_HEX_B}/__data"))).unwrap(),
            b"folder-mod-b"
        );
        restore_dropped_by_id(&game, &game_dir, &res.mod_id, None).unwrap();
        assert_eq!(
            fs::read(game_dir.join(format!("AstralParty_CN_Data/com.unity.addressables/AssetBundles/abc/{TEST_HEX_B}/__data"))).unwrap(),
            b"orig-addr-b"
        );
    }

    #[test]
    fn protects_xingshu_runtime_bundle_from_overwrite() {
        let tmp = tempfile::tempdir().unwrap();
        let game_dir = tmp.path().join("game");
        fs::create_dir_all(&game_dir).unwrap();
        let game = fixture_game(&game_dir);

        let rt_id = runtime_bundle_id_for_platform("STEAM_CN").unwrap();
        let rt_dir = game_dir.join(format!("AstralParty_CN_Data/com.unity.addressables/AssetBundles/57a198703b66364d5420d39c0d251747/{rt_id}"));
        fs::create_dir_all(&rt_dir).unwrap();
        fs::write(rt_dir.join("__data"), b"xingshu-runtime").unwrap();

        let bad_zip = tmp.path().join("overwrite-rt.zip");
        write_zip(&bad_zip, &[(&format!("{rt_id}.bundle"), b"tampered")]);

        let (inspect, _) = inspect_against_game(&game, &game.id, &game_dir, &bad_zip).unwrap();
        assert!(!inspect.can_install);
        assert!(!inspect.conflicts.is_empty());
        assert!(install_dropped_from_path(&game, &game_dir, &bad_zip, false, None, None).is_err());
    }
}
