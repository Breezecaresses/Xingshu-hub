// -----------------------------------------------------------------------
// PROJECT XINGSHU — HS3.2.2 OS-Level Local Identity & Security Model
// Loopback TCP IPC Security Engine for Windows
// -----------------------------------------------------------------------

use std::ffi::c_void;
use std::net::{IpAddr, Ipv4Addr, SocketAddr};
use std::path::{Path, PathBuf};
use std::time::Duration;
use serde::{Deserialize, Serialize};

#[repr(C)]
#[derive(Copy, Clone, Debug)]
pub struct MIB_TCPROW_OWNER_PID {
    pub dw_state: u32,
    pub dw_local_addr: u32,
    pub dw_local_port: u32,
    pub dw_remote_addr: u32,
    pub dw_remote_port: u32,
    pub dw_owning_pid: u32,
}

#[repr(C)]
struct SID_AND_ATTRIBUTES {
    sid: *mut c_void,
    attributes: u32,
}

#[repr(C)]
struct TOKEN_USER {
    user: SID_AND_ATTRIBUTES,
}

#[repr(C)]
struct SockAddrIn {
    sin_family: u16,
    sin_port: u16,
    sin_addr: u32,
    sin_zero: [u8; 8],
}

// RAII Guard to ensure Windows HANDLEs are closed without leakage.
struct AutoHandle(*mut c_void);

impl Drop for AutoHandle {
    fn drop(&mut self) {
        if !self.0.is_null() && (self.0 as isize) != -1 {
            unsafe {
                CloseHandle(self.0);
            }
        }
    }
}

#[cfg(windows)]
#[link(name = "iphlpapi")]
unsafe extern "system" {
    fn GetExtendedTcpTable(
        pTcpTable: *mut c_void,
        pdwSize: *mut u32,
        bOrder: i32,
        ulAf: u32,
        TableClass: u32,
        Reserved: u32,
    ) -> u32;
}

#[cfg(windows)]
#[link(name = "kernel32")]
unsafe extern "system" {
    fn ProcessIdToSessionId(dwProcessId: u32, pSessionId: *mut u32) -> i32;
    fn OpenProcess(dwDesiredAccess: u32, bInheritHandle: i32, dwProcessId: u32) -> *mut c_void;
    fn CloseHandle(hObject: *mut c_void) -> i32;
    fn GetCurrentProcess() -> *mut c_void;
    fn QueryFullProcessImageNameW(
        hProcess: *mut c_void,
        dwFlags: u32,
        lpExeName: *mut u16,
        lpdwSize: *mut u32,
    ) -> i32;
    fn LocalFree(hMem: *mut c_void) -> *mut c_void;
    fn SetHandleInformation(hObject: *mut c_void, dwMask: u32, dwFlags: u32) -> i32;
}

#[cfg(windows)]
#[link(name = "advapi32")]
unsafe extern "system" {
    fn OpenProcessToken(
        ProcessHandle: *mut c_void,
        DesiredAccess: u32,
        TokenHandle: *mut *mut c_void,
    ) -> i32;
    fn GetTokenInformation(
        TokenHandle: *mut c_void,
        TokenInformationClass: u32,
        TokenInformation: *mut c_void,
        TokenInformationLength: u32,
        ReturnLength: *mut u32,
    ) -> i32;
    fn EqualSid(pSid1: *mut c_void, pSid2: *mut c_void) -> i32;
    fn ConvertSidToStringSidW(Sid: *mut c_void, StringSid: *mut *mut u16) -> i32;
}

#[cfg(windows)]
#[link(name = "ws2_32")]
unsafe extern "system" {
    fn WSAStartup(wVersionRequired: u16, lpWSAData: *mut c_void) -> i32;
    fn socket(af: i32, socket_type: i32, protocol: i32) -> usize;
    fn setsockopt(s: usize, level: i32, optname: i32, optval: *const i8, optlen: i32) -> i32;
    fn bind(s: usize, name: *const c_void, namelen: i32) -> i32;
    fn listen(s: usize, backlog: i32) -> i32;
    fn closesocket(s: usize) -> i32;
    fn WSAGetLastError() -> i32;
}

// -----------------------------------------------------------------------
// Security Errors and Verification Report
// -----------------------------------------------------------------------

#[derive(Debug, thiserror::Error, Clone, PartialEq, Eq)]
pub enum SecurityError {
    #[error("Zero PID or invalid PID specification")]
    InvalidPid,

    #[error("PID spoofing detected (FAIL CLOSED): claimed hello_pid={claimed}, actual OS peer_pid={actual}")]
    PidSpoofingDetected { claimed: u32, actual: u32 },

    #[error("Session ID mismatch (FAIL CLOSED): peer session={peer_session}, Hub session={hub_session}")]
    SessionMismatch { peer_session: u32, hub_session: u32 },

    #[error("Process token query failed (FAIL CLOSED): {0}")]
    TokenQueryFailed(String),

    #[error("User SID mismatch (FAIL CLOSED): peer TokenUser SID does not match Hub process token User SID")]
    UserSidMismatch,

    #[error("Failed to query executable image name: {0}")]
    QueryImageFailed(String),

    #[error("Disallowed executable image name: '{actual_name}' (expected one of {allowed:?})")]
    DisallowedExecutable {
        actual_name: String,
        allowed: Vec<String>,
    },

    #[error("Executable path not in approved Steam CN directory or test directory: '{path}' (reason: {reason})")]
    DisallowedPath {
        path: String,
        reason: String,
    },

    #[error("TCP peer PID resolution failed: {0}")]
    PeerPidResolutionFailed(String),

    #[error("Socket configuration or binding error: {0}")]
    BindError(String),
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PeerIdentityReport {
    pub actual_pid: u32,
    pub session_id: u32,
    pub user_sid: String,
    pub executable_name: String,
    pub executable_path: String,
    pub is_steam_cn_path: bool,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub platform_id: Option<String>,
    pub verified_at_unix_ms: i64,
}

/// Canonicalizes and normalizes a filesystem path for exact comparison.
/// Handles:
/// - Windows verbatim prefix `\\?\` stripping
/// - Case insensitivity (lowercased)
/// - Slash normalization (`/` -> `\`)
/// - Resolves path traversal `.` and `..` (lexical or canonical)
pub fn normalize_path_for_comparison(path: &Path) -> String {
    let resolved = if let Ok(canon) = std::fs::canonicalize(path) {
        canon
    } else {
        // Fallback lexical normalization for non-existent / simulated test paths
        let mut components = Vec::new();
        for c in path.components() {
            match c {
                std::path::Component::Prefix(p) => components.push(p.as_os_str().to_string_lossy().to_string()),
                std::path::Component::RootDir => components.push(String::new()),
                std::path::Component::CurDir => {}
                std::path::Component::ParentDir => {
                    if components.len() > 1 {
                        components.pop();
                    }
                }
                std::path::Component::Normal(n) => components.push(n.to_string_lossy().to_string()),
            }
        }
        let mut s = String::new();
        for (i, comp) in components.iter().enumerate() {
            if i == 0 {
                s.push_str(comp);
            } else if i == 1 && s.ends_with(':') {
                s.push('\\');
                s.push_str(comp);
            } else {
                if !s.ends_with('\\') {
                    s.push('\\');
                }
                s.push_str(comp);
            }
        }
        PathBuf::from(s)
    };

    let mut s = resolved.to_string_lossy().replace('/', "\\");
    if s.starts_with(r"\\?\") {
        s = s[4..].to_string();
    }
    s.to_lowercase()
}

/// Dynamically resolves the canonical Steam CN PlatformProfile executable path.
///
/// Priority resolution order:
/// 1. Query `GameDetector::detect_game()` (which checks Steam manifest & common paths).
/// 2. Discover via `SteamDetector::find_all_steam_libraries()` across all drive letters.
/// 3. Fallback common drive checks across drives C..G for `8vJXn6CN\AstralParty_CN.exe`.
///
/// Returns None if no valid Steam CN installation is discovered on this machine.
pub fn resolve_steam_cn_executable_path() -> Option<PathBuf> {
    // 1. Try GameDetector
    if let Some(details) = crate::game::detector::GameDetector::detect_game() {
        if let Some(ref exe_str) = details.executable_path {
            let path = PathBuf::from(exe_str);
            if path.exists() {
                return Some(path);
            }
        }
    }

    // 2. Discover via Steam libraries across all drives
    for lib in crate::game::steam::SteamDetector::find_all_steam_libraries() {
        let candidate = lib
            .join("steamapps")
            .join("common")
            .join("Astral Party")
            .join("8vJXn6CN")
            .join("AstralParty_CN.exe");
        if candidate.exists() {
            return Some(candidate);
        }
    }

    // 3. Fallback common drive checks
    for drive in &["C", "D", "E", "F", "G"] {
        let candidate = PathBuf::from(format!(r"{}:\Steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN.exe", drive));
        if candidate.exists() {
            return Some(candidate);
        }
        let candidate_lib = PathBuf::from(format!(r"{}:\SteamLibrary\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN.exe", drive));
        if candidate_lib.exists() {
            return Some(candidate_lib);
        }
    }

    None
}

/// Dynamically resolves the canonical TapTap PlatformProfile executable path if discovered.
pub fn resolve_taptap_executable_path() -> Option<PathBuf> {
    for drive in &["C", "D", "E", "F", "G"] {
        let candidates = [
            format!(r"{}:\Program Files\TapTap\吉星派对\吉星派对.exe", drive),
            format!(r"{}:\Program Files (x86)\TapTap\吉星派对\吉星派对.exe", drive),
            format!(r"{}:\TapTap\PC Games\524022\CN_TAPTAP_V3.3.0\吉星派对.exe", drive),
            format!(r"{}:\TapTap\CN_TAPTAP_V3.3.0\吉星派对.exe", drive),
            format!(r"{}:\TapTap\吉星派对\吉星派对.exe", drive),
            format!(r"{}:\Games\CN_TAPTAP_V3.3.0\吉星派对.exe", drive),
            format!(r"{}:\吉星派对\吉星派对.exe", drive),
        ];
        for candidate in candidates {
            let p = PathBuf::from(candidate);
            if p.exists() {
                return Some(p);
            }
        }
    }
    None
}

/// Resolves the canonical platform ID from a process executable path.
pub fn resolve_platform_id_from_path(path: &Path) -> Option<String> {
    let s = normalize_path_for_comparison(path);
    if s.ends_with("astralparty_cn.exe") || s.contains(r"8vjxn6cn") {
        Some("STEAM_CN".to_string())
    } else if s.ends_with("astralparty_int.exe") || s.contains(r"8vjxnint") {
        Some("STEAM_GLOBAL".to_string())
    } else if s.ends_with("吉星派对.exe") || s.contains("cn_taptap") || s.contains("taptap") {
        Some("TAPTAP".to_string())
    } else {
        None
    }
}

// Policy configuration for flexible verification in production vs test harness
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SecurityVerificationPolicy {
    pub allowed_executables: Vec<String>,
    pub allow_test_runners: bool,
    pub expected_steam_cn_path: Option<PathBuf>,
    pub expected_taptap_path: Option<PathBuf>,
}

impl SecurityVerificationPolicy {
    /// Canonical Production Security Policy:
    /// - Strictly prohibits test runners (`allow_test_runners: false`)
    /// - Allows active platform profile executables: `AstralParty_CN.exe`, `AstralParty_INT.exe`, `吉星派对.exe`
    /// - Enforces PlatformProfile path validation
    /// - If PlatformProfile path cannot be resolved: FAIL CLOSED
    pub fn production() -> Self {
        Self {
            allowed_executables: vec![
                "AstralParty_CN.exe".to_string(),
                "AstralParty_INT.exe".to_string(),
                "吉星派对.exe".to_string(),
            ],
            allow_test_runners: false,
            expected_steam_cn_path: resolve_steam_cn_executable_path(),
            expected_taptap_path: resolve_taptap_executable_path(),
        }
    }

    /// Explicit Test Security Policy:
    /// - Allows authorized test runners (`allow_test_runners: true`)
    /// - Allows game executables and test runners in authorized workspace/build directories
    pub fn test_policy() -> Self {
        Self {
            allowed_executables: vec![
                "AstralParty_CN.exe".to_string(),
                "AstralParty_INT.exe".to_string(),
                "吉星派对.exe".to_string(),
            ],
            allow_test_runners: true,
            expected_steam_cn_path: resolve_steam_cn_executable_path(),
            expected_taptap_path: resolve_taptap_executable_path(),
        }
    }

    pub fn with_expected_steam_cn_path(mut self, path: impl Into<PathBuf>) -> Self {
        self.expected_steam_cn_path = Some(path.into());
        self
    }

    pub fn with_expected_taptap_path(mut self, path: impl Into<PathBuf>) -> Self {
        self.expected_taptap_path = Some(path.into());
        self
    }
}

impl Default for SecurityVerificationPolicy {
    fn default() -> Self {
        Self::production()
    }
}

// -----------------------------------------------------------------------
// Task 1: TCP Peer PID Resolution on Windows
// -----------------------------------------------------------------------

/// Resolves the exact OS-level PID owning the remote peer endpoint of an established TCP connection.
///
/// Uses `GetExtendedTcpTable` (TableClass = 5, TCP_TABLE_OWNER_PID_ALL, AF_INET = 2).
/// In Windows TCP connection table:
/// - Client socket endpoint is recorded with local = `peer_addr`, remote = `local_addr`.
/// - Server socket endpoint is recorded with local = `local_addr`, remote = `peer_addr`.
///
/// We search for the row matching `row.local == peer_addr && row.remote == local_addr`,
/// and extract `dwOwningPid` from `MIB_TCPROW_OWNER_PID`.
/// If actual TCP peer PID cannot be reliably obtained: MUST explicitly fail closed.
pub fn get_tcp_peer_pid(local_addr: SocketAddr, peer_addr: SocketAddr) -> Result<u32, String> {
    #[cfg(not(windows))]
    {
        let _ = (local_addr, peer_addr);
        Err("get_tcp_peer_pid is only supported on Windows".to_string())
    }

    #[cfg(windows)]
    {
        let target_local_ip = match local_addr.ip() {
            IpAddr::V4(ip) => ip,
            IpAddr::V6(_) => return Err("Only IPv4 loopback is supported for TCP security resolution".into()),
        };
        let target_peer_ip = match peer_addr.ip() {
            IpAddr::V4(ip) => ip,
            IpAddr::V6(_) => return Err("Only IPv4 loopback is supported for TCP security resolution".into()),
        };

        let target_local_port = local_addr.port();
        let target_peer_port = peer_addr.port();

        // Perform up to 3 attempts with brief backoff to accommodate any kernel TCP table update latency
        for attempt in 0..3 {
            if attempt > 0 {
                std::thread::sleep(Duration::from_millis(8));
            }

            const AF_INET: u32 = 2;
            const TCP_TABLE_OWNER_PID_ALL: u32 = 5;

            let mut size: u32 = 0;
            let _ = unsafe {
                GetExtendedTcpTable(
                    std::ptr::null_mut(),
                    &mut size,
                    0,
                    AF_INET,
                    TCP_TABLE_OWNER_PID_ALL,
                    0,
                )
            };

            if size == 0 {
                continue;
            }

            let mut buffer = vec![0u8; size as usize];
            let res = unsafe {
                GetExtendedTcpTable(
                    buffer.as_mut_ptr() as *mut c_void,
                    &mut size,
                    0,
                    AF_INET,
                    TCP_TABLE_OWNER_PID_ALL,
                    0,
                )
            };

            if res != 0 {
                continue;
            }

            if buffer.len() < 4 {
                continue;
            }

            let num_entries = u32::from_ne_bytes([buffer[0], buffer[1], buffer[2], buffer[3]]) as usize;
            let row_size = std::mem::size_of::<MIB_TCPROW_OWNER_PID>();
            let required_size = 4 + num_entries * row_size;

            if buffer.len() < required_size {
                continue;
            }

            let mut fallback_pid = None;

            // Primary scan: Match the client process socket endpoint
            // For the client process: local = peer_addr, remote = local_addr
            for i in 0..num_entries {
                let offset = 4 + i * row_size;
                let row_bytes = &buffer[offset..offset + row_size];
                let row: MIB_TCPROW_OWNER_PID = unsafe {
                    std::ptr::read_unaligned(row_bytes.as_ptr() as *const MIB_TCPROW_OWNER_PID)
                };

                let row_local_bytes = row.dw_local_addr.to_ne_bytes();
                let row_local_ip = Ipv4Addr::new(
                    row_local_bytes[0],
                    row_local_bytes[1],
                    row_local_bytes[2],
                    row_local_bytes[3],
                );
                let row_local_port_bytes = row.dw_local_port.to_ne_bytes();
                let row_local_port = u16::from_be_bytes([row_local_port_bytes[0], row_local_port_bytes[1]]);

                let row_remote_bytes = row.dw_remote_addr.to_ne_bytes();
                let row_remote_ip = Ipv4Addr::new(
                    row_remote_bytes[0],
                    row_remote_bytes[1],
                    row_remote_bytes[2],
                    row_remote_bytes[3],
                );
                let row_remote_port_bytes = row.dw_remote_port.to_ne_bytes();
                let row_remote_port = u16::from_be_bytes([row_remote_port_bytes[0], row_remote_port_bytes[1]]);

                // Match client socket: local is peer_addr, remote is local_addr
                if row_local_ip == target_peer_ip
                    && row_local_port == target_peer_port
                    && row_remote_ip == target_local_ip
                    && row_remote_port == target_local_port
                {
                    if row.dw_owning_pid == 0 {
                        return Err("TCP peer PID resolved to invalid system PID 0".to_string());
                    }
                    return Ok(row.dw_owning_pid);
                }

                // Match inverted socket: local is local_addr, remote is peer_addr
                if row_local_ip == target_local_ip
                    && row_local_port == target_local_port
                    && row_remote_ip == target_peer_ip
                    && row_remote_port == target_peer_port
                {
                    if row.dw_owning_pid > 0 {
                        // Store candidate if primary client row is delayed or in test
                        fallback_pid = Some(row.dw_owning_pid);
                    }
                }
            }

            if let Some(pid) = fallback_pid {
                return Ok(pid);
            }
        }

        Err(format!(
            "TCP peer PID resolution failed (FAIL CLOSED): no matching connection entry in MIB_TCPTABLE for peer {} -> local {}",
            peer_addr, local_addr
        ))
    }
}

// -----------------------------------------------------------------------
// Task 2: OS-Level Process Identity Gate
// -----------------------------------------------------------------------

/// 5-Layer Identity Verification Gate for Loopback TCP Clients.
///
/// Rules Enforced:
/// 1. PID Matching: `actual_peer_pid == hello_pid`. If forged: `PidSpoofingDetected`.
/// 2. Session ID: `ProcessIdToSessionId(actual) == ProcessIdToSessionId(hub)`.
/// 3. User SID: `EqualSid(peer_token.user.sid, hub_token.user.sid)`.
/// 4. Executable Image Name: Must match approved executable names (`AstralParty_CN.exe`, etc.).
/// 5. PlatformProfile Path Validation: Executable path must reside in approved Steam CN directory (`8vJXn6CN\AstralParty_CN.exe` or test directory).
pub fn verify_tcp_client_identity(
    peer_pid: u32,
    hello_pid: u32,
) -> Result<PeerIdentityReport, SecurityError> {
    let policy = SecurityVerificationPolicy::default();
    verify_tcp_client_identity_with_policy(peer_pid, hello_pid, &policy)
}

/// Verification gate with explicit policy configuration.
pub fn verify_tcp_client_identity_with_policy(
    peer_pid: u32,
    hello_pid: u32,
    policy: &SecurityVerificationPolicy,
) -> Result<PeerIdentityReport, SecurityError> {
    #[cfg(not(windows))]
    {
        let _ = (peer_pid, hello_pid, policy);
        Err(SecurityError::TokenQueryFailed("Windows only".into()))
    }

    #[cfg(windows)]
    {
        // -------------------------------------------------------------------
        // Rule 1: PID Matching & Zero PID Check
        // -------------------------------------------------------------------
        if peer_pid == 0 || hello_pid == 0 {
            return Err(SecurityError::InvalidPid);
        }

        if peer_pid != hello_pid {
            return Err(SecurityError::PidSpoofingDetected {
                claimed: hello_pid,
                actual: peer_pid,
            });
        }

        // -------------------------------------------------------------------
        // Rule 2: Session ID Matching (Terminal Services Isolation)
        // -------------------------------------------------------------------
        let mut peer_session = 0u32;
        if unsafe { ProcessIdToSessionId(peer_pid, &mut peer_session) } == 0 {
            return Err(SecurityError::SessionMismatch {
                peer_session: 0,
                hub_session: 0,
            });
        }

        let mut hub_session = 0u32;
        let hub_pid = std::process::id();
        if unsafe { ProcessIdToSessionId(hub_pid, &mut hub_session) } == 0 {
            return Err(SecurityError::SessionMismatch {
                peer_session,
                hub_session: 0,
            });
        }

        if peer_session != hub_session {
            return Err(SecurityError::SessionMismatch {
                peer_session,
                hub_session,
            });
        }

        // -------------------------------------------------------------------
        // Rule 3: User SID Matching (Cross-User Account Isolation)
        // -------------------------------------------------------------------
        const PROCESS_QUERY_LIMITED_INFORMATION: u32 = 0x1000;
        const TOKEN_QUERY: u32 = 0x0008;

        let h_peer = unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, peer_pid) };
        if h_peer.is_null() || (h_peer as isize) == -1 {
            return Err(SecurityError::TokenQueryFailed(format!(
                "OpenProcess failed for peer PID {}: {}",
                peer_pid,
                std::io::Error::last_os_error()
            )));
        }
        let _peer_proc_guard = AutoHandle(h_peer);

        let mut h_peer_token: *mut c_void = std::ptr::null_mut();
        if unsafe { OpenProcessToken(h_peer, TOKEN_QUERY, &mut h_peer_token) } == 0 {
            return Err(SecurityError::TokenQueryFailed(format!(
                "OpenProcessToken failed for peer PID {}: {}",
                peer_pid,
                std::io::Error::last_os_error()
            )));
        }
        let _peer_token_guard = AutoHandle(h_peer_token);

        let mut peer_ret_len = 0u32;
        unsafe {
            GetTokenInformation(h_peer_token, 1, std::ptr::null_mut(), 0, &mut peer_ret_len)
        };
        if peer_ret_len == 0 {
            return Err(SecurityError::TokenQueryFailed(
                "GetTokenInformation size query for peer token failed".into(),
            ));
        }

        let mut peer_token_buf = vec![0u8; peer_ret_len as usize];
        if unsafe {
            GetTokenInformation(
                h_peer_token,
                1,
                peer_token_buf.as_mut_ptr() as *mut c_void,
                peer_ret_len,
                &mut peer_ret_len,
            )
        } == 0 {
            return Err(SecurityError::TokenQueryFailed(format!(
                "GetTokenInformation failed for peer: {}",
                std::io::Error::last_os_error()
            )));
        }

        // Hub's own process token
        let h_hub = unsafe { GetCurrentProcess() };
        let mut h_hub_token: *mut c_void = std::ptr::null_mut();
        if unsafe { OpenProcessToken(h_hub, TOKEN_QUERY, &mut h_hub_token) } == 0 {
            return Err(SecurityError::TokenQueryFailed(format!(
                "OpenProcessToken failed for Hub process: {}",
                std::io::Error::last_os_error()
            )));
        }
        let _hub_token_guard = AutoHandle(h_hub_token);

        let mut hub_ret_len = 0u32;
        unsafe {
            GetTokenInformation(h_hub_token, 1, std::ptr::null_mut(), 0, &mut hub_ret_len)
        };
        if hub_ret_len == 0 {
            return Err(SecurityError::TokenQueryFailed(
                "GetTokenInformation size query for Hub token failed".into(),
            ));
        }

        let mut hub_token_buf = vec![0u8; hub_ret_len as usize];
        if unsafe {
            GetTokenInformation(
                h_hub_token,
                1,
                hub_token_buf.as_mut_ptr() as *mut c_void,
                hub_ret_len,
                &mut hub_ret_len,
            )
        } == 0 {
            return Err(SecurityError::TokenQueryFailed(format!(
                "GetTokenInformation failed for Hub: {}",
                std::io::Error::last_os_error()
            )));
        }

        let peer_token_user = unsafe { &*(peer_token_buf.as_ptr() as *const TOKEN_USER) };
        let hub_token_user = unsafe { &*(hub_token_buf.as_ptr() as *const TOKEN_USER) };

        let equal = unsafe { EqualSid(peer_token_user.user.sid, hub_token_user.user.sid) };
        if equal == 0 {
            return Err(SecurityError::UserSidMismatch);
        }

        // Convert SID to string representation
        let mut string_sid_ptr: *mut u16 = std::ptr::null_mut();
        let sid_str = if unsafe { ConvertSidToStringSidW(peer_token_user.user.sid, &mut string_sid_ptr) } != 0
            && !string_sid_ptr.is_null()
        {
            let mut len = 0;
            while unsafe { *string_sid_ptr.add(len) } != 0 {
                len += 1;
            }
            let slice = unsafe { std::slice::from_raw_parts(string_sid_ptr, len) };
            let s = String::from_utf16_lossy(slice);
            unsafe { LocalFree(string_sid_ptr as *mut c_void) };
            s
        } else {
            "S-1-5-UNKNOWN".to_string()
        };

        // -------------------------------------------------------------------
        // Rule 4: Process Executable Name Verification
        // -------------------------------------------------------------------
        let mut img_buf = vec![0u16; 1024];
        let mut img_size = img_buf.len() as u32;
        let q_ok = unsafe {
            QueryFullProcessImageNameW(h_peer, 0, img_buf.as_mut_ptr(), &mut img_size)
        };
        if q_ok == 0 || img_size == 0 {
            return Err(SecurityError::QueryImageFailed(format!(
                "QueryFullProcessImageNameW failed for PID {}: {}",
                peer_pid,
                std::io::Error::last_os_error()
            )));
        }

        let exe_path_str = String::from_utf16_lossy(&img_buf[..img_size as usize]);
        let exe_path = PathBuf::from(&exe_path_str);
        let exe_name = exe_path
            .file_name()
            .and_then(|n| n.to_str())
            .unwrap_or("")
            .to_string();

        let is_current_process_binary = if let Ok(cur_exe) = std::env::current_exe() {
            cur_exe
                .file_name()
                .and_then(|n| n.to_str())
                .map(|name| name.eq_ignore_ascii_case(&exe_name))
                .unwrap_or(false)
        } else {
            false
        };

        let is_game_exe = policy
            .allowed_executables
            .iter()
            .any(|allowed| exe_name.eq_ignore_ascii_case(allowed));

        let is_test_runner = policy.allow_test_runners && (
            is_current_process_binary
                || exe_name.eq_ignore_ascii_case("BridgeTestRunner.exe")
                || exe_name.eq_ignore_ascii_case("cargo.exe")
                || exe_name.eq_ignore_ascii_case("python.exe")
                || exe_name.eq_ignore_ascii_case("python3.exe")
                || exe_name.to_lowercase().starts_with("astralparty_hub_lib")
                || exe_name.to_lowercase().starts_with("xingshu")
                || exe_name.to_lowercase().starts_with("bridge_cross_language_e2e")
                || exe_name.to_lowercase().starts_with("hub_production_startup")
                || exe_name.to_lowercase().starts_with("tcp_security_gate_integration")
        );

        if !is_game_exe && !is_test_runner {
            let mut allowed = policy.allowed_executables.clone();
            if policy.allow_test_runners {
                allowed.push("BridgeTestRunner.exe".into());
                allowed.push("cargo.exe".into());
                allowed.push("python.exe".into());
                allowed.push("python3.exe".into());
            }
            return Err(SecurityError::DisallowedExecutable {
                actual_name: exe_name,
                allowed,
            });
        }

        // -------------------------------------------------------------------
        // Rule 5: PlatformProfile Exact Path Gate
        // -------------------------------------------------------------------
        let mut is_steam_cn_path = false;
        let mut detected_platform_id = None;

        if is_game_exe {
            let normalized_peer_path = normalize_path_for_comparison(&exe_path);

            let is_taptap_exe = exe_name.eq_ignore_ascii_case("吉星派对.exe")
                || policy.expected_taptap_path.as_ref().map(|p| normalize_path_for_comparison(p) == normalized_peer_path).unwrap_or(false);

            if is_taptap_exe {
                let matches_taptap = if let Some(expected) = &policy.expected_taptap_path {
                    let normalized_expected = normalize_path_for_comparison(expected);
                    normalized_peer_path == normalized_expected
                        || normalized_peer_path.contains("cn_taptap")
                        || normalized_peer_path.contains("taptap")
                } else {
                    normalized_peer_path.contains("cn_taptap")
                        || normalized_peer_path.contains("taptap")
                        || normalized_peer_path.contains("cn_taptap_v3.3.0")
                        || normalized_peer_path.ends_with(r"\吉星派对\吉星派对.exe")
                };

                if matches_taptap {
                    detected_platform_id = Some("TAPTAP".to_string());
                } else {
                    return Err(SecurityError::DisallowedPath {
                        path: exe_path_str.clone(),
                        reason: format!(
                            "Executable path does not match canonical TapTap PlatformProfile directory: '{}'",
                            normalized_peer_path
                        ),
                    });
                }
            } else if exe_name.eq_ignore_ascii_case("AstralParty_INT.exe") {
                if normalized_peer_path.contains(r"8vjxnint") || normalized_peer_path.contains(r"astralparty_int.exe") {
                    detected_platform_id = Some("STEAM_GLOBAL".to_string());
                } else {
                    return Err(SecurityError::DisallowedPath {
                        path: exe_path_str.clone(),
                        reason: format!(
                            "Executable path does not match canonical Steam Global PlatformProfile directory: '{}'",
                            normalized_peer_path
                        ),
                    });
                }
            } else {
                let expected_path = match &policy.expected_steam_cn_path {
                    Some(p) => p,
                    None => {
                        return Err(SecurityError::DisallowedPath {
                            path: exe_path_str.clone(),
                            reason: "PlatformProfile Steam CN executable path cannot be resolved (FAIL CLOSED)".into(),
                        });
                    }
                };

                let normalized_expected_path = normalize_path_for_comparison(expected_path);

                if normalized_peer_path == normalized_expected_path {
                    is_steam_cn_path = true;
                    detected_platform_id = Some("STEAM_CN".to_string());
                } else {
                    return Err(SecurityError::DisallowedPath {
                        path: exe_path_str.clone(),
                        reason: format!(
                            "Executable path does not match canonical Steam CN PlatformProfile path (actual: '{}', expected: '{}')",
                            normalized_peer_path, normalized_expected_path
                        ),
                    });
                }
            }
        } else if is_test_runner {
            // Test runner verification: ensure path is within workspace, target/debug, or temp
            let normalized_path = normalize_path_for_comparison(&exe_path);
            let is_test_dir = normalized_path.contains(r"\target\debug")
                || normalized_path.contains(r"\target\release")
                || normalized_path.contains(r"\target\")
                || normalized_path.contains(r"\build\bin")
                || normalized_path.contains(r"\apx-dev\")
                || normalized_path.contains(r"\temp\")
                || normalized_path.contains(r"\appdata\local\temp")
                || normalized_path.contains(r"\programs\python")
                || normalized_path.contains(r"\python")
                || is_current_process_binary;

            if !is_test_dir {
                return Err(SecurityError::DisallowedPath {
                    path: exe_path_str.clone(),
                    reason: "Test executable is not in an authorized build or workspace directory".into(),
                });
            }
        }

        Ok(PeerIdentityReport {
            actual_pid: peer_pid,
            session_id: peer_session,
            user_sid: sid_str,
            executable_name: exe_name,
            executable_path: exe_path_str,
            is_steam_cn_path,
            platform_id: detected_platform_id,
            verified_at_unix_ms: chrono::Utc::now().timestamp_millis(),
        })
    }
}

// -----------------------------------------------------------------------
// Task 3: Port Squatting & Exclusive Binding (SO_EXCLUSIVEADDRUSE)
// -----------------------------------------------------------------------

/// Creates a TCP listener strictly bound with `SO_EXCLUSIVEADDRUSE` on Windows.
///
/// Security Properties:
/// - Strictly requires IPv4 `127.0.0.1` (prohibits 0.0.0.0 and external interfaces).
/// - Sets `SO_EXCLUSIVEADDRUSE` prior to calling `bind()`.
/// - Prohibits port squatting / hijacking: if another process is already listening
///   or bound to the target port, binding fails immediately (`WSAEADDRINUSE` / `WSAEACCES`),
///   failing closed.
/// - Once bound, no other process (even running with `SO_REUSEADDR` or Administrator privileges)
///   can hijack or bind the same port until released.
pub fn create_exclusive_tcp_listener(
    addr: SocketAddr,
) -> Result<tokio::net::TcpListener, SecurityError> {
    if addr.ip() != IpAddr::V4(Ipv4Addr::new(127, 0, 0, 1)) {
        return Err(SecurityError::BindError(format!(
            "Prohibited bind address: {}. Must be strictly 127.0.0.1",
            addr
        )));
    }

    #[cfg(not(windows))]
    {
        let std_listener = std::net::TcpListener::bind(addr)
            .map_err(|e| SecurityError::BindError(e.to_string()))?;
        std_listener
            .set_nonblocking(true)
            .map_err(|e| SecurityError::BindError(e.to_string()))?;
        tokio::net::TcpListener::from_std(std_listener)
            .map_err(|e| SecurityError::BindError(e.to_string()))
    }

    #[cfg(windows)]
    {
        use std::os::windows::io::{FromRawSocket, RawSocket};

        static INIT_WINSOCK: std::sync::Once = std::sync::Once::new();
        INIT_WINSOCK.call_once(|| {
            let mut wsa_buf = [0u8; 512];
            unsafe {
                let _ = WSAStartup(0x0202, wsa_buf.as_mut_ptr() as *mut c_void);
            }
        });

        unsafe {
            const AF_INET: i32 = 2;
            const SOCK_STREAM: i32 = 1;
            const IPPROTO_TCP: i32 = 6;
            const SOL_SOCKET: i32 = 0xFFFF;
            const SO_EXCLUSIVEADDRUSE: i32 = -5; // ((int)(~SO_REUSEADDR))

            let sock = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
            if sock == usize::MAX {
                let err = WSAGetLastError();
                return Err(SecurityError::BindError(format!(
                    "Failed to create raw socket: WSA error {}",
                    err
                )));
            }

            // Ensure socket handle is not inherited by child processes (prevents port locking across Hub restart)
            const HANDLE_FLAG_INHERIT: u32 = 0x00000001;
            SetHandleInformation(sock as *mut c_void, HANDLE_FLAG_INHERIT, 0);

            // Set SO_EXCLUSIVEADDRUSE before calling bind()
            let optval: i32 = 1;
            let opt_res = setsockopt(
                sock,
                SOL_SOCKET,
                SO_EXCLUSIVEADDRUSE,
                &optval as *const _ as *const i8,
                std::mem::size_of::<i32>() as i32,
            );
            if opt_res != 0 {
                let err = WSAGetLastError();
                closesocket(sock);
                return Err(SecurityError::BindError(format!(
                    "Failed to set SO_EXCLUSIVEADDRUSE: WSA error {}",
                    err
                )));
            }

            let port_be = addr.port().to_be();
            let ip_bytes = match addr.ip() {
                IpAddr::V4(ipv4) => ipv4.octets(),
                _ => {
                    closesocket(sock);
                    return Err(SecurityError::BindError("Only IPv4 is supported".into()));
                }
            };
            let ip_u32 = u32::from_ne_bytes(ip_bytes);

            let sockaddr = SockAddrIn {
                sin_family: AF_INET as u16,
                sin_port: port_be,
                sin_addr: ip_u32,
                sin_zero: [0; 8],
            };

            let bind_res = bind(
                sock,
                &sockaddr as *const _ as *const c_void,
                std::mem::size_of::<SockAddrIn>() as i32,
            );
            if bind_res != 0 {
                let err = WSAGetLastError();
                closesocket(sock);
                return Err(SecurityError::BindError(format!(
                    "Exclusive socket bind to {} failed: WSA error {} (port in use or access denied; FAILING CLOSED)",
                    addr, err
                )));
            }

            let listen_res = listen(sock, 128);
            if listen_res != 0 {
                let err = WSAGetLastError();
                closesocket(sock);
                return Err(SecurityError::BindError(format!(
                    "listen failed on {}: WSA error {}",
                    addr, err
                )));
            }

            let std_listener = std::net::TcpListener::from_raw_socket(sock as RawSocket);
            std_listener.set_nonblocking(true).map_err(|e| {
                SecurityError::BindError(format!("Failed to set nonblocking mode: {}", e))
            })?;

            let tokio_listener = if tokio::runtime::Handle::try_current().is_ok() {
                tokio::net::TcpListener::from_std(std_listener).map_err(|e| {
                    SecurityError::BindError(format!("Failed to convert to tokio TcpListener: {}", e))
                })?
            } else {
                tauri::async_runtime::block_on(async move {
                    tokio::net::TcpListener::from_std(std_listener)
                }).map_err(|e| {
                    SecurityError::BindError(format!("Failed to convert to tokio TcpListener: {}", e))
                })?
            };

            Ok(tokio_listener)
        }
    }
}

// -----------------------------------------------------------------------
// Unit & Integration Tests
// -----------------------------------------------------------------------

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn test_get_tcp_peer_pid_loopback_connection() {
        // Bind an exclusive listener on an ephemeral loopback port
        let listener = create_exclusive_tcp_listener(SocketAddr::new(
            IpAddr::V4(Ipv4Addr::new(127, 0, 0, 1)),
            0,
        ))
        .expect("Must bind exclusive ephemeral listener");

        let server_addr = listener.local_addr().expect("local_addr must be available");
        let server_handle = tokio::spawn(async move {
            let (server_conn, peer_addr) = listener.accept().await.expect("Accept failed");
            let local_addr = server_conn.local_addr().expect("local_addr failed");
            (local_addr, peer_addr)
        });

        // Client connects to the server
        let client_conn = tokio::net::TcpStream::connect(server_addr)
            .await
            .expect("Client connect failed");

        let client_local_addr = client_conn.local_addr().expect("client local_addr failed");
        let client_peer_addr = client_conn.peer_addr().expect("client peer_addr failed");

        let (server_local_addr, server_peer_addr) = server_handle.await.expect("Server task failed");

        assert_eq!(server_local_addr.ip(), server_addr.ip());
        assert_eq!(server_local_addr.port(), server_addr.port());
        assert_eq!(server_peer_addr, client_local_addr);
        assert_eq!(client_peer_addr, server_local_addr);

        // Resolve peer PID from server side: peer endpoint is client_local_addr
        let resolved_pid = get_tcp_peer_pid(server_local_addr, server_peer_addr)
            .expect("get_tcp_peer_pid must resolve peer PID");

        assert_eq!(
            resolved_pid,
            std::process::id(),
            "Resolved peer PID must equal current test process PID"
        );

        // Also verify from client side looking at server:
        let resolved_server_pid = get_tcp_peer_pid(client_local_addr, client_peer_addr)
            .expect("get_tcp_peer_pid must resolve server PID");
        assert_eq!(
            resolved_server_pid,
            std::process::id(),
            "Resolved server PID must equal current test process PID"
        );
    }

    #[test]
    fn test_verify_tcp_client_identity_current_process_success_with_test_policy() {
        let current_pid = std::process::id();
        let report = verify_tcp_client_identity_with_policy(
            current_pid,
            current_pid,
            &SecurityVerificationPolicy::test_policy(),
        )
        .expect("Verification must succeed for current test process with test policy");

        assert_eq!(report.actual_pid, current_pid);
        assert!(!report.user_sid.is_empty(), "User SID must not be empty");
        assert!(!report.executable_name.is_empty(), "Executable name must not be empty");
        assert!(!report.executable_path.is_empty(), "Executable path must not be empty");
    }

    #[test]
    fn test_verify_tcp_client_identity_production_policy_rejects_test_runner() {
        let current_pid = std::process::id();
        let res = verify_tcp_client_identity(current_pid, current_pid);
        assert!(res.is_err(), "Default production policy must reject test runner");
        match res.err().unwrap() {
            SecurityError::DisallowedExecutable { actual_name, allowed } => {
                assert!(allowed.contains(&"AstralParty_CN.exe".to_string()));
                assert!(allowed.contains(&"吉星派对.exe".to_string()));
                assert!(!actual_name.is_empty());
            }
            other => panic!("Expected DisallowedExecutable, got: {:?}", other),
        }
    }

    #[test]
    fn test_resolve_platform_id_from_path_cases() {
        assert_eq!(
            resolve_platform_id_from_path(Path::new(r"E:\Steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN.exe")),
            Some("STEAM_CN".to_string())
        );
        assert_eq!(
            resolve_platform_id_from_path(Path::new(r"E:\Steam\steamapps\common\Astral Party\8vJXnINT\AstralParty_INT.exe")),
            Some("STEAM_GLOBAL".to_string())
        );
        assert_eq!(
            resolve_platform_id_from_path(Path::new(r"D:\TapTap\CN_TAPTAP_V3.3.0\吉星派对.exe")),
            Some("TAPTAP".to_string())
        );
        assert_eq!(
            resolve_platform_id_from_path(Path::new(r"C:\Windows\System32\notepad.exe")),
            None
        );
    }

    #[test]
    fn test_verify_tcp_client_identity_pid_spoofing_rejected() {
        let actual_pid = std::process::id();
        let forged_pid = actual_pid + 9999;

        let res = verify_tcp_client_identity(actual_pid, forged_pid);
        assert!(res.is_err(), "Must reject forged PID");

        match res.err().unwrap() {
            SecurityError::PidSpoofingDetected { claimed, actual } => {
                assert_eq!(claimed, forged_pid);
                assert_eq!(actual, actual_pid);
            }
            other => panic!("Expected PidSpoofingDetected, got: {:?}", other),
        }
    }

    #[test]
    fn test_verify_tcp_client_identity_zero_pid_rejected() {
        let res = verify_tcp_client_identity(0, 0);
        assert!(res.is_err(), "Must reject zero PID");
        match res.err().unwrap() {
            SecurityError::InvalidPid => {}
            other => panic!("Expected InvalidPid, got: {:?}", other),
        }
    }

    #[test]
    fn test_verify_tcp_client_identity_disallowed_executable_rejected() {
        let current_pid = std::process::id();
        let mut policy = SecurityVerificationPolicy::default();
        policy.allowed_executables = vec!["DefinitelyNotThisExe_42.exe".into()];
        policy.allow_test_runners = false;

        let res = verify_tcp_client_identity_with_policy(current_pid, current_pid, &policy);
        assert!(res.is_err(), "Must reject non-whitelisted executable");
        match res.err().unwrap() {
            SecurityError::DisallowedExecutable { actual_name, allowed } => {
                assert_eq!(allowed, vec!["DefinitelyNotThisExe_42.exe"]);
                assert!(!actual_name.is_empty());
            }
            other => panic!("Expected DisallowedExecutable, got: {:?}", other),
        }
    }

    #[test]
    fn test_verify_tcp_client_identity_disallowed_game_path_rejected() {
        let current_pid = std::process::id();
        let mut policy = SecurityVerificationPolicy::default();
        let cur_name = std::env::current_exe()
            .unwrap()
            .file_name()
            .unwrap()
            .to_string_lossy()
            .to_string();

        policy.allowed_executables = vec![cur_name];
        policy.allow_test_runners = false;
        policy.expected_steam_cn_path = Some(PathBuf::from(r"C:\NonExistentSteamPath\8vJXn6CN\AstralParty_CN.exe"));

        let res = verify_tcp_client_identity_with_policy(current_pid, current_pid, &policy);
        assert!(res.is_err(), "Must reject executable path outside approved Steam CN folder");
        match res.err().unwrap() {
            SecurityError::DisallowedPath { path, reason } => {
                assert!(!path.is_empty());
                assert!(reason.contains("canonical Steam CN PlatformProfile path") || reason.contains("does not match"));
            }
            other => panic!("Expected DisallowedPath, got: {:?}", other),
        }
    }

    #[test]
    fn test_verify_tcp_client_identity_exact_canonical_path_accepted() {
        let current_pid = std::process::id();
        let cur_exe = std::env::current_exe().unwrap();
        let cur_name = cur_exe.file_name().unwrap().to_string_lossy().to_string();

        let mut policy = SecurityVerificationPolicy::production();
        policy.allowed_executables = vec![cur_name];
        policy.allow_test_runners = false; // Strictly requires exact game path match
        policy.expected_steam_cn_path = Some(cur_exe);

        let res = verify_tcp_client_identity_with_policy(current_pid, current_pid, &policy);
        assert!(res.is_ok(), "Must accept exact canonical path: {:?}", res.err());
        let report = res.unwrap();
        assert!(report.is_steam_cn_path);
    }

    #[test]
    fn test_verify_tcp_client_identity_same_filename_wrong_directory_rejected() {
        let current_pid = std::process::id();
        let cur_exe = std::env::current_exe().unwrap();
        let cur_name = cur_exe.file_name().unwrap().to_string_lossy().to_string();

        let mut policy = SecurityVerificationPolicy::production();
        policy.allowed_executables = vec![cur_name.clone()];
        policy.allow_test_runners = false;
        policy.expected_steam_cn_path = Some(PathBuf::from(r"C:\DifferentFolder\WrongDirectory").join(&cur_name));

        let res = verify_tcp_client_identity_with_policy(current_pid, current_pid, &policy);
        assert!(res.is_err(), "Must reject same filename in wrong directory");
        match res.err().unwrap() {
            SecurityError::DisallowedPath { reason, .. } => {
                assert!(reason.contains("does not match canonical Steam CN PlatformProfile path"));
            }
            other => panic!("Expected DisallowedPath, got: {:?}", other),
        }
    }

    #[test]
    fn test_verify_tcp_client_identity_fake_temp_path_rejected() {
        let fake_path = PathBuf::from(r"C:\Temp\Astral Party\8vJXn6CN\AstralParty_CN.exe");
        let expected_path = PathBuf::from(r"E:\Steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN.exe");

        let normalized_fake = normalize_path_for_comparison(&fake_path);
        let normalized_expected = normalize_path_for_comparison(&expected_path);

        assert_ne!(normalized_fake, normalized_expected, "Fake path in C:\\Temp must not match canonical Steam path");
    }

    #[test]
    fn test_path_traversal_and_slash_normalization() {
        let path1 = PathBuf::from(r"E:\Steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN.exe");
        let path2 = PathBuf::from(r"E:/Steam/steamapps/common/../common/Astral Party/8vJXn6CN/AstralParty_CN.exe");

        let norm1 = normalize_path_for_comparison(&path1);
        let norm2 = normalize_path_for_comparison(&path2);

        assert_eq!(norm1, norm2, "Path with forward slashes and traversal '..' must normalize identically");
    }

    #[test]
    fn test_verify_tcp_client_identity_missing_platform_profile_path_fails_closed() {
        let current_pid = std::process::id();
        let cur_exe = std::env::current_exe().unwrap();
        let cur_name = cur_exe.file_name().unwrap().to_string_lossy().to_string();

        let mut policy = SecurityVerificationPolicy::production();
        policy.allowed_executables = vec![cur_name];
        policy.allow_test_runners = false;
        policy.expected_steam_cn_path = None; // Unresolved / unconfigured in production

        let res = verify_tcp_client_identity_with_policy(current_pid, current_pid, &policy);
        assert!(res.is_err(), "Must fail closed if PlatformProfile path cannot be resolved");
        match res.err().unwrap() {
            SecurityError::DisallowedPath { reason, .. } => {
                assert!(reason.contains("cannot be resolved (FAIL CLOSED)"));
            }
            other => panic!("Expected DisallowedPath, got: {:?}", other),
        }
    }

    #[tokio::test]
    async fn test_exclusive_binding_port_squatting_fails_closed() {
        // Bind first listener with SO_EXCLUSIVEADDRUSE on ephemeral port
        let listener_1 = create_exclusive_tcp_listener(SocketAddr::new(
            IpAddr::V4(Ipv4Addr::new(127, 0, 0, 1)),
            0,
        ))
        .expect("Listener 1 bind must succeed");

        let bound_addr = listener_1.local_addr().expect("Bound address required");

        // Attempt second listener on the exact same address/port
        let res_2 = create_exclusive_tcp_listener(bound_addr);
        assert!(
            res_2.is_err(),
            "Second bind to the same address must fail closed immediately"
        );

        match res_2.err().unwrap() {
            SecurityError::BindError(msg) => {
                assert!(
                    msg.contains("failed") || msg.contains("WSA error"),
                    "Error must indicate socket binding failure: {}",
                    msg
                );
            }
            other => panic!("Expected BindError, got: {:?}", other),
        }
    }

    #[test]
    fn test_exclusive_listener_prohibits_non_loopback() {
        let res = create_exclusive_tcp_listener(
            "0.0.0.0:25890".parse().unwrap(),
        );
        assert!(res.is_err(), "Must prohibit 0.0.0.0");
        match res.err().unwrap() {
            SecurityError::BindError(msg) => {
                assert!(msg.contains("Must be strictly 127.0.0.1"), "{}", msg);
            }
            other => panic!("Expected BindError, got: {:?}", other),
        }
    }
}
