//! Android maintenance in the existing desktop Hub. All device writes use the
//! same hash-checked worker as the portable installer; no shell input is accepted.
use crate::domain::error::{HubError, HubResult};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{
    collections::BTreeMap,
    net::{IpAddr, SocketAddr},
    path::{Component, Path, PathBuf},
    process::Stdio,
    sync::Arc,
    time::Duration,
};
use tokio::{io::AsyncWriteExt, process::Command, sync::Mutex};

const WORKER: &str = "星枢安卓安装助手.exe";
const PHONE_APK: &str = "星枢手机安装助手.apk";

#[derive(Default)]
pub struct AndroidInstaller {
    gate: Arc<Mutex<()>>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct BridgeManifest {
    format_version: u32,
    runtime_version: String,
    files: BTreeMap<String, String>,
}

#[derive(Clone, Serialize, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct AndroidDevice {
    serial: String,
    state: String,
    model: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AndroidInstallerStatus {
    available: bool,
    adb_path: Option<String>,
    runtime_version: Option<String>,
    phone_helper_available: bool,
    devices: Vec<AndroidDevice>,
    message: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AndroidAction {
    action: String,
    serial: Option<String>,
    address: Option<String>,
    pairing_code: Option<String>,
}

fn error(message: impl Into<String>) -> HubError {
    HubError::new("ANDROID_INSTALLER", message, "ANDROID").with_recoverable(true)
}

fn package_root() -> HubResult<PathBuf> {
    let exe = std::env::current_exe().map_err(|e| error(e.to_string()))?;
    exe.ancestors()
        .skip(1)
        .take(4)
        .map(|p| p.join("Android"))
        .find(|p| p.join("bridge.json").is_file())
        .ok_or_else(|| {
            error("未找到配套安卓文件。请完整解压电脑版 ZIP，保留 Hub 与 Android 文件夹。")
        })
}

fn manifest(root: &Path) -> HubResult<BridgeManifest> {
    let text = std::fs::read(root.join("bridge.json")).map_err(|e| error(e.to_string()))?;
    let value: BridgeManifest =
        serde_json::from_slice(&text).map_err(|_| error("安卓文件清单损坏，请重新解压发布包。"))?;
    if value.format_version != 1 || value.runtime_version.is_empty() {
        return Err(error("安卓文件清单版本不支持。"));
    }
    Ok(value)
}

fn checked_file(
    root: &Path,
    files: &BTreeMap<String, String>,
    relative: &str,
) -> HubResult<PathBuf> {
    let path = Path::new(relative);
    if path.is_absolute()
        || path
            .components()
            .any(|c| !matches!(c, Component::Normal(_)))
    {
        return Err(error("安卓文件路径无效。"));
    }
    let hash = files
        .get(relative)
        .ok_or_else(|| error(format!("安卓清单缺少 {relative}")))?;
    let target = root
        .join(path)
        .canonicalize()
        .map_err(|_| error(format!("缺少 {relative}，请重新完整解压。")))?;
    let canonical_root = root.canonicalize().map_err(|e| error(e.to_string()))?;
    if !target.starts_with(canonical_root) {
        return Err(error("安卓文件路径超出安装包。"));
    }
    let bytes = std::fs::read(&target).map_err(|e| error(e.to_string()))?;
    if hash.len() != 64 || hex::encode(Sha256::digest(&bytes)) != *hash {
        return Err(error(format!("{relative} 校验不符，请重新解压发布包。")));
    }
    Ok(target)
}

fn adb_path(root: &Path, files: &BTreeMap<String, String>) -> HubResult<PathBuf> {
    // A packaged ADB always wins over PATH and emulator-specific installations.
    // Its companion DLLs are checked too: adb.exe alone is not a working install.
    for file in [
        "platform-tools/AdbWinApi.dll",
        "platform-tools/AdbWinUsbApi.dll",
    ] {
        checked_file(root, files, file)?;
    }
    checked_file(root, files, "platform-tools/adb.exe")
}

fn parse_devices(text: &str) -> Vec<AndroidDevice> {
    text.lines()
        .filter_map(|line| {
            let mut fields = line.split_whitespace();
            let serial = fields.next()?;
            let state = fields.next()?;
            if serial.starts_with('*')
                || serial == "List"
                || !["device", "offline", "unauthorized", "no"].contains(&state)
            {
                return None;
            }
            let model = fields
                .find_map(|field| field.strip_prefix("model:"))
                .unwrap_or("")
                .replace('_', " ");
            Some(AndroidDevice {
                serial: serial.to_owned(),
                state: if state == "no" {
                    "no permissions".into()
                } else {
                    state.into()
                },
                model,
            })
        })
        .collect()
}

fn validate_serial(serial: Option<&str>) -> HubResult<&str> {
    let value = serial.unwrap_or("");
    if value.is_empty()
        || value.len() > 256
        || value.starts_with('-')
        || !value
            .chars()
            .all(|c| c.is_ascii_alphanumeric() || "-_.:[]%".contains(c))
    {
        return Err(error("请先选择已授权的手机。"));
    }
    Ok(value)
}

fn local_address(value: Option<&str>) -> HubResult<String> {
    let address: SocketAddr = value
        .unwrap_or("")
        .trim()
        .parse()
        .map_err(|_| error("请输入无线调试页面的完整 IP:端口，例如 192.168.1.8:35001。"))?;
    let local = match address.ip() {
        IpAddr::V4(ip) => ip.is_private() || ip.is_loopback() || ip.is_link_local(),
        IpAddr::V6(ip) => ip.is_loopback() || ip.is_unique_local() || ip.is_unicast_link_local(),
    };
    if address.port() == 0 || !local {
        return Err(error("只连接本机或同一局域网内的手机，请核对 IP 和端口。"));
    }
    Ok(address.to_string())
}

async fn execute(
    file: &Path,
    args: &[&str],
    input: Option<&str>,
    seconds: u64,
) -> HubResult<String> {
    let mut command = Command::new(file);
    // A transaction worker must finish or roll back even if the Hub is closed.
    // Its individual ADB calls have their own timeout; never kill it mid-copy.
    command
        .args(args)
        .kill_on_drop(seconds != 0)
        .stdin(if input.is_some() {
            Stdio::piped()
        } else {
            Stdio::null()
        })
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    #[cfg(windows)]
    command.creation_flags(0x08000000); // CREATE_NO_WINDOW
    let mut child = command
        .spawn()
        .map_err(|e| error(format!("无法启动安卓安装组件：{e}")))?;
    if let Some(value) = input {
        if let Some(mut stream) = child.stdin.take() {
            stream
                .write_all(format!("{value}\n").as_bytes())
                .await
                .map_err(|e| error(e.to_string()))?;
        }
    }
    let wait = child.wait_with_output();
    let output = if seconds == 0 {
        wait.await
    } else {
        tokio::time::timeout(Duration::from_secs(seconds), wait)
            .await
            .map_err(|_| {
                error("连接或安装超时。请保持手机解锁并检查连接，再刷新设备；不会自动重试写入。")
            })?
    }
    .map_err(|e| error(e.to_string()))?;
    let text = format!(
        "{}\n{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    )
    .trim()
    .to_owned();
    let text = if let Some(secret) = input {
        text.replace(secret, "[已隐藏]")
    } else {
        text
    };
    if !output.status.success() {
        return Err(error(if text.is_empty() {
            "安卓操作未完成，请检查设备授权。".into()
        } else {
            text
        }));
    }
    Ok(text)
}

async fn devices(adb: &Path) -> HubResult<Vec<AndroidDevice>> {
    Ok(parse_devices(
        &execute(adb, &["devices", "-l"], None, 15).await?,
    ))
}

async fn require_device(adb: &Path, serial: &str) -> HubResult<()> {
    match devices(adb)
        .await?
        .iter()
        .find(|d| d.serial == serial)
        .map(|d| d.state.as_str())
    {
        Some("device") => Ok(()),
        Some("unauthorized") => Err(error("手机尚未授权，请在手机上允许 USB 调试后刷新设备。")),
        _ => Err(error("所选手机已离线，请重新连接并刷新设备。")),
    }
}

#[tauri::command]
pub async fn get_android_installer_status(
    state: tauri::State<'_, AndroidInstaller>,
) -> HubResult<AndroidInstallerStatus> {
    let _guard = state
        .gate
        .try_lock()
        .map_err(|_| error("安卓操作正在进行，请等待完成。"))?;
    let inspect = async {
        let root = package_root()?;
        let info = manifest(&root)?;
        let adb = adb_path(&root, &info.files)?;
        checked_file(&root, &info.files, WORKER)?;
        checked_file(&root, &info.files, "candidate.json")?;
        checked_file(&root, &info.files, "__data")?;
        checked_file(&root, &info.files, "vanilla.__data")?;
        let phone_helper_available = checked_file(&root, &info.files, PHONE_APK).is_ok();
        let devices = devices(&adb).await?;
        Ok::<_, HubError>(AndroidInstallerStatus {
            available: true,
            adb_path: Some(adb.display().to_string()),
            runtime_version: Some(info.runtime_version),
            phone_helper_available,
            message: if devices.is_empty() {
                "未发现设备。连接数据线并在手机允许 USB 调试，或展开无线连接。".into()
            } else {
                "请选择目标手机；未授权、离线设备不能安装。".into()
            },
            devices,
        })
    };
    match inspect.await {
        Ok(value) => Ok(value),
        Err(e) => Ok(AndroidInstallerStatus {
            available: false,
            adb_path: None,
            runtime_version: None,
            phone_helper_available: false,
            devices: vec![],
            message: e.message,
        }),
    }
}

#[tauri::command]
pub async fn android_installer_action(
    request: AndroidAction,
    state: tauri::State<'_, AndroidInstaller>,
) -> HubResult<String> {
    let _guard = state
        .gate
        .try_lock()
        .map_err(|_| error("安卓操作正在进行，请等待完成。"))?;
    let root = package_root()?;
    let info = manifest(&root)?;
    let adb = adb_path(&root, &info.files)?;
    match request.action.as_str() {
        "pair" => {
            let address = local_address(request.address.as_deref())?;
            let code = request.pairing_code.as_deref().unwrap_or("").trim();
            if code.len() != 6 || !code.bytes().all(|c| c.is_ascii_digit()) {
                return Err(error("请输入手机弹窗中的六位配对码。"));
            }
            let result = execute(&adb, &["pair", &address], Some(code), 30).await?;
            if !result.to_ascii_lowercase().contains("successfully paired") {
                return Err(error(result));
            }
            Ok("配对成功。请填写无线调试主页面的连接地址，再点“连接手机”。".into())
        }
        "connect" => {
            let address = local_address(request.address.as_deref())?;
            let result = execute(&adb, &["connect", &address], None, 20).await?;
            if !result.to_ascii_lowercase().contains("connected to") {
                return Err(error(result));
            }
            Ok(result)
        }
        "check" | "install" | "restore" | "install-helper" => {
            let serial = validate_serial(request.serial.as_deref())?;
            require_device(&adb, serial).await?;
            if request.action == "install-helper" {
                let apk = checked_file(&root, &info.files, PHONE_APK)?;
                let result = execute(
                    &adb,
                    &["-s", serial, "install", "-r", &apk.to_string_lossy()],
                    None,
                    180,
                )
                .await?;
                if !result.lines().any(|line| line.trim() == "Success") {
                    return Err(error(result));
                }
                return Ok(
                    "星枢手机安装助手已安装。可在手机桌面打开它，之后使用手机本机无线调试更新。"
                        .into(),
                );
            }
            let worker = checked_file(&root, &info.files, WORKER)?;
            let candidate = checked_file(&root, &info.files, "candidate.json")?;
            checked_file(&root, &info.files, "__data")?;
            checked_file(&root, &info.files, "vanilla.__data")?;
            execute(
                &worker,
                &[
                    &format!("--{}", request.action),
                    "--manifest",
                    &candidate.to_string_lossy(),
                    "--adb",
                    &adb.to_string_lossy(),
                    "--serial",
                    serial,
                ],
                None,
                0,
            )
            .await
        }
        _ => Err(error("不支持的安卓操作。")),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn distinguishes_usb_wireless_unauthorized_and_offline() {
        let parsed = parse_devices(
            "* daemon started successfully *\nList of devices attached\nUSB123 device product:shennong model:Xiaomi_14_Pro device:shennong\n192.168.31.87:33399 unauthorized\nemulator-5554 offline\n",
        );
        assert_eq!(parsed.len(), 3);
        assert_eq!(parsed[0].model, "Xiaomi 14 Pro");
        assert_eq!(parsed[1].state, "unauthorized");
        assert_eq!(parsed[2].state, "offline");
    }
    #[test]
    fn connection_only_accepts_local_complete_addresses() {
        for address in [
            "192.168.31.87:33399",
            "127.0.0.1:16384",
            "[::1]:37001",
            "10.0.0.5:5555",
        ] {
            assert!(local_address(Some(address)).is_ok());
        }
        for address in [
            "33399",
            "192.168.31.87",
            "192.168.31.87:0",
            "8.8.8.8:5555",
            "--help",
            "192.168.1.1:5555;echo hi",
        ] {
            assert!(local_address(Some(address)).is_err());
        }
        assert!(validate_serial(Some("192.168.31.87:33399")).is_ok());
        assert!(validate_serial(Some("-s other")).is_err());
    }
    #[test]
    fn refuses_missing_modified_and_escaped_payloads() {
        let root = tempfile::tempdir().unwrap();
        std::fs::write(root.path().join("worker.exe"), b"expected").unwrap();
        let files = BTreeMap::from([(
            "worker.exe".to_owned(),
            hex::encode(Sha256::digest(b"expected")),
        )]);
        assert!(checked_file(root.path(), &files, "worker.exe").is_ok());
        assert!(checked_file(root.path(), &files, "../worker.exe").is_err());
        assert!(checked_file(root.path(), &files, "missing.exe").is_err());
        std::fs::write(root.path().join("worker.exe"), b"changed").unwrap();
        assert!(checked_file(root.path(), &files, "worker.exe").is_err());
    }
}
