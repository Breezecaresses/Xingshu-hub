//! Installation-scoped extension inventory. Never treats an example package as installed.
use std::path::{Path, PathBuf};
use chrono::Utc;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use tauri::State;
use crate::domain::error::{HubError, HubResult};
use crate::domain::installation::GameInstallation;
use crate::security::hash::compute_file_sha256;
use crate::storage::{database::Database, repositories::{GameRepository, ExtensionRepository}};

pub struct ExtensionCenter { pub db: Database }

pub fn profiles() -> Vec<serde_json::Value> {
    serde_json::from_str(include_str!("xing_shu/platform/platform_profiles.json")).unwrap_or_default()
}
fn normal(path: &str) -> String { path.replace('\\', "/").trim_end_matches('/').to_lowercase() }
pub fn scoped_package_id(package: &str, game: &GameInstallation) -> String {
    format!("{}@{}", package, hex::encode(Sha256::digest(format!("{}:{}", game.platform, normal(&game.install_path)).as_bytes()))[..16].to_string())
}
pub fn game_root(game: &GameInstallation) -> HubResult<PathBuf> {
    let exe = game.executable_path.as_ref().ok_or_else(|| HubError::game_not_found("请先选择此渠道的实际游戏程序。"))?;
    let root = Path::new(exe).parent().ok_or_else(|| HubError::game_not_found("游戏程序路径无效。"))?;
    if !Path::new(exe).is_file() || !root.join("GameAssembly.dll").is_file() {
        return Err(HubError::game_not_found("游戏程序或 GameAssembly.dll 不存在，请重新配置此安装位置。"));
    }
    Ok(root.to_path_buf())
}
pub fn get_game(db: &Database, id: &str) -> HubResult<GameInstallation> {
    GameRepository::new(db.clone()).list_all()?.into_iter().find(|g| g.id == id)
        .ok_or_else(|| HubError::game_not_found("未找到所选安装位置，请刷新。"))
}
fn register(db: &Database, platform: &str, selection: &str) -> HubResult<GameInstallation> {
    let p = profiles().into_iter().find(|p| p["platformId"] == platform)
        .ok_or_else(|| HubError::game_not_found("未知游戏渠道。"))?;
    let selection = PathBuf::from(selection.trim().trim_matches('"'));
    let mut root = if selection.is_file() { selection.parent().unwrap().to_path_buf() } else { selection.clone() };
    let exe = p["processExecutable"].as_str().unwrap_or_default();
    if !root.join(exe).is_file() {
        let sub = if platform == "STEAM_GLOBAL" { "8vJXnINT" } else { "8vJXn6CN" };
        if platform.starts_with("STEAM_") && root.join(sub).join(exe).is_file() { root = root.join(sub); }
    }
    if !root.join(exe).is_file() || !root.join("GameAssembly.dll").is_file()
        || (selection.is_file() && selection.file_name() != Some(std::ffi::OsStr::new(exe))) {
        return Err(HubError::game_not_found(&format!("请选择 {} 及其所在的游戏目录，不能选择渠道启动器。", exe)));
    }
    let root = dunce::canonicalize(root).map_err(|e| HubError::game_not_found(&e.to_string()))?;
    let repo = GameRepository::new(db.clone());
    let all = repo.list_all()?;
    // Do not allow one directory to be relabelled as a different channel.
    if all.iter().any(|g| g.platform != platform && g.platform != "steam" && normal(&g.install_path) == normal(&root.display().to_string())) {
        return Err(HubError::game_not_found("该目录已登记在另一渠道下，请检查选择。"));
    }
    let existing = all.iter().find(|g| g.platform == platform && normal(&g.install_path) == normal(&root.display().to_string()));
    let game = GameInstallation {
        id: existing.map(|g| g.id.clone()).unwrap_or_else(|| uuid::Uuid::new_v4().to_string()),
        platform: platform.into(), platform_app_id: p["steamAppId"].as_u64().map(|v| v.to_string()),
        install_path: root.display().to_string(), executable_path: Some(root.join(exe).display().to_string()),
        detected_build_id: None, detected_version: None, detection_method: "CHANNEL_DIRECTORY".into(),
        is_valid: true, is_primary: false, last_verified_at: Some(Utc::now()),
        created_at: existing.map(|g| g.created_at).unwrap_or_else(Utc::now), updated_at: Utc::now(),
    };
    repo.save(&game)?;
    Ok(game)
}

#[tauri::command]
pub async fn list_extension_targets(center: State<'_, ExtensionCenter>) -> HubResult<Vec<GameInstallation>> {
    list_targets(&center.db)
}
pub fn list_targets(db: &Database) -> HubResult<Vec<GameInstallation>> {
    let repo = GameRepository::new(db.clone());
    let saved = repo.list_all()?;
    let steam = crate::game::detector::GameDetector::detect_game();
    for p in profiles() {
        let platform = p["platformId"].as_str().unwrap_or_default();
        let mut candidates = Vec::new();
        if let Some(root) = p["installRoot"].as_str() { candidates.push(root.to_string()); }
        if platform.starts_with("STEAM_") {
            if let Some(d) = &steam { candidates.push(d.install_path.clone()); }
            for g in &saved { if g.platform == "steam" { candidates.push(g.install_path.clone()); } }
        }
        for root in candidates { let _ = register(db, platform, &root); }
    }
    Ok(repo.list_all()?.into_iter().filter(|g| profiles().iter().any(|p| p["platformId"] == g.platform)).collect())
}
#[tauri::command]
pub async fn register_extension_target(platform_id: String, path: String, center: State<'_, ExtensionCenter>) -> HubResult<GameInstallation> {
    register(&center.db, &platform_id, &path)
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ExtensionEntry {
    pub id: String, pub package_id: String, pub name: String, pub category: String,
    pub version: Option<String>, pub path: String, pub source: String, pub status: String,
    pub description: String, pub managed: bool,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ExtensionInventory {
    pub entries: Vec<ExtensionEntry>, pub plugin_directory: String, pub mod_directory: String,
    pub resource_directory: String, pub warnings: Vec<String>, pub installer_available: bool,
}
fn package_root() -> Option<PathBuf> {
    let exe = std::env::current_exe().ok()?;
    [exe.parent()?, exe.parent()?.parent()?].into_iter()
        .find(|p| p.join("release.json").is_file() && installer_path(p).is_some()).map(Path::to_path_buf)
}
fn installer_path(package: &Path) -> Option<PathBuf> {
    ["星枢安装助手.exe", "星枢群测助手.exe"].into_iter()
        .map(|name| package.join(name)).find(|path| path.is_file())
}
pub fn inventory(db: &Database, game: &GameInstallation) -> HubResult<ExtensionInventory> {
    let root = game_root(game)?;
    let repo = ExtensionRepository::new(db.clone());
    let mut out = ExtensionInventory { entries: vec![], plugin_directory: root.join("xingshu/plugins").display().to_string(),
        mod_directory: root.join("mods").display().to_string(), resource_directory: root.join("resources").display().to_string(), warnings: vec![], installer_available: package_root().is_some() };
    for (id, name, version, path, manifest, category) in repo.list_plugins()?.into_iter().map(|p| (p.plugin_id,p.name,p.installed_version,p.install_path,p.manifest_json,"xingshu-plugin"))
        .chain(repo.list_mods()?.into_iter().map(|p| (p.mod_id,p.name,p.installed_version,p.install_path,p.manifest_json,"mod"))) {
        let Some(path) = path else { continue };
        if !Path::new(&path).starts_with(&root) { continue; }
        let original_id = manifest.as_ref().and_then(|m| m["package"]["id"].as_str()).unwrap_or(&id).to_string();
        let actual_category = manifest.as_ref().and_then(|m| m["package"]["type"].as_str()).unwrap_or(category).to_string();
        out.entries.push(ExtensionEntry { id, package_id: original_id, name, category: actual_category, version, path: path.clone(),
            source: "Hub 安装记录".into(), status: if Path::new(&path).exists() { "已安装" } else { "文件缺失" }.into(),
            description: manifest.and_then(|m| m["package"]["description"].as_str().map(String::from)).unwrap_or_default(), managed: true });
    }
    // This index contains only hashes of the checked local runtime, never user paths or credentials.
    let runtimes: Vec<serde_json::Value> = serde_json::from_str(include_str!("runtime_inventory.generated.json")).unwrap_or_default();
    if let Some(runtime) = runtimes.iter().find(|r| r["platform"] == game.platform) {
        let path = root.join(runtime["bundleRelativePath"].as_str().unwrap_or_default());
        if path.is_file() {
            let hash = compute_file_sha256(&path)?;
            let installed = hash.eq_ignore_ascii_case(runtime["sha256"].as_str().unwrap_or_default());
            if installed {
                out.entries.insert(0, ExtensionEntry { id: "builtin.runtime".into(), package_id: "xingshu.runtime.core".into(),
                    name: "星枢运行时与内置功能".into(), category: "xingshu-plugin".into(), version: runtime["version"].as_str().map(String::from),
                    path: path.display().to_string(), source: "游戏文件 SHA256 核验".into(), status: "文件已核验".into(),
                    description: "包含观战手牌、角色与战绩同步、功能中心及本地计时。内置功能随运行时一起更新，请在游戏中查看启用状态。".into(), managed: false });
            } else { out.warnings.push("当前游戏资源与本版运行时指纹不同；不将其视为已安装最新版。请使用群测助手检查。".into()); }
        }
    }
    for (directory, category) in [("xingshu/plugins", "xingshu-plugin"), ("BepInEx/plugins", "xingshu-plugin"), ("mods", "mod"), ("resources", "resource-pack"), ("modimg", "resource-pack")] {
        let dir = root.join(directory);
        let files = match std::fs::read_dir(&dir) { Ok(f) => f, Err(e) if e.kind() == std::io::ErrorKind::NotFound => continue, Err(e) => { out.warnings.push(format!("无法读取 {}：{}", dir.display(), e)); continue } };
        for file in files.take(200) {
            let Ok(file) = file else { continue };
            let path = file.path();
            if file.file_type().map(|t| t.is_symlink()).unwrap_or(true) { continue; }
            if out.entries.iter().any(|e| Path::new(&e.path).starts_with(&path) || path.starts_with(Path::new(&e.path))) { continue; }
            if category == "xingshu-plugin" && !path.is_dir() && path.extension().and_then(|e| e.to_str()) != Some("dll") { continue; }
            let archive = matches!(path.extension().and_then(|e| e.to_str()), Some("zip" | "xspkg" | "xingshu"));
            out.entries.push(ExtensionEntry { id: format!("local:{}", path.display()), package_id: String::new(), name: file.file_name().to_string_lossy().into(), category: category.into(),
                version: None, path: path.display().to_string(), source: "本地目录扫描".into(), status: if archive { "本地归档 · 未验证安装" } else { "本地文件 · 加载状态待确认" }.into(), description: "由外部工具或手动放入；星枢只展示检测到的文件，不推断它是否已加载。".into(), managed: false });
        }
    }
    Ok(out)
}
#[tauri::command]
pub async fn get_extension_inventory(installation_id: String, center: State<'_, ExtensionCenter>) -> HubResult<ExtensionInventory> {
    inventory(&center.db, &get_game(&center.db, &installation_id)?)
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RuntimeTargetState { process_ids: Vec<u32>, bep_in_ex_files_detected: bool }
#[tauri::command]
pub async fn get_extension_runtime_state(installation_id: String, center: State<'_, ExtensionCenter>) -> HubResult<RuntimeTargetState> {
    use sysinfo::{ProcessRefreshKind, RefreshKind, System, UpdateKind};
    let game = get_game(&center.db, &installation_id)?;
    let root = game_root(&game)?;
    let expected = dunce::canonicalize(game.executable_path.as_deref().unwrap_or_default()).map_err(|e| HubError::game_not_found(&e.to_string()))?;
    let system = System::new_with_specifics(RefreshKind::nothing().with_processes(ProcessRefreshKind::nothing().with_exe(UpdateKind::Always)));
    let process_ids = system.processes().iter().filter_map(|(pid, process)| {
        let exe = dunce::canonicalize(process.exe()?).ok()?;
        (normal(&exe.display().to_string()) == normal(&expected.display().to_string())).then_some(pid.as_u32())
    }).collect();
    Ok(RuntimeTargetState { process_ids, bep_in_ex_files_detected: root.join("BepInEx/core").is_dir() })
}
#[tauri::command]
pub async fn open_group_installer(installation_id: String, center: State<'_, ExtensionCenter>) -> HubResult<()> {
    let game = get_game(&center.db, &installation_id)?;
    let root = game_root(&game)?;
    let package = package_root().ok_or_else(|| HubError::game_not_found("请完整解压发布包，并从其中的 Hub 目录运行。"))?;
    let installer = installer_path(&package).ok_or_else(|| HubError::game_not_found("安装助手不存在，请重新完整解压发布包。"))?;
    let mut command = std::process::Command::new(installer);
    command.args(["--ui", &game.platform, &root.display().to_string()]);
    command.spawn().map_err(|e| HubError::game_not_found(&e.to_string()))?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::install::engine::InstallEngine;
    use crate::application::ExtensionService;

    fn fixture(db: &Database, root: &Path, channel: &str) -> GameInstallation {
        let profile = profiles().into_iter().find(|p| p["platformId"] == channel).unwrap();
        std::fs::create_dir_all(root).unwrap();
        std::fs::write(root.join(profile["processExecutable"].as_str().unwrap()), b"fixture").unwrap();
        std::fs::write(root.join("GameAssembly.dll"), b"fixture").unwrap();
        register(db, channel, root.to_str().unwrap()).unwrap()
    }

    fn archive(root: &Path, version: &str, platforms: &[&str], content: &[u8]) -> PathBuf {
        use std::io::Write;
        let path = root.join(format!("fixture-{version}.zip"));
        let mut zip = zip::ZipWriter::new(std::fs::File::create(&path).unwrap());
        let opts = zip::write::SimpleFileOptions::default().compression_method(zip::CompressionMethod::Stored);
        let manifest = serde_json::json!({
            "schemaVersion":1,
            "package":{"id":"tests.scoped-mod","type":"mod","name":"隔离测试","version":version,"publisher":"fixture"},
            "compatibility":{"game":{"appId":"2622000","platforms":platforms}},
            "install":{"requiresGameClosed":true,"requiresXingShuClosed":true,"backup":true},
            "files":[{"source":"payload/settings.ini","target":"game/mods/scoped-test/settings.ini","sha256":hex::encode(Sha256::digest(content)),"size":content.len()}]
        });
        zip.start_file("manifest.json", opts).unwrap();
        zip.write_all(manifest.to_string().as_bytes()).unwrap();
        zip.start_file("payload/settings.ini", opts).unwrap();
        zip.write_all(content).unwrap(); zip.finish().unwrap(); path
    }

    #[test]
    fn installs_updates_and_uninstalls_are_scoped_to_selected_directory() {
        let temp = tempfile::tempdir().unwrap();
        let db = Database::in_memory().unwrap();
        // Both clients may sit below the same Steam directory.
        let cn = fixture(&db, &temp.path().join("Steam/8vJXn6CN"), "STEAM_CN");
        let global = fixture(&db, &temp.path().join("Steam/8vJXnINT"), "STEAM_GLOBAL");
        let other_cn = fixture(&db, &temp.path().join("other-cn"), "STEAM_CN");
        let engine = InstallEngine::new(db.clone(), temp.path().join("backups"));
        let first = archive(temp.path(), "1.0.0", &["STEAM_CN", "STEAM_GLOBAL"], b"version=1");
        for game in [&cn, &global, &other_cn] {
            engine.install_package(&first, game).unwrap();
            let entry = inventory(&db, game).unwrap().entries.into_iter().find(|e| e.managed).unwrap();
            assert_eq!(entry.id, scoped_package_id("tests.scoped-mod", game));
            assert_eq!(entry.version.as_deref(), Some("1.0.0"));
        }
        let target = |g: &GameInstallation| game_root(g).unwrap().join("mods/scoped-test/settings.ini");
        assert_eq!(ExtensionRepository::new(db.clone()).list_mods().unwrap().len(), 3);
        let update = archive(temp.path(), "1.1.0", &["STEAM_CN", "STEAM_GLOBAL"], b"version=2");
        engine.install_package(update, &global).unwrap();
        assert_eq!(std::fs::read(target(&global)).unwrap(), b"version=2");
        assert_eq!(std::fs::read(target(&cn)).unwrap(), b"version=1");
        assert_eq!(std::fs::read(target(&other_cn)).unwrap(), b"version=1");
        let service = ExtensionService::new(db.clone());
        let id = scoped_package_id("tests.scoped-mod", &global);
        let exe = Path::new(global.executable_path.as_ref().unwrap());
        std::fs::write(target(&global), b"edited-by-user").unwrap();
        assert!(service.uninstall_extension(&id, false, exe).is_err());
        assert_eq!(std::fs::read(target(&global)).unwrap(), b"edited-by-user");
        std::fs::write(target(&global), b"version=2").unwrap();
        service.uninstall_extension(&id, false, exe).unwrap();
        assert!(!target(&global).exists());
        assert!(target(&cn).exists() && target(&other_cn).exists());
        assert_eq!(ExtensionRepository::new(db).list_mods().unwrap().len(), 2);
    }

    #[test]
    fn incompatible_channel_is_rejected_and_manual_resources_are_read_only() {
        let temp = tempfile::tempdir().unwrap();
        let db = Database::in_memory().unwrap();
        let tap = fixture(&db, &temp.path().join("tap"), "TAPTAP");
        let engine = InstallEngine::new(db.clone(), temp.path().join("backups"));
        let package = archive(temp.path(), "1.0.0", &["STEAM_CN"], b"version=1");
        assert!(engine.install_package(&package, &tap).is_err());
        assert!(!game_root(&tap).unwrap().join("mods").exists());
        let local = game_root(&tap).unwrap().join("modimg/卡面替换");
        std::fs::create_dir_all(&local).unwrap();
        std::fs::write(local.join("fixture.png"), b"image").unwrap();
        let entries = inventory(&db, &tap).unwrap().entries;
        assert_eq!(entries.len(), 1);
        assert_eq!(entries[0].category, "resource-pack");
        assert!(!entries[0].managed);
        assert_eq!(entries[0].status, "本地文件 · 加载状态待确认");
    }

    #[test]
    fn running_executable_is_blocked_before_modification() {
        assert!(crate::install::gates::ProcessGate::check_target(&std::env::current_exe().unwrap()).is_err());
    }
    #[test]
    fn channels_and_scoped_ids_do_not_collide() {
        let temp = tempfile::tempdir().unwrap();
        let db = Database::in_memory().unwrap();
        for (channel,exe) in [("STEAM_CN","AstralParty_CN.exe"),("STEAM_GLOBAL","AstralParty_INT.exe"),("TAPTAP","吉星派对.exe"),("BILIBILI","吉星派对.exe"),("WEGAME","吉星派对.exe")] {
            let root = temp.path().join(channel); std::fs::create_dir(&root).unwrap();
            std::fs::write(root.join(exe), b"fixture").unwrap(); std::fs::write(root.join("GameAssembly.dll"), b"fixture").unwrap();
            let game = register(&db,channel,root.to_str().unwrap()).unwrap();
            assert_eq!(game.platform,channel);
            assert_eq!(register(&db,channel,root.to_str().unwrap()).unwrap().id,game.id);
        }
        let games = GameRepository::new(db).list_all().unwrap();
        assert_eq!(games.len(),5);
        let ids: std::collections::HashSet<_> = games.iter().map(|g| scoped_package_id("same.package",g)).collect();
        assert_eq!(ids.len(),5);
    }
}
