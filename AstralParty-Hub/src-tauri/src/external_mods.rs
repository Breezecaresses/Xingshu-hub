//! Local third-party mod inventory. Never loads a DLL to identify it.
use crate::{
    desktop_host::State,
    domain::{
        error::{HubError, HubResult},
        installation::GameInstallation,
    },
    extension_center::{game_root, get_game, ExtensionCenter},
    security::{hash::compute_file_sha256 as hash, path_guard::PathGuard},
};
use serde::{Deserialize, Serialize};
use std::{
    collections::{HashMap, HashSet},
    fs,
    io::Write,
    path::{Path, PathBuf},
};

const LEGACY_SPEED: &str = "759cba427e79826aae0b180c4b44ecf2917793aa7b2a620c83226aee10fe1ebd";
const COORDINATED_SPEED: &str = "36e7df0fce281c95dccd237760fdc982fba3d56d25883d55ded86b87a0588e20";

#[derive(Clone, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct Catalog {
    format_version: u32,
    game_version: String,
    game_assembly_sha256: String,
    catalog_sha256: String,
    items: Vec<ModItem>,
}
#[derive(Clone, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct ModItem {
    id: String,
    name: String,
    archive: String,
    archive_sha256: String,
    author: String,
    platforms: Vec<String>,
    files: Vec<ModFile>,
    unmapped_files: Vec<String>,
    conflicts: Vec<String>,
}
#[derive(Clone, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct ModFile {
    source: String,
    target: String,
    sha256: String,
    original_sha256: String,
    size: u64,
    assets: Vec<String>,
}
#[derive(Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct Receipt {
    item_id: String,
    phase: String,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Finding {
    name: String,
    path: String,
    sha256: String,
    level: String,
    description: String,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LibraryEntry {
    id: String,
    name: String,
    author: String,
    archive: String,
    files: Vec<ModFile>,
    unmapped_count: usize,
    conflicts: Vec<String>,
    status: String,
    can_install: bool,
    can_restore: bool,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ModReport {
    game_path: String,
    library_path: Option<String>,
    compatible: bool,
    findings: Vec<Finding>,
    library: Vec<LibraryEntry>,
    notes: Vec<String>,
}

fn catalog() -> HubResult<Catalog> {
    serde_json::from_str(include_str!("mod_library.generated.json"))
        .map_err(|e| HubError::storage_error(e.to_string()))
}
fn library_root() -> Option<PathBuf> {
    std::env::current_exe()
        .ok()?
        .ancestors()
        .skip(1)
        .take(4)
        .map(|p| p.join("ModLibrary"))
        .find(|p| p.join("catalog.json").is_file())
}
fn confined(root: &Path, relative: &str) -> HubResult<PathBuf> {
    let clean = PathGuard::sanitize_relative_path(relative)?;
    PathGuard::ensure_within_root(root, &root.join(clean))
}
fn target(root: &Path, file: &ModFile) -> HubResult<PathBuf> {
    let clean = file.target.replace('\\', "/");
    let base = "AstralParty_CN_Data/StreamingAssets/aa/StandaloneWindows64/";
    let name = clean
        .strip_prefix(base)
        .ok_or_else(|| HubError::security_violation("模组目标不是已核验的纹理目录。"))?;
    let stem = name.strip_suffix(".bundle").unwrap_or_default();
    if stem.len() != 32 || !stem.bytes().all(|c| c.is_ascii_hexdigit()) {
        return Err(HubError::security_violation("非法纹理文件名。"));
    }
    confined(root, &clean)
}
fn state_dir(root: &Path, item: &ModItem) -> HubResult<PathBuf> {
    if item.id.len() != 22
        || !item.id.starts_with("local.")
        || !item.id[6..].bytes().all(|c| c.is_ascii_hexdigit())
    {
        return Err(HubError::security_violation("非法模组标识。"));
    }
    confined(root, &format!(".xingshu-mods/receipts/{}", item.id))
}
fn read_receipt(root: &Path, item: &ModItem) -> HubResult<Option<Receipt>> {
    let path = confined(&state_dir(root, item)?, "receipt.json")?;
    if !path.exists() {
        return Ok(None);
    }
    let data = fs::read(&path).map_err(|e| HubError::storage_error(e.to_string()))?;
    let r: Receipt =
        serde_json::from_slice(&data).map_err(|e| HubError::storage_error(e.to_string()))?;
    if r.item_id != item.id || !["prepared", "installed", "restored"].contains(&r.phase.as_str()) {
        return Err(HubError::security_violation("模组收据无效。"));
    }
    Ok(Some(r))
}
fn atomic_write(path: &Path, data: &[u8]) -> HubResult<()> {
    let parent = path
        .parent()
        .ok_or_else(|| HubError::storage_error("缺少目标目录"))?;
    fs::create_dir_all(parent).map_err(|e| HubError::storage_error(e.to_string()))?;
    let mut temp = tempfile::NamedTempFile::new_in(parent)
        .map_err(|e| HubError::storage_error(e.to_string()))?;
    temp.write_all(data)
        .and_then(|_| temp.as_file().sync_all())
        .map_err(|e| HubError::storage_error(e.to_string()))?;
    temp.persist(path)
        .map_err(|e| HubError::storage_error(e.to_string()))?;
    Ok(())
}
fn save_receipt(root: &Path, item: &ModItem, phase: &str) -> HubResult<()> {
    let path = confined(&state_dir(root, item)?, "receipt.json")?;
    atomic_write(
        &path,
        &serde_json::to_vec(&Receipt {
            item_id: item.id.clone(),
            phase: phase.into(),
        })
        .unwrap(),
    )
}
fn compatible(root: &Path, game: &GameInstallation, c: &Catalog) -> HubResult<bool> {
    let cat = root.join("AstralParty_CN_Data/StreamingAssets/aa/catalog.bundle");
    Ok(game.platform == "STEAM_CN"
        && cat.is_file()
        && hash(root.join("GameAssembly.dll"))? == c.game_assembly_sha256
        && hash(confined(
            root,
            "AstralParty_CN_Data/StreamingAssets/aa/catalog.bundle",
        )?)? == c.catalog_sha256)
}
fn fingerprint(path: &Path) -> HubResult<String> {
    if path.is_file() {
        hash(path)
    } else {
        Ok(String::new())
    }
}
fn classify_dll(name: &str, digest: &str) -> (&'static str, &'static str) {
    if name.eq_ignore_ascii_case("version.dll") && digest == LEGACY_SPEED {
        (
            "conflict",
            "旧版独立 DLL 加速不支持星枢互斥协调，可能叠加倍率、影响计时；请勿与托管加速同时开启。",
        )
    } else if name.eq_ignore_ascii_case("version.dll") && digest == COORDINATED_SPEED {
        (
            "compatible",
            "已识别支持星枢 IPC 协调的原生加速版本；文件识别不代表当前游戏已加载或握手成功。",
        )
    } else if name.to_lowercase().contains("xingshu") || name.to_lowercase().contains("apx") {
        (
            "review",
            "检测到额外星枢/旧插件 DLL，可能重复注入内置功能；需核对来源及加载状态。",
        )
    } else {
        (
            "review",
            "外部 DLL/加载器，尚无经过验证的兼容指纹；仅记录，不按文件名判定为冲突。",
        )
    }
}
fn scan_dlls(root: &Path) -> HubResult<Vec<Finding>> {
    let mut paths = Vec::new();
    for e in fs::read_dir(root).map_err(|e| HubError::storage_error(e.to_string()))? {
        let e = e.map_err(|e| HubError::storage_error(e.to_string()))?;
        let n = e.file_name().to_string_lossy().to_lowercase();
        if n.ends_with(".dll")
            && ![
                "gameassembly.dll",
                "unityplayer.dll",
                "baselib.dll",
                "steam_api64.dll",
            ]
            .contains(&n.as_str())
        {
            paths.push(e.path());
        }
    }
    for d in [
        "BepInEx/plugins",
        "BepInEx/patchers",
        "MelonLoader",
        "Mods",
        "xingshu/plugins",
    ] {
        let dir = confined(root, d)?;
        if !dir.is_dir() {
            continue;
        }
        for e in walkdir::WalkDir::new(dir)
            .follow_links(false)
            .max_depth(4)
            .into_iter()
            .take(2000)
        {
            let e = e.map_err(|e| HubError::storage_error(e.to_string()))?;
            if e.file_type().is_file()
                && e.path()
                    .extension()
                    .is_some_and(|s| s.eq_ignore_ascii_case("dll"))
            {
                paths.push(e.into_path());
            }
        }
    }
    paths.sort();
    paths.dedup();
    let mut findings = Vec::new();
    for path in paths {
        let safe = PathGuard::ensure_within_root(root, &path)?;
        if fs::symlink_metadata(&path)
            .map_err(|e| HubError::storage_error(e.to_string()))?
            .file_type()
            .is_symlink()
        {
            continue;
        }
        let digest = hash(&safe)?;
        let name = path.file_name().unwrap().to_string_lossy().to_string();
        let (level, description) = classify_dll(&name, &digest);
        findings.push(Finding {
            name,
            path: safe.display().to_string(),
            sha256: digest,
            level: level.into(),
            description: description.into(),
        });
    }
    Ok(findings)
}
fn report(
    game: &GameInstallation,
    root: &Path,
    lib: Option<&Path>,
    c: &Catalog,
) -> HubResult<ModReport> {
    let ok = compatible(root, game, c)?;
    let mut library = Vec::new();
    let mut fingerprints = HashMap::new();
    let mut cached_hash = |path: PathBuf| -> HubResult<String> {
        if let Some(value) = fingerprints.get(&path) {
            return Ok(String::clone(value));
        }
        let value = fingerprint(&path)?;
        fingerprints.insert(path, value.clone());
        Ok(value)
    };
    for item in &c.items {
        let receipt = read_receipt(root, item)?;
        let mut baseline = true;
        let mut installed = true;
        let mut payload_ready = lib.is_some();
        for f in &item.files {
            let actual = cached_hash(target(root, f)?)?;
            baseline &= actual == f.original_sha256;
            installed &= actual == f.sha256;
            if let Some(lib) = lib {
                let path = confined(lib, &f.source)?;
                payload_ready &= cached_hash(path.clone())? == f.sha256
                    && fs::metadata(path)
                        .map(|m| m.len() == f.size)
                        .unwrap_or(false);
            }
        }
        let phase = receipt.as_ref().map(|r| r.phase.as_str());
        let owned = phase == Some("installed");
        let status = if item.files.is_empty() {
            "模型/语音待适配"
        } else if phase == Some("prepared") {
            "上次操作未完成，请还原"
        } else if owned && installed {
            "已启用 · 显示待实测"
        } else if owned {
            "文件已变化，停止操作"
        } else if !ok {
            "此渠道/游戏版本尚未适配"
        } else if installed {
            "检测到同款外部安装，无 Hub 安装前备份"
        } else if !baseline {
            "有资源被替换，请先处理同位置模组"
        } else if !payload_ready {
            "本地配套文件缺失或校验不符"
        } else {
            "可安装 · 资源映射已核验"
        };
        library.push(LibraryEntry {
            id: item.id.clone(),
            name: item.name.clone(),
            author: item.author.clone(),
            archive: item.archive.clone(),
            files: item.files.clone(),
            unmapped_count: item.unmapped_files.len(),
            conflicts: item
                .conflicts
                .iter()
                .filter_map(|id| c.items.iter().find(|x| &x.id == id).map(|x| x.name.clone()))
                .collect(),
            status: status.into(),
            can_install: ok
                && baseline
                && payload_ready
                && !item.files.is_empty()
                && phase != Some("prepared")
                && !owned,
            can_restore: matches!(phase, Some("installed" | "prepared")),
        });
    }
    Ok(ModReport{game_path:root.display().to_string(),library_path:lib.map(|p|p.display().to_string()),compatible:ok,
        findings:scan_dlls(root)?,library,notes:vec!["外部 DLL 只做文件核验，运行状态请到运行时诊断查看。".into(),
        "立绘、卡面纹理按资源名称映射到当前文件；模型、动画、语音未适配部分不会混装。仅适配 Steam 国服 3.3.0，TapTap 与安卓不沿用这些文件。".into(),
        "启用前备份当前文件；还原只恢复本条目的安装前备份。文件被其他工具改动后会停止，防止覆盖更新。".into(),
        "DLL 检查覆盖游戏根目录及常用加载器插件目录，仅已知指纹能够确认兼容或冲突；无法据此保证发现所有第三方修改。".into()]})
}
fn game_closed(game: &GameInstallation) -> HubResult<()> {
    crate::install::gates::ProcessGate::check_target(Path::new(
        game.executable_path.as_deref().unwrap_or_default(),
    ))
}
fn operation_lock(root: &Path) -> HubResult<fs::File> {
    let path = confined(root, ".xingshu-mods/operation.lock")?;
    fs::create_dir_all(path.parent().unwrap())
        .map_err(|e| HubError::storage_error(e.to_string()))?;
    let mut options = fs::OpenOptions::new();
    options.write(true).create(true).truncate(false);
    #[cfg(windows)]
    {
        use std::os::windows::fs::OpenOptionsExt;
        options.share_mode(0);
    }
    options
        .open(path)
        .map_err(|_| HubError::storage_error("另一个模组操作尚未结束，请稍后重试。"))
}
fn restore(root: &Path, item: &ModItem) -> HubResult<()> {
    let receipt = read_receipt(root, item)?
        .ok_or_else(|| HubError::storage_error("没有本条目的安装前备份。"))?;
    if receipt.phase == "restored" {
        return Ok(());
    }
    let dir = state_dir(root, item)?;
    // Preflight the entire transaction before changing even one target.
    for (i, f) in item.files.iter().enumerate() {
        let digest = fingerprint(&target(root, f)?)?;
        if digest != f.sha256 && digest != f.original_sha256 {
            return Err(HubError::security_violation(
                "文件被外部修改或游戏已更新，停止还原。",
            ));
        }
        if hash(confined(&dir, &format!("baseline/{i}.bundle"))?)? != f.original_sha256 {
            return Err(HubError::security_violation("安装前备份校验失败。"));
        }
    }
    for (i, f) in item.files.iter().enumerate() {
        let current = fingerprint(&target(root, f)?)?;
        if current != f.sha256 && current != f.original_sha256 {
            return Err(HubError::security_violation(
                "还原期间文件发生变化，保留备份并停止。",
            ));
        }
        let data = fs::read(confined(&dir, &format!("baseline/{i}.bundle"))?)
            .map_err(|e| HubError::storage_error(e.to_string()))?;
        use sha2::{Digest, Sha256};
        if hex::encode(Sha256::digest(&data)) != f.original_sha256 {
            return Err(HubError::security_violation(
                "还原期间备份发生变化，停止操作。",
            ));
        }
        atomic_write(&target(root, f)?, &data)?;
        if hash(target(root, f)?)? != f.original_sha256 {
            return Err(HubError::security_violation(
                "还原后的文件校验失败，保留收据以便重试。",
            ));
        }
    }
    save_receipt(root, item, "restored")
}
fn install(
    game: &GameInstallation,
    root: &Path,
    lib: &Path,
    c: &Catalog,
    item: &ModItem,
) -> HubResult<()> {
    if !compatible(root, game, c)? || item.files.is_empty() {
        return Err(HubError::invalid_manifest("此条目尚未适配所选游戏版本。"));
    }
    game_closed(game)?;
    if read_receipt(root, item)?.is_some_and(|r| r.phase != "restored") {
        return Err(HubError::invalid_manifest("此条目已有安装记录，请先还原。"));
    }
    let mut unique = HashSet::new();
    for f in &item.files {
        if !unique.insert(&f.target) {
            return Err(HubError::invalid_manifest("重复的模组目标。"));
        }
        if hash(target(root, f)?)? != f.original_sha256 {
            return Err(HubError::security_violation(
                "目标已被其他模组修改，请先还原，不能覆盖。",
            ));
        }
        let payload = confined(lib, &f.source)?;
        if fs::metadata(&payload)
            .map_err(|e| HubError::storage_error(e.to_string()))?
            .len()
            != f.size
            || hash(payload)? != f.sha256
        {
            return Err(HubError::security_violation("配套模组文件已损坏。"));
        }
    }
    // Shared targets are owned by exactly one receipt, even when two payloads happen to match.
    for other in &c.items {
        if other.id != item.id
            && other.files.iter().any(|f| unique.contains(&f.target))
            && read_receipt(root, other)?.is_some_and(|r| r.phase != "restored")
        {
            return Err(HubError::invalid_manifest(format!(
                "目标由“{}”管理，请先还原该模组。",
                other.name
            )));
        }
    }
    let dir = state_dir(root, item)?;
    for (i, f) in item.files.iter().enumerate() {
        let backup = confined(&dir, &format!("baseline/{i}.bundle"))?;
        if backup.exists() {
            if hash(&backup)? != f.original_sha256 {
                return Err(HubError::security_violation("已有备份不匹配。"));
            }
        } else {
            atomic_write(
                &backup,
                &fs::read(target(root, f)?).map_err(|e| HubError::storage_error(e.to_string()))?,
            )?;
        }
        if hash(&backup)? != f.original_sha256 {
            return Err(HubError::security_violation("备份校验失败。"));
        }
    }
    game_closed(game)?;
    save_receipt(root, item, "prepared")?;
    let commit = (|| -> HubResult<()> {
        for f in &item.files {
            if hash(target(root, f)?)? != f.original_sha256 {
                return Err(HubError::security_violation("操作期间目标文件发生变化。"));
            }
            let data = fs::read(confined(lib, &f.source)?)
                .map_err(|e| HubError::storage_error(e.to_string()))?;
            // Recheck bytes immediately before replacing to avoid a changed library during the operation.
            use sha2::{Digest, Sha256};
            if hex::encode(Sha256::digest(&data)) != f.sha256 {
                return Err(HubError::security_violation("模组在操作期间发生变化。"));
            }
            atomic_write(&target(root, f)?, &data)?;
            if hash(target(root, f)?)? != f.sha256 {
                return Err(HubError::security_violation("写入后的模组校验失败。"));
            }
        }
        save_receipt(root, item, "installed")
    })();
    if let Err(e) = commit {
        return match restore(root, item) {
            Ok(()) => Err(e),
            Err(rollback) => Err(HubError::storage_error(format!(
                "{e}；自动还原未完成：{rollback}。保留备份和收据，请用还原重试。"
            ))),
        };
    }
    Ok(())
}

#[tauri::command]
pub async fn get_external_mod_report(
    installation_id: String,
    center: State<'_, ExtensionCenter>,
) -> HubResult<ModReport> {
    let game = get_game(&center.db, &installation_id)?;
    tokio::task::spawn_blocking(move || {
        let root = game_root(&game)?;
        report(&game, &root, library_root().as_deref(), &catalog()?)
    })
    .await
    .map_err(|e| HubError::storage_error(e.to_string()))?
}
#[tauri::command]
pub async fn external_mod_action(
    installation_id: String,
    item_id: String,
    action: String,
    center: State<'_, ExtensionCenter>,
) -> HubResult<String> {
    let game = get_game(&center.db, &installation_id)?;
    tokio::task::spawn_blocking(move || {
        let root = game_root(&game)?;
        let _lock = operation_lock(&root)?;
        game_closed(&game)?;
        let c = catalog()?;
        let item = c
            .items
            .iter()
            .find(|x| x.id == item_id)
            .ok_or_else(|| HubError::invalid_manifest("未知模组。"))?;
        match action.as_str() {
            "install" => {
                let lib =
                    library_root().ok_or_else(|| HubError::storage_error("未找到本地模组库。"))?;
                install(&game, &root, &lib, &c, item)?;
                Ok("已备份安装前文件并启用，文件校验通过；请进入游戏核对显示。".into())
            }
            "restore" => {
                restore(&root, item)?;
                Ok("已还原安装前文件，备份保留。".into())
            }
            _ => Err(HubError::invalid_manifest("未知模组操作。")),
        }
    })
    .await
    .map_err(|e| HubError::storage_error(e.to_string()))?
}
#[tauri::command]
pub async fn archive_external_mod_report(
    installation_id: String,
    center: State<'_, ExtensionCenter>,
) -> HubResult<String> {
    let game = get_game(&center.db, &installation_id)?;
    tokio::task::spawn_blocking(move||{
        let root=game_root(&game)?;let _lock=operation_lock(&root)?;
        let r=report(&game,&root,library_root().as_deref(),&catalog()?)?;
        let dir=confined(&root,".xingshu-mods/reports")?;fs::create_dir_all(&dir).map_err(|e|HubError::storage_error(e.to_string()))?;
        let dest=dir.join(format!("mod-check-{}.zip",uuid::Uuid::new_v4()));
        let mut temp=tempfile::NamedTempFile::new_in(&dir).map_err(|e|HubError::storage_error(e.to_string()))?;
        {
            let mut zip=zip::ZipWriter::new(temp.as_file_mut());
            let opts=zip::write::SimpleFileOptions::default().compression_method(zip::CompressionMethod::Deflated);
            let io=|e:zip::result::ZipError|HubError::storage_error(e.to_string());
            zip.start_file("report.json",opts).map_err(io)?;
            zip.write_all(&serde_json::to_vec_pretty(&r).unwrap()).map_err(|e|HubError::storage_error(e.to_string()))?;
            let mut size=0u64;
            for f in r.findings.iter().filter(|f|f.level!="compatible") {
                let p=PathGuard::ensure_within_root(&root,Path::new(&f.path))?;
                let data=fs::read(&p).map_err(|e|HubError::storage_error(e.to_string()))?;size+=data.len() as u64;
                if size>512_000_000{return Err(HubError::storage_error("待归档文件超过 512 MB，请分开处理。"));}
                use sha2::{Digest,Sha256};if hex::encode(Sha256::digest(&data))!=f.sha256{return Err(HubError::security_violation("文件在归档期间发生变化。"));}
                let rel=p.strip_prefix(&root).map_err(|e|HubError::security_violation(e.to_string()))?.to_string_lossy().replace('\\',"/");
                zip.start_file(format!("review-files/{rel}"),opts).map_err(io)?;zip.write_all(&data).map_err(|e|HubError::storage_error(e.to_string()))?;
            }
            zip.start_file("说明.txt",opts).map_err(io)?;zip.write_all("此包保存检查报告、已知冲突和待核验组件的副本。原目录文件未移动、停用或删除。兼容的星枢协调加速 DLL 不重复归档。报告包含本机路径，请按需分享。".as_bytes()).map_err(|e|HubError::storage_error(e.to_string()))?;
            zip.finish().map_err(io)?;
        }
        temp.persist(&dest).map_err(|e|HubError::storage_error(e.to_string()))?;
        Ok(dest.display().to_string())
    }).await.map_err(|e|HubError::storage_error(e.to_string()))?
}

#[cfg(test)]
mod tests {
    use super::*;
    use sha2::{Digest, Sha256};
    fn digest(b: &[u8]) -> String {
        hex::encode(Sha256::digest(b))
    }
    fn fixture() -> (tempfile::TempDir, GameInstallation, Catalog, PathBuf) {
        let temp = tempfile::tempdir().unwrap();
        let root = temp.path();
        let base = "AstralParty_CN_Data/StreamingAssets/aa";
        fs::create_dir_all(root.join(format!("{base}/StandaloneWindows64"))).unwrap();
        fs::write(root.join("GameAssembly.dll"), b"game").unwrap();
        fs::write(root.join("game.exe"), b"fixture").unwrap();
        fs::write(root.join(format!("{base}/catalog.bundle")), b"catalog").unwrap();
        let file = ModFile {
            source: "payload/test.bundle".into(),
            target: format!("{base}/StandaloneWindows64/0123456789abcdef0123456789abcdef.bundle"),
            sha256: digest(b"mod"),
            original_sha256: digest(b"original"),
            size: 3,
            assets: vec!["UT_Test".into()],
        };
        fs::write(root.join(&file.target), b"original").unwrap();
        let lib = root.join("library");
        fs::create_dir_all(lib.join("payload")).unwrap();
        fs::write(lib.join(&file.source), b"mod").unwrap();
        let game:GameInstallation=serde_json::from_value(serde_json::json!({"id":"test","platform":"STEAM_CN","install_path":root,"executable_path":root.join("game.exe"),"detection_method":"fixture","is_valid":true,"is_primary":true,"created_at":chrono::Utc::now(),"updated_at":chrono::Utc::now()})).unwrap();
        let c = Catalog {
            format_version: 1,
            game_version: "3.3.0".into(),
            game_assembly_sha256: digest(b"game"),
            catalog_sha256: digest(b"catalog"),
            items: vec![ModItem {
                id: "local.0123456789abcdef".into(),
                name: "fixture".into(),
                archive: "a.zip".into(),
                archive_sha256: String::new(),
                author: "fixture".into(),
                platforms: vec!["STEAM_CN".into()],
                files: vec![file],
                unmapped_files: vec![],
                conflicts: vec![],
            }],
        };
        (temp, game, c, lib)
    }
    #[test]
    fn hashes_distinguish_legacy_coordinated_and_unknown_dlls() {
        assert_eq!(classify_dll("version.dll", LEGACY_SPEED).0, "conflict");
        assert_eq!(
            classify_dll("version.dll", COORDINATED_SPEED).0,
            "compatible"
        );
        assert_eq!(classify_dll("version.dll", "unknown").0, "review");
    }
    #[test]
    fn install_restore_reinstall_preserve_original() {
        let (t, g, c, lib) = fixture();
        let r = t.path();
        let item = &c.items[0];
        install(&g, r, &lib, &c, item).unwrap();
        assert_eq!(
            fs::read(target(r, &item.files[0]).unwrap()).unwrap(),
            b"mod"
        );
        assert!(install(&g, r, &lib, &c, item).is_err());
        restore(r, item).unwrap();
        restore(r, item).unwrap();
        assert_eq!(
            fs::read(target(r, &item.files[0]).unwrap()).unwrap(),
            b"original"
        );
        install(&g, r, &lib, &c, item).unwrap();
        restore(r, item).unwrap();
    }
    #[test]
    fn external_changes_block_restore_without_touching_any_file() {
        let (t, g, c, lib) = fixture();
        install(&g, t.path(), &lib, &c, &c.items[0]).unwrap();
        let p = target(t.path(), &c.items[0].files[0]).unwrap();
        fs::write(&p, b"external").unwrap();
        assert!(restore(t.path(), &c.items[0]).is_err());
        assert_eq!(fs::read(p).unwrap(), b"external");
    }
    #[test]
    fn wrong_channel_corrupt_payload_and_changed_original_are_rejected() {
        let (t, mut g, c, lib) = fixture();
        g.platform = "TAPTAP".into();
        assert!(install(&g, t.path(), &lib, &c, &c.items[0]).is_err());
        g.platform = "STEAM_CN".into();
        fs::write(lib.join("payload/test.bundle"), b"bad").unwrap();
        assert!(install(&g, t.path(), &lib, &c, &c.items[0]).is_err());
        assert!(read_receipt(t.path(), &c.items[0]).unwrap().is_none());
        fs::write(lib.join("payload/test.bundle"), b"mod").unwrap();
        fs::write(target(t.path(), &c.items[0].files[0]).unwrap(), b"new game").unwrap();
        assert!(install(&g, t.path(), &lib, &c, &c.items[0]).is_err());
    }
    #[test]
    fn prepared_transaction_can_be_recovered() {
        let (t, g, c, lib) = fixture();
        let item = &c.items[0];
        install(&g, t.path(), &lib, &c, item).unwrap();
        save_receipt(t.path(), item, "prepared").unwrap();
        restore(t.path(), item).unwrap();
        assert_eq!(
            read_receipt(t.path(), item).unwrap().unwrap().phase,
            "restored"
        );
    }
    #[test]
    fn paths_cannot_replace_runtime_or_escape() {
        let (t, _, c, _) = fixture();
        let mut f = c.items[0].files[0].clone();
        f.target = "GameAssembly.dll".into();
        assert!(target(t.path(), &f).is_err());
        f.target = "../escape.bundle".into();
        assert!(target(t.path(), &f).is_err());
        assert!(confined(t.path(), "../escape").is_err());
    }
    #[test]
    fn shared_resources_cannot_be_owned_by_two_mods() {
        let (t, g, mut c, lib) = fixture();
        let mut second = c.items[0].clone();
        second.id = "local.fedcba9876543210".into();
        c.items.push(second);
        install(&g, t.path(), &lib, &c, &c.items[0]).unwrap();
        assert!(install(&g, t.path(), &lib, &c, &c.items[1]).is_err());
        restore(t.path(), &c.items[0]).unwrap();
        install(&g, t.path(), &lib, &c, &c.items[1]).unwrap();
    }
    fn add_second(c: &mut Catalog, root: &Path, lib: &Path) {
        let mut f = c.items[0].files[0].clone();
        f.source = "payload/second.bundle".into();
        f.target = f.target.replace(
            "0123456789abcdef0123456789abcdef",
            "fedcba9876543210fedcba9876543210",
        );
        fs::write(root.join(&f.target), b"original").unwrap();
        fs::write(lib.join(&f.source), b"mod").unwrap();
        c.items[0].files.push(f);
    }
    #[test]
    fn partial_multifile_transaction_recovers_and_corrupt_backup_preflights_every_file() {
        let (t, _, mut c, lib) = fixture();
        let root = t.path();
        add_second(&mut c, root, &lib);
        let item = &c.items[0];
        for i in 0..2 {
            atomic_write(
                &confined(
                    &state_dir(root, item).unwrap(),
                    &format!("baseline/{i}.bundle"),
                )
                .unwrap(),
                b"original",
            )
            .unwrap();
        }
        save_receipt(root, item, "prepared").unwrap();
        fs::write(target(root, &item.files[0]).unwrap(), b"mod").unwrap();
        let backup = confined(&state_dir(root, item).unwrap(), "baseline/1.bundle").unwrap();
        fs::write(&backup, b"corrupt").unwrap();
        assert!(restore(root, item).is_err());
        assert_eq!(
            fs::read(target(root, &item.files[0]).unwrap()).unwrap(),
            b"mod"
        );
        assert_eq!(read_receipt(root, item).unwrap().unwrap().phase, "prepared");
        fs::write(backup, b"original").unwrap();
        restore(root, item).unwrap();
        for f in &item.files {
            assert_eq!(fs::read(target(root, f).unwrap()).unwrap(), b"original");
        }
    }
    #[test]
    fn corrupt_library_is_reported_before_install() {
        let (t, g, c, lib) = fixture();
        assert!(report(&g, t.path(), Some(&lib), &c).unwrap().library[0].can_install);
        fs::write(lib.join("payload/test.bundle"), b"bad").unwrap();
        assert!(!report(&g, t.path(), Some(&lib), &c).unwrap().library[0].can_install);
    }
    #[test]
    fn a_running_selected_executable_blocks_all_install_writes() {
        let (t, mut g, c, lib) = fixture();
        g.executable_path = Some(std::env::current_exe().unwrap().display().to_string());
        assert!(install(&g, t.path(), &lib, &c, &c.items[0]).is_err());
        assert!(!t.path().join(".xingshu-mods").exists());
        assert_eq!(
            fs::read(target(t.path(), &c.items[0].files[0]).unwrap()).unwrap(),
            b"original"
        );
    }
    #[test]
    fn embedded_catalog_has_unique_scoped_targets_and_fixed_payload_hashes() {
        let c = catalog().unwrap();
        let temp = tempfile::tempdir().unwrap();
        let mut ids = HashSet::new();
        assert_eq!(c.format_version, 1);
        for item in &c.items {
            assert!(ids.insert(&item.id));
            state_dir(temp.path(), item).unwrap();
            assert_eq!(item.platforms, vec!["STEAM_CN"]);
            let mut targets = HashSet::new();
            for f in &item.files {
                assert!(targets.insert(&f.target));
                target(temp.path(), f).unwrap();
                assert_eq!(f.source, format!("payload/{}.bundle", f.sha256));
                assert_eq!(f.sha256.len(), 64);
                assert_eq!(f.original_sha256.len(), 64);
            }
        }
    }
}
