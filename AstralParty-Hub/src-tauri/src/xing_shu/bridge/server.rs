use std::collections::{HashMap, VecDeque};
use std::net::{IpAddr, Ipv4Addr, SocketAddr};
use std::sync::{Arc, Mutex};
use chrono::Utc;
use serde::{Deserialize, Serialize};
use tracing::{error, info, warn};
use uuid::Uuid;
use tauri::Emitter;

use crate::domain::error::HubResult;
use crate::xing_shu::bridge::protocol::{
    error_codes, message_types, BridgeEnvelope, BridgeHandshake, BridgeHandshakeAck, BridgeMessage,
    BridgeServerState, ComponentIdentity, HeartbeatPayload, HelloAckPayload, HelloPayload,
    RuntimeIdentityUpdatePayload, RuntimeMatchContext, RuntimeMatchContextPayload, RuntimeStatusPayload, SpectatorHandPayload, TestEventPayload,
    WatchTargetPayload, WatchTargetResponsePayload, ALLOWED_CAPABILITIES,
    MAX_MESSAGE_SIZE, PROHIBITED_CREDENTIAL_KEYS, XINGSHU_BRIDGE_NAMED_PIPE, XINGSHU_BRIDGE_PROTOCOL_VERSION,
    XINGSHU_BRIDGE_TCP_ADDR,
};
use crate::xing_shu::bridge::session::RuntimeSession;
use crate::xing_shu::bridge::connection::ActiveConnection;

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "type", content = "data", rename_all = "camelCase")]
pub enum SessionEvent {
    SessionChanged(RuntimeSession),
    SessionDisconnected { connection_id: String },
}

#[derive(Debug, PartialEq, Eq)]
pub enum BoundedLineError {
    TooLong,
    Truncated,
    InvalidUtf8,
    Io(std::io::ErrorKind),
}

impl std::fmt::Display for BoundedLineError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            BoundedLineError::TooLong => write!(f, "Message exceeds 64KB frame limit"),
            BoundedLineError::Truncated => write!(f, "Message truncated before newline delimiter"),
            BoundedLineError::InvalidUtf8 => write!(f, "Message contains invalid UTF-8"),
            BoundedLineError::Io(kind) => write!(f, "I/O error: {:?}", kind),
        }
    }
}

impl std::error::Error for BoundedLineError {}

/// Reads a single line terminated by '\n' up to max_bytes.
/// Immediately aborts if byte (max_bytes + 1) is observed before '\n', preventing unbounded memory allocation.
pub async fn read_bounded_line<R: tokio::io::AsyncBufReadExt + Unpin>(
    reader: &mut R,
    max_bytes: usize,
) -> Result<Option<String>, BoundedLineError> {
    let mut line_bytes = Vec::new();
    loop {
        let available = match reader.fill_buf().await {
            Ok(buf) => buf,
            Err(e) => return Err(BoundedLineError::Io(e.kind())),
        };

        if available.is_empty() {
            // EOF reached
            if line_bytes.is_empty() {
                return Ok(None);
            } else {
                return Err(BoundedLineError::Truncated);
            }
        }

        if let Some(pos) = available.iter().position(|&b| b == b'\n') {
            let chunk_len = pos;
            if line_bytes.len() + chunk_len > max_bytes {
                return Err(BoundedLineError::TooLong);
            }
            line_bytes.extend_from_slice(&available[..chunk_len]);
            reader.consume(pos + 1);

            if line_bytes.ends_with(b"\r") {
                line_bytes.pop();
            }

            let s = String::from_utf8(line_bytes).map_err(|_| BoundedLineError::InvalidUtf8)?;
            return Ok(Some(s));
        } else {
            let chunk_len = available.len();
            if line_bytes.len() + chunk_len > max_bytes {
                return Err(BoundedLineError::TooLong);
            }
            line_bytes.extend_from_slice(available);
            reader.consume(chunk_len);
        }
    }
}

#[cfg(windows)]
struct AutoHandle(std::os::windows::io::RawHandle);

#[cfg(windows)]
impl Drop for AutoHandle {
    fn drop(&mut self) {
        if !self.0.is_null() && (self.0 as isize) != -1 {
            unsafe { CloseHandle(self.0) };
        }
    }
}

#[cfg(windows)]
#[repr(C)]
struct SidAndAttributes {
    sid: *mut std::ffi::c_void,
    attributes: u32,
}

#[cfg(windows)]
#[repr(C)]
struct TokenUser {
    user: SidAndAttributes,
}

#[cfg(windows)]
#[link(name = "advapi32")]
unsafe extern "system" {
    fn OpenProcessToken(
        process_handle: std::os::windows::io::RawHandle,
        desired_access: u32,
        token_handle: *mut std::os::windows::io::RawHandle,
    ) -> i32;
    fn GetTokenInformation(
        token_handle: std::os::windows::io::RawHandle,
        token_information_class: u32,
        token_information: *mut std::ffi::c_void,
        token_information_length: u32,
        return_length: *mut u32,
    ) -> i32;
    fn EqualSid(
        sid1: *mut std::ffi::c_void,
        sid2: *mut std::ffi::c_void,
    ) -> i32;
    fn ConvertSidToStringSidW(
        sid: *mut std::ffi::c_void,
        string_sid: *mut *mut u16,
    ) -> i32;
    fn ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string_security_descriptor: *const u16,
        string_sd_revision: u32,
        security_descriptor: *mut *mut std::ffi::c_void,
        security_descriptor_size: *mut u32,
    ) -> i32;
}

#[cfg(windows)]
#[repr(C)]
#[allow(non_snake_case)]
struct SECURITY_ATTRIBUTES {
    nLength: u32,
    lpSecurityDescriptor: *mut std::ffi::c_void,
    bInheritHandle: i32,
}

#[cfg(windows)]
#[link(name = "kernel32")]
unsafe extern "system" {
    fn GetNamedPipeClientProcessId(
        pipe: std::os::windows::io::RawHandle,
        client_process_id: *mut u32,
    ) -> i32;
    fn GetCurrentProcess() -> std::os::windows::io::RawHandle;
    fn GetCurrentProcessId() -> u32;
    fn ProcessIdToSessionId(process_id: u32, session_id: *mut u32) -> i32;
    fn OpenProcess(desired_access: u32, inherit_handle: i32, process_id: u32) -> std::os::windows::io::RawHandle;
    fn CloseHandle(handle: std::os::windows::io::RawHandle) -> i32;
    fn GetExitCodeProcess(process: std::os::windows::io::RawHandle, exit_code: *mut u32) -> i32;
    fn LocalFree(hmem: *mut std::ffi::c_void) -> *mut std::ffi::c_void;
    fn CreateNamedPipeW(
        lpName: *const u16,
        dwOpenMode: u32,
        dwPipeMode: u32,
        nMaxInstances: u32,
        nOutBufferSize: u32,
        nInBufferSize: u32,
        nDefaultTimeOut: u32,
        lpSecurityAttributes: *mut SECURITY_ATTRIBUTES,
    ) -> std::os::windows::io::RawHandle;
}

#[cfg(windows)]
unsafe fn get_process_user_sid(process_handle: std::os::windows::io::RawHandle) -> Result<Vec<u8>, String> {
    const TOKEN_QUERY: u32 = 0x0008;
    let mut token_handle: std::os::windows::io::RawHandle = std::ptr::null_mut();
    if unsafe { OpenProcessToken(process_handle, TOKEN_QUERY, &mut token_handle) } == 0 {
        return Err(format!("OpenProcessToken failed: {}", std::io::Error::last_os_error()));
    }
    let _token_guard = AutoHandle(token_handle);

    let mut ret_len = 0u32;
    let _ = unsafe { GetTokenInformation(token_handle, 1, std::ptr::null_mut(), 0, &mut ret_len) };
    if ret_len == 0 {
        return Err(format!("GetTokenInformation query size failed: {}", std::io::Error::last_os_error()));
    }

    let mut buf = vec![0u8; ret_len as usize];
    if unsafe { GetTokenInformation(token_handle, 1, buf.as_mut_ptr() as *mut _, ret_len, &mut ret_len) } == 0 {
        return Err(format!("GetTokenInformation failed: {}", std::io::Error::last_os_error()));
    }

    Ok(buf)
}

#[cfg(windows)]
pub fn get_current_user_sid_string() -> Result<String, String> {
    unsafe {
        let sid_buf = get_process_user_sid(GetCurrentProcess())?;
        let token_user = &*(sid_buf.as_ptr() as *const TokenUser);
        let mut string_sid_ptr: *mut u16 = std::ptr::null_mut();
        if ConvertSidToStringSidW(token_user.user.sid, &mut string_sid_ptr) == 0 {
            return Err(format!("ConvertSidToStringSidW failed: {}", std::io::Error::last_os_error()));
        }
        if string_sid_ptr.is_null() {
            return Err("ConvertSidToStringSidW returned null pointer".to_string());
        }

        let mut len = 0;
        while *string_sid_ptr.add(len) != 0 {
            len += 1;
        }
        let slice = std::slice::from_raw_parts(string_sid_ptr, len);
        let sid_str = String::from_utf16(slice).map_err(|e| format!("Invalid UTF-16 SID: {}", e))?;
        LocalFree(string_sid_ptr as *mut _);
        Ok(sid_str)
    }
}

/// Builds the explicit Security Descriptor Definition Language (SDDL) string
/// for Named Pipe IPC under LOCAL_SESSION_RESTRICTED classification.
///
/// Security Posture & Access Control List:
/// - Protected DACL `D:P` (SE_DACL_PROTECTED): prevents inheritance of loose default ACEs.
/// - Deny Anonymous logon: `(D;;GA;;;AN)` - blocks unauthenticated / null session RPC.
/// - Allow LocalSystem: `(A;;GA;;;SY)` - grants Generic All (Full Control) to NT AUTHORITY\\SYSTEM.
/// - Allow Administrators: `(A;;GA;;;BA)` - grants Generic All (Full Control) to Built-in Administrators.
/// - Allow Current User: `(A;;GRGW;;;{UserSID})` - grants Generic Read and Generic Write to current user token.
/// - Deny Everyone (WD) and other accounts: strictly enforced via omission in the protected DACL (under Windows
///   discretionary access control, any principal not explicitly granted access is denied; an explicit `(D;;GA;;;WD)`
///   ACE would also match and deny the current user token, causing ERROR_ACCESS_DENIED).
#[cfg(windows)]
pub fn build_pipe_security_descriptor_sddl(user_sid: &str) -> String {
    format!("D:P(D;;GA;;;AN)(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;{})", user_sid)
}

#[cfg(windows)]
pub fn verify_client_process_security(client_pid: u32) -> Result<u32, String> {
    if client_pid == 0 {
        return Err("client_pid must be non-zero".to_string());
    }

    let mut client_session_id = 0u32;
    let mut current_session_id = 0u32;
    let curr_pid = unsafe { GetCurrentProcessId() };

    let s1 = unsafe { ProcessIdToSessionId(client_pid, &mut client_session_id) };
    if s1 == 0 {
        return Err(format!(
            "ProcessIdToSessionId failed for client PID {}: {}",
            client_pid,
            std::io::Error::last_os_error()
        ));
    }

    let s2 = unsafe { ProcessIdToSessionId(curr_pid, &mut current_session_id) };
    if s2 == 0 {
        return Err(format!(
            "ProcessIdToSessionId failed for Hub PID {}: {}",
            curr_pid,
            std::io::Error::last_os_error()
        ));
    }

    if client_session_id != current_session_id {
        return Err(format!(
            "Session mismatch (FAIL CLOSED): client PID {} in session {}, current in session {}",
            client_pid, client_session_id, current_session_id
        ));
    }

    const PROCESS_QUERY_LIMITED_INFORMATION: u32 = 0x1000;
    const STILL_ACTIVE: u32 = 259;
    let h_proc = unsafe { OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, client_pid) };
    if h_proc.is_null() || (h_proc as isize) == -1 {
        return Err(format!("Failed to open client process PID {}: {}", client_pid, std::io::Error::last_os_error()));
    }
    let _proc_guard = AutoHandle(h_proc);

    let mut exit_code = 0u32;
    let get_ok = unsafe { GetExitCodeProcess(h_proc, &mut exit_code) };
    let is_alive = get_ok != 0 && exit_code == STILL_ACTIVE;
    if !is_alive {
        return Err(format!("Client PID {} is not an active process", client_pid));
    }

    // Windows User SID Verification (Task 1: FAIL CLOSED)
    unsafe {
        let client_sid_buf = get_process_user_sid(h_proc)
            .map_err(|e| format!("Failed to get client user SID: {}", e))?;
        let server_sid_buf = get_process_user_sid(GetCurrentProcess())
            .map_err(|e| format!("Failed to get Hub server user SID: {}", e))?;

        let client_token_user = &*(client_sid_buf.as_ptr() as *const TokenUser);
        let server_token_user = &*(server_sid_buf.as_ptr() as *const TokenUser);

        let equal = EqualSid(client_token_user.user.sid, server_token_user.user.sid);
        if equal == 0 {
            return Err("Windows User SID mismatch (FAIL CLOSED): client user SID does not match Hub server user SID".to_string());
        }
    }

    Ok(client_pid)
}

#[cfg(windows)]
pub fn verify_pipe_client_security(pipe_handle: std::os::windows::io::RawHandle) -> Result<u32, String> {
    let mut client_pid = 0u32;
    let ok = unsafe { GetNamedPipeClientProcessId(pipe_handle, &mut client_pid) };
    if ok == 0 || client_pid == 0 {
        return Err(format!("GetNamedPipeClientProcessId failed: {}", std::io::Error::last_os_error()));
    }
    verify_client_process_security(client_pid)
}

/// Retrieves the PID of the process owning the TCP peer connection using the Windows Extended TCP Table API.
pub use super::tcp_security::get_tcp_peer_pid;

/// Verifies OS-level security trust for a TCP connection by extracting its peer PID and checking process credentials.
#[cfg(windows)]
pub fn verify_tcp_peer_security(peer_addr: SocketAddr, local_addr: SocketAddr) -> Result<u32, String> {
    let peer_pid = get_tcp_peer_pid(local_addr, peer_addr)?;
    verify_client_process_security(peer_pid)
}

#[cfg(not(windows))]
pub fn verify_client_process_security(client_pid: u32) -> Result<u32, String> {
    Ok(client_pid)
}

#[cfg(not(windows))]
pub fn verify_tcp_peer_security(_peer_addr: SocketAddr, _local_addr: SocketAddr) -> Result<u32, String> {
    Ok(0)
}

pub const DEFAULT_TCP_ADDR: &str = XINGSHU_BRIDGE_TCP_ADDR;

/// Validates that the requested TCP bind address is strictly IPv4 loopback 127.0.0.1.
/// Strictly prohibits 0.0.0.0 or external interfaces (fails closed immediately).
pub fn validate_tcp_bind_addr(addr: &str) -> Result<SocketAddr, String> {
    let parsed: SocketAddr = addr
        .parse()
        .map_err(|e| format!("Invalid address '{}': {}", addr, e))?;

    if parsed.ip().is_unspecified() || parsed.ip() != IpAddr::V4(Ipv4Addr::new(127, 0, 0, 1)) {
        let msg = format!(
            "Security violation: TCP server strictly prohibited from binding to non-127.0.0.1 address '{}'. 0.0.0.0 and external interfaces are forbidden.",
            parsed
        );
        return Err(msg);
    }
    Ok(parsed)
}

/// Named Pipe Security & Creation Helper
///
/// Security Classification: LOCAL_SESSION_RESTRICTED
/// - Security Posture:
///   1. `PIPE_REJECT_REMOTE_CLIENTS` strictly prevents remote network connections (SMB/RPC),
///      ensuring the named pipe is accessible solely by processes running locally.
///   2. Explicit Security Descriptor (DACL) created via ConvertStringSecurityDescriptorToSecurityDescriptorW:
///      - Protected DACL (`D:P`) prevents loose inherited permissions.
///      - Explicit Deny Anonymous (`(D;;GA;;;AN)`).
///      - Explicit Allow LocalSystem (`(A;;GA;;;SY)`).
///      - Explicit Allow Administrators (`(A;;GA;;;BA)`).
///      - Explicit Allow Current User Token SID Read/Write (`(A;;GRGW;;;{UserSID})`).
///      - Deny Everyone (WD) and other users enforced via exclusion in protected DACL.
///   3. Process and session validation via `verify_pipe_client_security`:
///      - Validates caller Session ID == Hub Session ID (fails closed if session query fails or mismatches).
///      - Validates caller Token User SID == Hub Token User SID (fails closed if token query fails or SIDs mismatch).
///   4. Pipe Name Squatting Protection: `FILE_FLAG_FIRST_PIPE_INSTANCE` guarantees that the server only starts
///      if it creates the first pipe instance. If the pipe name is already owned or created (e.g.,
///      ERROR_ACCESS_DENIED, ERROR_PIPE_BUSY, ERROR_ALREADY_EXISTS), creation FAILS CLOSED immediately
///      with `PIPE_NAME_ALREADY_OWNED`, preventing rogue server / squatting attacks. Standard create
///      fallback is strictly prohibited.
#[cfg(windows)]
pub fn create_named_pipe_server(
    pipe_name: &str,
    is_first: bool,
) -> Result<tokio::net::windows::named_pipe::NamedPipeServer, String> {
    let user_sid = get_current_user_sid_string()
        .map_err(|e| format!("Failed to obtain current user SID for pipe security descriptor: {}", e))?;
    let sddl = build_pipe_security_descriptor_sddl(&user_sid);

    let wide_sddl: Vec<u16> = sddl.encode_utf16().chain(std::iter::once(0)).collect();
    let mut p_sd: *mut std::ffi::c_void = std::ptr::null_mut();
    let mut sd_size = 0u32;
    let ok = unsafe {
        ConvertStringSecurityDescriptorToSecurityDescriptorW(
            wide_sddl.as_ptr(),
            1, // SDDL_REVISION_1
            &mut p_sd,
            &mut sd_size,
        )
    };
    if ok == 0 || p_sd.is_null() {
        return Err(format!(
            "ConvertStringSecurityDescriptorToSecurityDescriptorW failed for SDDL '{}': {}",
            sddl,
            std::io::Error::last_os_error()
        ));
    }

    let mut sa = SECURITY_ATTRIBUTES {
        nLength: std::mem::size_of::<SECURITY_ATTRIBUTES>() as u32,
        lpSecurityDescriptor: p_sd,
        bInheritHandle: 0,
    };

    let wide_name: Vec<u16> = pipe_name.encode_utf16().chain(std::iter::once(0)).collect();

    const PIPE_ACCESS_DUPLEX: u32 = 0x00000003;
    const FILE_FLAG_OVERLAPPED: u32 = 0x40000000;
    const FILE_FLAG_FIRST_PIPE_INSTANCE: u32 = 0x00080000;
    const PIPE_TYPE_BYTE: u32 = 0x00000000;
    const PIPE_READMODE_BYTE: u32 = 0x00000000;
    const PIPE_WAIT: u32 = 0x00000000;
    const PIPE_REJECT_REMOTE_CLIENTS: u32 = 0x00000008;
    const PIPE_UNLIMITED_INSTANCES: u32 = 255;

    let mut dw_open_mode = PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED;
    if is_first {
        dw_open_mode |= FILE_FLAG_FIRST_PIPE_INSTANCE;
    }

    let dw_pipe_mode = PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS;

    let h = unsafe {
        CreateNamedPipeW(
            wide_name.as_ptr(),
            dw_open_mode,
            dw_pipe_mode,
            PIPE_UNLIMITED_INSTANCES,
            65536,
            65536,
            0,
            &mut sa as *mut _,
        )
    };

    unsafe {
        LocalFree(p_sd);
    }

    if h.is_null() || (h as isize) == -1 {
        let err = std::io::Error::last_os_error();
        if is_first {
            error!(
                "PIPE_NAME_ALREADY_OWNED: Pipe '{}' is already owned or created by another process (OS error: {}). Failing closed.",
                pipe_name, err
            );
            return Err(error_codes::PIPE_NAME_ALREADY_OWNED.to_string());
        } else {
            error!("Failed to create subsequent Named Pipe server instance on '{}': {}", pipe_name, err);
            return Err(format!("Failed to create subsequent pipe instance: {}", err));
        }
    }

    unsafe {
        tokio::net::windows::named_pipe::NamedPipeServer::from_raw_handle(h)
            .map_err(|e| format!("NamedPipeServer::from_raw_handle failed on '{}': {}", pipe_name, e))
    }
}

#[cfg(not(windows))]
pub fn create_named_pipe_server(
    _pipe_name: &str,
    _is_first: bool,
) -> Result<(), String> {
    Err("Named Pipe IPC is only supported on Windows platforms.".to_string())
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SafeFrameSummary {
    pub message_type: String,
    pub request_id: String,
    pub length: usize,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BridgeLogEntry {
    pub id: String,
    pub direction: String, // "IN" | "OUT" | "SYS"
    pub message_type: String,
    pub summary: String,
    pub raw_json: String,
    pub timestamp: i64,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "SCREAMING_SNAKE_CASE")]
pub enum ActiveTransport {
    Disconnected,
    LoopbackTcp,
    NamedPipeLegacy,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct BridgeStatus {
    pub is_running: bool,
    pub named_pipe_name: String,
    pub pipe_connected: bool,
    pub runtime_connected: bool,
    pub runtime_authenticated: bool,
    pub connection_state: BridgeServerState,
    pub protocol_version: String,
    pub game_pid: Option<u32>,
    pub game_version: Option<String>,
    pub runtime_version: Option<String>,
    pub last_heartbeat_timestamp: Option<i64>,
    pub last_heartbeat_latency_ms: Option<u64>,
    pub connected_at: Option<i64>,
    pub total_messages_received: u64,
    pub total_messages_sent: u64,
    pub recent_messages: Vec<BridgeLogEntry>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub tcp_addr: Option<String>,
    pub active_transport: ActiveTransport,
    pub tcp_connected: bool,
}

#[derive(Debug, Clone)]
pub struct ConnectedClient {
    pub session_id: String,
    pub component: ComponentIdentity,
    pub capabilities: Vec<String>,
    pub connected_at: chrono::DateTime<Utc>,
}

struct InnerServerState {
    pipe_name: String,
    connection_state: BridgeServerState,
    session_challenge: String,
    session_id: Option<String>,
    pipe_connected: bool,
    runtime_connected: bool,
    active_generation: u64,
    next_generation: u64,
    pipe_client_pid: Option<u32>,
    game_pid: Option<u32>,
    game_version: Option<String>,
    runtime_version: Option<String>,
    last_heartbeat_timestamp: Option<i64>,
    last_heartbeat_latency_ms: Option<u64>,
    connected_at: Option<i64>,
    total_messages_received: u64,
    total_messages_sent: u64,
    recent_messages: VecDeque<BridgeLogEntry>,
    active_sender: Option<tokio::sync::mpsc::Sender<String>>,
    legacy_clients: Vec<ConnectedClient>,
    spectator_active_epoch: u64,
    spectator_highest_sequence: u64,
    latest_spectator_snapshot: Option<SpectatorHandPayload>,
    tcp_addr: Option<SocketAddr>,
    tcp_server_started: bool,
    active_transport: ActiveTransport,
    tcp_connected: bool,
    pub match_contexts: HashMap<String, RuntimeMatchContextPayload>,
}

impl Default for InnerServerState {
    fn default() -> Self {
        Self {
            pipe_name: XINGSHU_BRIDGE_NAMED_PIPE.to_string(),
            connection_state: BridgeServerState::Disconnected,
            session_challenge: Uuid::new_v4().to_string(),
            session_id: None,
            pipe_connected: false,
            runtime_connected: false,
            active_generation: 0,
            next_generation: 0,
            pipe_client_pid: None,
            game_pid: None,
            game_version: None,
            runtime_version: None,
            last_heartbeat_timestamp: None,
            last_heartbeat_latency_ms: None,
            connected_at: None,
            total_messages_received: 0,
            total_messages_sent: 0,
            recent_messages: VecDeque::with_capacity(100),
            active_sender: None,
            legacy_clients: Vec::new(),
            spectator_active_epoch: 0,
            spectator_highest_sequence: 0,
            latest_spectator_snapshot: None,
            tcp_addr: None,
            tcp_server_started: false,
            active_transport: ActiveTransport::Disconnected,
            tcp_connected: false,
            match_contexts: HashMap::new(),
        }
    }
}

#[derive(Clone)]
pub struct BridgeServer {
    state: Arc<Mutex<InnerServerState>>,
    is_server_started: Arc<Mutex<bool>>,
    shutdown_notify: Arc<tokio::sync::Notify>,
    spectator_broadcast_tx: tokio::sync::broadcast::Sender<SpectatorHandPayload>,
    security_policy: Arc<Mutex<super::tcp_security::SecurityVerificationPolicy>>,
    session_manager: super::session::SessionManager,
    connection_manager: super::connection::ConnectionManager,
    app_handle: Arc<Mutex<Option<tauri::AppHandle>>>,
    session_events_tx: tokio::sync::broadcast::Sender<SessionEvent>,
}

impl Default for BridgeServer {
    fn default() -> Self {
        Self::new()
    }
}

impl BridgeServer {
    /// Creates a production BridgeServer enforcing canonical production security policy
    /// (allow_test_runners: false, AstralParty_CN.exe only, Steam CN path only).
    pub fn new() -> Self {
        Self::with_security_policy_internal(super::tcp_security::SecurityVerificationPolicy::production())
    }

    /// Creates a test BridgeServer configured to accept authorized test runners
    /// (allow_test_runners: true).
    pub fn new_test() -> Self {
        Self::with_security_policy_internal(super::tcp_security::SecurityVerificationPolicy::test_policy())
    }

    fn with_security_policy_internal(policy: super::tcp_security::SecurityVerificationPolicy) -> Self {
        let (spectator_broadcast_tx, _) = tokio::sync::broadcast::channel(64);
        let (session_events_tx, _) = tokio::sync::broadcast::channel(64);
        Self {
            state: Arc::new(Mutex::new(InnerServerState::default())),
            is_server_started: Arc::new(Mutex::new(false)),
            shutdown_notify: Arc::new(tokio::sync::Notify::new()),
            spectator_broadcast_tx,
            security_policy: Arc::new(Mutex::new(policy)),
            session_manager: super::session::SessionManager::new(),
            connection_manager: super::connection::ConnectionManager::new(),
            app_handle: Arc::new(Mutex::new(None)),
            session_events_tx,
        }
    }

    pub fn session_manager(&self) -> &super::session::SessionManager {
        &self.session_manager
    }

    pub fn connection_manager(&self) -> &super::connection::ConnectionManager {
        &self.connection_manager
    }

    pub fn set_app_handle(&self, handle: tauri::AppHandle) {
        *self.app_handle.lock().unwrap() = Some(handle);
    }

    pub fn subscribe_session_events(&self) -> tokio::sync::broadcast::Receiver<SessionEvent> {
        self.session_events_tx.subscribe()
    }

    pub fn emit_session_changed(&self, session: &RuntimeSession) {
        let _ = self.session_events_tx.send(SessionEvent::SessionChanged(session.clone()));
        if let Some(ref handle) = *self.app_handle.lock().unwrap() {
            use tauri::Emitter;
            let _ = handle.emit("runtime://session-changed", session);
        }
    }

    pub fn emit_session_disconnected(&self, connection_id: &str) {
        let _ = self.session_events_tx.send(SessionEvent::SessionDisconnected {
            connection_id: connection_id.to_string(),
        });
        if let Some(ref handle) = *self.app_handle.lock().unwrap() {
            use tauri::Emitter;
            let payload = serde_json::json!({
                "connectionId": connection_id,
                "connection_id": connection_id
            });
            let _ = handle.emit("runtime://session-disconnected", &payload);
            let _ = handle.emit("runtime://session-changed", &serde_json::json!({
                "disconnectedConnectionId": connection_id
            }));
        }
    }

    pub fn emit_watch_target_response(&self, response: &WatchTargetResponsePayload) {
        if let Some(ref handle) = *self.app_handle.lock().unwrap() {
            use tauri::Emitter;
            let _ = handle.emit("spectator://watch-target-response", response);
        }
    }

    pub fn get_runtime_sessions(&self) -> Vec<RuntimeSession> {
        self.session_manager.list()
    }

    pub fn get_runtime_session(&self, connection_id: &str) -> Option<RuntimeSession> {
        self.session_manager.get(connection_id)
    }

    pub fn get_runtime_match_context(&self, connection_id: &str) -> Option<RuntimeMatchContextPayload> {
        let state = self.state.lock().unwrap();
        state.match_contexts.get(connection_id).cloned()
    }

    /// HS4-P1.6R2.1 Lane 2: Secure Mirror Routing
    /// Routes spectator hand truth exclusively to the verified Steam MAIN runtime session.
    ///
    /// Invariant Requirements:
    /// 1. Producer Invariant:
    ///    - source RuntimeSession.platform_id == "TAPTAP"
    ///    - AND source RuntimeSession.player_id == Some(3045259) (bound USER_SPECTATOR)
    ///    - AND source role == "USER_SPECTATOR".
    ///    - SPECTATOR_HAND_SNAPSHOT from Steam MAIN or any other runtime must NEVER become a mirror producer.
    ///
    /// 2. Target Invariant:
    ///    - target RuntimeSession.platform_id == "STEAM_CN"
    ///    - AND target RuntimeSession.player_id == Some(1556514) (bound MAIN)
    ///    - AND target role == "MAIN".
    ///    - Do NOT broadcast to any unrelated or unauthenticated runtimes.
    ///
    /// 3. Room Correlation:
    ///    - Forward HAND_TRUTH_MIRROR only when:
    ///      TapTap spectator roomId == Steam MAIN active roomId.
    ///    - If rooms differ or Steam MAIN is not in match: DO NOT forward mirror.
    ///      Log: `HAND_TRUTH_MIRROR_ROOM_MISMATCH_REJECTED: spectator_room={} != main_room={}`.
    pub fn send_hand_truth_mirror_to_main(
        &self,
        source_connection_id: &str,
        snapshot: &SpectatorHandPayload,
    ) -> Result<String, String> {
        // Enforce Producer Invariant
        let source_session = match self.session_manager.get(source_connection_id) {
            Some(s) => s,
            None => {
                warn!("HAND_TRUTH_MIRROR rejected: Source session {} not found", source_connection_id);
                return Err("SOURCE_SESSION_NOT_FOUND".to_string());
            }
        };

        if !self.session_manager.is_bound_taptap_spectator(source_connection_id)
            || Some(snapshot.spectator_account_player_id as i64) != source_session.live_player_id
        {
            warn!(
                "HAND_TRUTH_MIRROR producer rejected: session on conn={} is not bound TapTap spectator (platform={}, player_id={:?}, role={})",
                source_connection_id, source_session.platform_id, source_session.player_id, source_session.role
            );
            return Err("PRODUCER_INVARIANT_VIOLATION".to_string());
        }

        // Enforce Target Invariant
        let target_session = match self.session_manager.find_bound_steam_main() {
            Some(t) => t,
            None => {
                warn!("HAND_TRUTH_MIRROR rejected: No verified Steam MAIN session found");
                return Err("TARGET_NOT_FOUND".to_string());
            }
        };

        if target_session.connection_id == source_connection_id {
            warn!("HAND_TRUTH_MIRROR rejected: Source and target session are identical ({})", source_connection_id);
            return Err("SOURCE_EQUALS_TARGET".to_string());
        }

        // Enforce Room Correlation
        let spectator_room = snapshot.room_id;
        let main_room = target_session.room_id.unwrap_or(0);
        let main_in_match = target_session.in_match;

        if !main_in_match {
            warn!(
                "HAND_TRUTH_MIRROR rejected: Steam MAIN (conn={}) is not in match (in_match=false, main_room={})",
                target_session.connection_id, main_room
            );
            return Err("STEAM_MAIN_NOT_IN_MATCH".to_string());
        }

        if spectator_room == 0 || spectator_room != main_room {
            warn!(
                "HAND_TRUTH_MIRROR_ROOM_MISMATCH_REJECTED: spectator_room={} != main_room={}",
                spectator_room, main_room
            );
            return Err(format!(
                "HAND_TRUTH_MIRROR_ROOM_MISMATCH_REJECTED: spectator_room={} != main_room={}",
                spectator_room, main_room
            ));
        }

        // Targeted Forwarding exclusively to verified Steam MAIN connection ID
        let mirror_envelope = BridgeEnvelope::new(
            message_types::HAND_TRUTH_MIRROR,
            serde_json::to_value(snapshot).unwrap_or_default(),
        );
        let mirror_json = match serde_json::to_string(&mirror_envelope) {
            Ok(j) => j,
            Err(e) => {
                warn!("Failed to serialize HAND_TRUTH_MIRROR envelope: {}", e);
                return Err("SERIALIZATION_FAILED".to_string());
            }
        };

        let sent = self.connection_manager.send_to(&target_session.connection_id, &mirror_json);
        if sent {
            info!(
                "HAND_TRUTH_MIRROR forwarded to Steam MAIN (conn={}): roomId={}, epoch={}, seq={}",
                target_session.connection_id, spectator_room, snapshot.spectator_session_epoch, snapshot.sequence_number
            );
            Ok(target_session.connection_id)
        } else {
            warn!(
                "Failed to send HAND_TRUTH_MIRROR to Steam MAIN conn={}",
                target_session.connection_id
            );
            Err("FORWARD_SEND_FAILED".to_string())
        }
    }

    pub fn security_policy(&self) -> super::tcp_security::SecurityVerificationPolicy {
        self.security_policy.lock().unwrap().clone()
    }

    pub fn set_security_policy(&self, policy: super::tcp_security::SecurityVerificationPolicy) {
        *self.security_policy.lock().unwrap() = policy;
    }

    pub fn with_security_policy(self, policy: super::tcp_security::SecurityVerificationPolicy) -> Self {
        self.set_security_policy(policy);
        self
    }

    pub fn subscribe_spectator_snapshots(&self) -> tokio::sync::broadcast::Receiver<SpectatorHandPayload> {
        self.spectator_broadcast_tx.subscribe()
    }

    pub fn broadcast_spectator_snapshot(&self, snapshot: SpectatorHandPayload) {
        let _ = self.spectator_broadcast_tx.send(snapshot);
    }

    pub fn get_latest_spectator_snapshot(&self) -> Option<SpectatorHandPayload> {
        let state = self.state.lock().unwrap();
        state.latest_spectator_snapshot.clone()
    }

    pub fn get_spectator_epoch_and_sequence(&self) -> (u64, u64) {
        let state = self.state.lock().unwrap();
        (state.spectator_active_epoch, state.spectator_highest_sequence)
    }

    pub fn active_generation(&self) -> u64 {
        self.state.lock().unwrap().active_generation
    }

    pub fn get_active_generation(&self) -> u64 {
        self.state.lock().unwrap().active_generation
    }

    pub fn get_next_generation(&self) -> u64 {
        self.state.lock().unwrap().next_generation
    }

    pub fn session_challenge(&self) -> String {
        self.state.lock().unwrap().session_challenge.clone()
    }

    pub fn write_session_challenge_file(&self) {
        let challenge = self.session_challenge();
        let path = if let Ok(appdata) = std::env::var("APPDATA") {
            std::path::PathBuf::from(appdata).join("XingShu").join("Bridge").join("session_challenge.txt")
        } else if let Some(dir) = dirs::data_dir() {
            dir.join("XingShu").join("Bridge").join("session_challenge.txt")
        } else {
            return;
        };

        if let Some(parent) = path.parent() {
            let _ = std::fs::create_dir_all(parent);
        }
        let _ = std::fs::write(&path, challenge.as_bytes());
    }

    pub fn set_session_challenge(&self, challenge: impl Into<String>) {
        self.state.lock().unwrap().session_challenge = challenge.into();
        self.write_session_challenge_file();
    }

    pub fn connection_state(&self) -> BridgeServerState {
        self.state.lock().unwrap().connection_state
    }

    pub fn safe_frame_summary(raw: &str) -> SafeFrameSummary {
        let message_type = Self::extract_json_string_field(raw, "messageType")
            .unwrap_or_else(|| "UNKNOWN".to_string());
        let request_id = Self::extract_json_string_field(raw, "requestId")
            .unwrap_or_else(|| "UNKNOWN".to_string());
        SafeFrameSummary {
            message_type,
            request_id,
            length: raw.len(),
        }
    }

    fn extract_json_string_field(raw: &str, field: &str) -> Option<String> {
        let pattern = format!("\"{}\"", field);
        let pos = raw.find(&pattern)?;
        let rest = &raw[pos + pattern.len()..];
        let colon_pos = rest.find(':')?;
        let after_colon = rest[colon_pos + 1..].trim_start();
        if after_colon.starts_with('"') {
            let quote_end = after_colon[1..].find('"')?;
            Some(after_colon[1..1 + quote_end].to_string())
        } else {
            None
        }
    }

    fn extract_json_u64_field(raw: &str, field: &str) -> Option<u64> {
        let pattern = format!("\"{}\"", field);
        let pos = raw.find(&pattern)?;
        let rest = &raw[pos + pattern.len()..];
        let colon_pos = rest.find(':')?;
        let after_colon = rest[colon_pos + 1..].trim_start();
        let num_end = after_colon.find(|c: char| !c.is_ascii_digit()).unwrap_or(after_colon.len());
        if num_end == 0 {
            return None;
        }
        after_colon[..num_end].parse::<u64>().ok()
    }

    /// Stop background listeners (both Named Pipe and TCP)
    pub fn stop(&self) {
        let mut started = self.is_server_started.lock().unwrap();
        let mut state = self.state.lock().unwrap();
        let pipe_started = *started;
        let tcp_started = state.tcp_server_started;
        if !pipe_started && !tcp_started {
            return;
        }
        *started = false;
        state.tcp_server_started = false;
        state.tcp_addr = None;
        self.shutdown_notify.notify_waiters();
        state.connection_state = BridgeServerState::Disconnected;
        state.runtime_connected = false;
        state.pipe_connected = false;
        state.tcp_connected = false;
        state.active_transport = ActiveTransport::Disconnected;
        state.active_sender = None;
        state.active_generation = 0;
        state.pipe_client_pid = None;
        state.session_id = None;
        let pipe_name = state.pipe_name.clone();
        drop(state);
        drop(started);

        self.session_manager.clear();
        self.connection_manager.clear();

        self.log_sys_event("SERVER_STOP", "XingShu Bridge Server stopped".to_string());

        #[cfg(windows)]
        if pipe_started {
            let _ = tokio::net::windows::named_pipe::ClientOptions::new().open(&pipe_name);
        }
    }

    /// Start the production XingShu Bridge Server:
    /// Starts canonical production TCP listener on 127.0.0.1:25890.
    /// Fails closed (returns Err) if TCP cannot be exclusively bound,
    /// ensuring no orphaned Named Pipe or half-started server state remains.
    /// If already started, idempotently returns Ok(()).
    pub fn start(&self) -> Result<(), String> {
        let mut started = self.is_server_started.lock().unwrap();
        if *started {
            return Ok(());
        }

        // 1. Attempt exclusive bind on canonical production TCP transport (127.0.0.1:25890)
        // If TCP port is squatted or unavailable, fail closed immediately WITHOUT starting Named Pipe!
        if let Err(e) = self.start_tcp_sync(DEFAULT_TCP_ADDR) {
            let err_msg = format!("Failed to start production TCP listener on {}: {}", DEFAULT_TCP_ADDR, e);
            error!("{}", err_msg);
            return Err(err_msg);
        }

        // 2. Mark server started now that canonical TCP transport is live
        *started = true;

        // 3. Start legacy Named Pipe listener (for backwards compatibility / offline transport)
        self.write_session_challenge_file();

        {
            let mut state = self.state.lock().unwrap();
            state.pipe_name = XINGSHU_BRIDGE_NAMED_PIPE.to_string();
        }

        let server_clone = self.clone();
        let target_pipe = XINGSHU_BRIDGE_NAMED_PIPE.to_string();
        if tokio::runtime::Handle::try_current().is_ok() {
            tokio::spawn(async move {
                let _ = server_clone.run_listener(target_pipe).await;
            });
        } else {
            tauri::async_runtime::spawn(async move {
                let _ = server_clone.run_listener(target_pipe).await;
            });
        }

        self.log_sys_event(
            "SERVER_START",
            format!("XingShu Bridge Server started (TCP: {}, Pipe: {})", DEFAULT_TCP_ADDR, XINGSHU_BRIDGE_NAMED_PIPE),
        );

        Ok(())
    }

    /// Start listener on a specific named pipe (allows test isolation)
    pub fn start_with_pipe(&self, pipe_name: &str) {
        let mut started = self.is_server_started.lock().unwrap();
        if *started {
            return;
        }
        *started = true;

        self.write_session_challenge_file();

        {
            let mut state = self.state.lock().unwrap();
            state.pipe_name = pipe_name.to_string();
        }

        let server_clone = self.clone();
        let target_pipe = pipe_name.to_string();
        if tokio::runtime::Handle::try_current().is_ok() {
            tokio::spawn(async move {
                let _ = server_clone.run_listener(target_pipe).await;
            });
        } else {
            tauri::async_runtime::spawn(async move {
                let _ = server_clone.run_listener(target_pipe).await;
            });
        }

        self.log_sys_event(
            "SERVER_START",
            format!("XingShu Bridge IPC Server started on {}", pipe_name),
        );
    }

    /// Background listener loop
    /// Security Classification: LOCAL_SESSION_RESTRICTED
    pub async fn run_listener(self, pipe_name: String) -> Result<(), String> {
        info!("Starting XingShu Bridge Named Pipe listener on {}", pipe_name);

        #[cfg(windows)]
        {
            let mut is_first = true;
            loop {
                if !*self.is_server_started.lock().unwrap() {
                    break;
                }

                let server = match create_named_pipe_server(&pipe_name, is_first) {
                    Ok(s) => {
                        is_first = false;
                        s
                    }
                    Err(e) => {
                        if is_first {
                            // FAIL CLOSED immediately: do NOT fall back to standard create!
                            error!(
                                "{}: Failed to create first pipe instance on {}. Failing closed.",
                                e, pipe_name
                            );
                            self.log_sys_event(
                                "SERVER_ERROR",
                                format!("{}: Named Pipe already owned or squatted on {}", e, pipe_name),
                            );
                            *self.is_server_started.lock().unwrap() = false;
                            {
                                let mut state = self.state.lock().unwrap();
                                state.connection_state = BridgeServerState::Disconnected;
                                state.pipe_connected = false;
                                state.tcp_connected = false;
                                state.active_transport = ActiveTransport::Disconnected;
                                state.runtime_connected = false;
                                state.active_sender = None;
                                state.active_generation = 0;
                            }
                            return Err(e);
                        }

                        error!("Failed to create subsequent Named Pipe server instance ({}): {}", pipe_name, e);
                        tokio::time::sleep(tokio::time::Duration::from_millis(500)).await;
                        continue;
                    }
                };

                info!("XingShu Bridge listening for incoming connection on {}", pipe_name);
                tokio::select! {
                    _ = self.shutdown_notify.notified() => {
                        info!("XingShu Bridge listener received shutdown signal on {}", pipe_name);
                        break;
                    }
                    conn_res = server.connect() => {
                        if !*self.is_server_started.lock().unwrap() {
                            break;
                        }
                        if let Err(e) = conn_res {
                            warn!("Named pipe connect error: {}", e);
                            continue;
                        }
                    }
                }

                info!("XingShu Bridge client connected on {}", pipe_name);
                let server_ref = self.clone();
                tokio::spawn(async move {
                    server_ref.handle_connection(server).await;
                });
            }
            Ok(())
        }

        #[cfg(not(windows))]
        {
            warn!("Named Pipe IPC is only supported on Windows platforms.");
            Ok(())
        }
    }

    /// Synchronously configures and binds exclusive TCP listener, spawning background listener task.
    pub fn start_tcp_sync(&self, addr: &str) -> Result<SocketAddr, String> {
        let parsed_addr = validate_tcp_bind_addr(addr)?;

        // Attempt exclusive bind with SO_EXCLUSIVEADDRUSE
        let listener = match super::tcp_security::create_exclusive_tcp_listener(parsed_addr) {
            Ok(l) => l,
            Err(e) => {
                let err_msg = format!("Port squatting detected or failed to bind exclusive TCP listener on {}: {}", parsed_addr, e);
                error!("{}", err_msg);
                return Err(err_msg);
            }
        };

        let local_addr = listener.local_addr().map_err(|e| e.to_string())?;

        {
            let mut state = self.state.lock().unwrap();
            if state.tcp_server_started {
                return Err(format!("TCP server already started on {:?}", state.tcp_addr));
            }
            state.tcp_server_started = true;
            state.tcp_addr = Some(local_addr);
        }

        self.write_session_challenge_file();

        let server_clone = self.clone();
        if tokio::runtime::Handle::try_current().is_ok() {
            tokio::spawn(async move {
                let _ = server_clone.run_tcp_listener(listener).await;
            });
        } else {
            tauri::async_runtime::spawn(async move {
                let _ = server_clone.run_tcp_listener(listener).await;
            });
        }

        self.log_sys_event(
            "TCP_SERVER_START",
            format!("XingShu Bridge Production TCP Server started on {}", local_addr),
        );

        Ok(local_addr)
    }

    /// Starts the production TCP listener on the specified loopback address.
    /// Strictly verifies that `addr` is 127.0.0.1 (prohibiting 0.0.0.0 or external addresses).
    /// Prevents port squatting by failing closed if the port is already bound.
    pub async fn start_tcp(&self, addr: &str) -> Result<SocketAddr, String> {
        self.start_tcp_sync(addr)
    }

    /// Starts production TCP listener on default address `127.0.0.1:25890`
    pub async fn start_tcp_default(&self) -> Result<SocketAddr, String> {
        self.start_tcp_sync(DEFAULT_TCP_ADDR)
    }

    pub fn stop_tcp(&self) {
        let was_started = {
            let mut state = self.state.lock().unwrap();
            let s = state.tcp_server_started;
            state.tcp_server_started = false;
            state.tcp_addr = None;
            s
        };
        if was_started {
            self.shutdown_notify.notify_waiters();
            self.log_sys_event("TCP_SERVER_STOP", "XingShu Bridge TCP Server stopped".to_string());
        }
    }

    pub fn is_tcp_started(&self) -> bool {
        self.state.lock().unwrap().tcp_server_started
    }

    pub fn tcp_local_addr(&self) -> Option<SocketAddr> {
        self.state.lock().unwrap().tcp_addr
    }

    /// Background TCP listener loop
    pub async fn run_tcp_listener(self, listener: tokio::net::TcpListener) -> Result<(), String> {
        let local_addr = listener.local_addr().map_err(|e| e.to_string())?;
        info!("Starting XingShu Bridge TCP listener on {}", local_addr);

        loop {
            tokio::select! {
                _ = self.shutdown_notify.notified() => {
                    info!("XingShu Bridge TCP listener received shutdown signal on {}", local_addr);
                    break;
                }
                accept_res = listener.accept() => {
                    let (socket, remote_addr) = match accept_res {
                        Ok(conn) => conn,
                        Err(e) => {
                            warn!("TCP accept error on {}: {}", local_addr, e);
                            continue;
                        }
                    };

                    // Strict remote address verification: verify loopback (127.0.0.1)
                    if remote_addr.ip() != IpAddr::V4(Ipv4Addr::new(127, 0, 0, 1)) && !remote_addr.ip().is_loopback() {
                        warn!("Rejected non-loopback TCP connection from {}", remote_addr);
                        continue;
                    }

                    info!("XingShu Bridge TCP client connected from {}", remote_addr);
                    let server_ref = self.clone();
                    tokio::spawn(async move {
                        server_ref.handle_tcp_connection(socket, remote_addr, local_addr).await;
                    });
                }
            }
        }
        Ok(())
    }

    async fn handle_tcp_connection(
        &self,
        socket: tokio::net::TcpStream,
        remote_addr: SocketAddr,
        local_addr: SocketAddr,
    ) {
        #[cfg(windows)]
        let peer_pid = {
            let mut pid_res = Err("TCP peer verification initial".to_string());
            for _ in 0..5 {
                pid_res = get_tcp_peer_pid(local_addr, remote_addr);
                if pid_res.is_ok() {
                    break;
                }
                tokio::time::sleep(tokio::time::Duration::from_millis(5)).await;
            }
            match pid_res {
                Ok(pid) => {
                    info!("TCP peer PID resolved via GetExtendedTcpTable for {} (PID={})", remote_addr, pid);
                    Some(pid)
                }
                Err(e) => {
                    warn!("TCP peer PID resolution failed for {}: {}. Rejecting client.", remote_addr, e);
                    return;
                }
            }
        };

        #[cfg(not(windows))]
        let peer_pid: Option<u32> = None;

        let (read_half, write_half) = tokio::io::split(socket);
        self.handle_transport_stream(
            read_half,
            write_half,
            peer_pid,
            &format!("TCP [{}]", remote_addr),
            true,
        ).await;
    }

    /// Validates the session challenge token against the expected challenge.
    pub fn validate_session_challenge(&self, client_challenge: Option<&str>) -> Result<(), &'static str> {
        let expected = self.session_challenge();
        match client_challenge {
            None => Err(error_codes::AUTH_CHALLENGE_REQUIRED),
            Some(ch) if ch != expected => Err(error_codes::AUTH_CHALLENGE_FAILED),
            Some(_) => Ok(()),
        }
    }

    /// Shared Protocol Gate validator for HELLO messages across all transports.
    pub fn validate_hello_message(
        &self,
        envelope: &BridgeEnvelope<serde_json::Value>,
        peer_pid: Option<u32>,
    ) -> Result<HelloPayload, (&'static str, String)> {
        // 1. Protocol version
        if envelope.protocol_version != XINGSHU_BRIDGE_PROTOCOL_VERSION {
            return Err((
                error_codes::UNSUPPORTED_PROTOCOL,
                format!("Unsupported protocol version: {}", envelope.protocol_version),
            ));
        }

        // 2. Message type
        if envelope.message_type != message_types::HELLO {
            return Err((
                error_codes::PROTOCOL_GATE_VIOLATION,
                "First message must be HELLO".to_string(),
            ));
        }

        // 3. Payload deserialization
        let hello: HelloPayload = serde_json::from_value(envelope.payload.clone()).map_err(|e| {
            (
                error_codes::MALFORMED_HELLO_PAYLOAD,
                format!("Malformed HELLO payload: {}", e),
            )
        })?;

        // 4. Component identity
        if hello.component.as_deref() != Some("XingShu_Runtime") {
            return Err((
                error_codes::INVALID_COMPONENT,
                "Component must be XingShu_Runtime".to_string(),
            ));
        }

        // 5. Multi-runtime concurrent session support:
        // In the multi-runtime bridge architecture, multiple runtimes (e.g. Steam CN, TapTap)
        // are allowed to connect concurrently.

        // 6. Session challenge validation
        if let Err(code) = self.validate_session_challenge(hello.session_challenge.as_deref()) {
            let msg = if code == error_codes::AUTH_CHALLENGE_REQUIRED {
                "Authentication failed: session challenge token required".to_string()
            } else {
                "Authentication failed: invalid session challenge token".to_string()
            };
            return Err((code, msg));
        }

        // 7. Game PID validation
        let game_pid = match hello.game_pid {
            Some(pid) if pid > 0 => pid,
            _ => {
                return Err((
                    error_codes::MISSING_GAME_PID,
                    "game_pid is required and must be greater than 0".to_string(),
                ));
            }
        };

        if let Some(actual_peer_pid) = peer_pid {
            if actual_peer_pid > 0 && game_pid != actual_peer_pid {
                return Err((
                    error_codes::PID_MISMATCH,
                    format!("game_pid {} does not match client process id {}", game_pid, actual_peer_pid),
                ));
            }
        }

        // 8. Capabilities validation
        let capabilities = match &hello.capabilities {
            Some(caps) if !caps.is_empty() => caps,
            _ => {
                return Err((
                    error_codes::CAPABILITIES_REQUIRED,
                    "capabilities list is required and cannot be empty".to_string(),
                ));
            }
        };

        for cap in capabilities {
            if !ALLOWED_CAPABILITIES.contains(&cap.as_str()) {
                return Err((
                    error_codes::UNKNOWN_CAPABILITY,
                    format!("Unknown capability: {}", cap),
                ));
            }
        }

        Ok(hello)
    }

    /// Validates a HELLO_ACK envelope received from a server.
    pub fn validate_hello_ack(
        envelope: &BridgeEnvelope<serde_json::Value>,
        expected_challenge: Option<&str>,
    ) -> Result<HelloAckPayload, String> {
        if envelope.protocol_version != XINGSHU_BRIDGE_PROTOCOL_VERSION {
            return Err(format!("Unsupported protocol version: {}", envelope.protocol_version));
        }
        if envelope.message_type != message_types::HELLO_ACK {
            return Err(format!("Expected HELLO_ACK but got {}", envelope.message_type));
        }
        let ack: HelloAckPayload = serde_json::from_value(envelope.payload.clone())
            .map_err(|e| format!("Malformed HELLO_ACK payload: {}", e))?;

        if ack.status != "ACCEPTED" {
            return Err(format!("HELLO_ACK status not accepted: {}", ack.status));
        }
        if let Some(expected) = expected_challenge {
            if ack.session_challenge.as_deref() != Some(expected) {
                return Err("HELLO_ACK session challenge mismatch".to_string());
            }
        }
        Ok(ack)
    }

    /// Unified transport connection handler for both Named Pipe and TCP.
    async fn handle_transport_stream<R, W>(
        &self,
        reader: R,
        mut writer: W,
        peer_pid: Option<u32>,
        transport_desc: &str,
        is_tcp: bool,
    ) where
        R: tokio::io::AsyncRead + Unpin + Send + 'static,
        W: tokio::io::AsyncWrite + Unpin + Send + 'static,
    {
        use tokio::io::{AsyncWriteExt, BufReader};

        // Transition state to PipeConnected if currently Disconnected
        {
            let mut state = self.state.lock().unwrap();
            if state.connection_state == BridgeServerState::Disconnected {
                state.pipe_connected = !is_tcp;
                state.tcp_connected = is_tcp;
                state.active_transport = if is_tcp {
                    ActiveTransport::LoopbackTcp
                } else {
                    ActiveTransport::NamedPipeLegacy
                };
                state.connection_state = BridgeServerState::PipeConnected;
                state.pipe_client_pid = peer_pid;
            }
        }
        self.log_sys_event(
            "TRANSPORT_CONNECTED",
            format!("Client connected via {} (PID={:?})", transport_desc, peer_pid),
        );

        let (tx, mut rx) = tokio::sync::mpsc::channel::<String>(64);

        // Spawn outbound writer task
        let desc_writer = transport_desc.to_string();
        let writer_handle = tokio::spawn(async move {
            while let Some(line) = rx.recv().await {
                if let Err(e) = writer.write_all(line.as_bytes()).await {
                    error!("Error writing to {}: {}", desc_writer, e);
                    break;
                }
                if let Err(e) = writer.flush().await {
                    error!("Error flushing {}: {}", desc_writer, e);
                    break;
                }
            }
        });

        let mut buf_reader = BufReader::new(reader);

        // 1. PROTOCOL GATE: FIRST message MUST be a valid HELLO frame
        let first_line = match read_bounded_line(&mut buf_reader, MAX_MESSAGE_SIZE).await {
            Ok(Some(l)) => l,
            Ok(None) => {
                info!("{} client disconnected before sending HELLO.", transport_desc);
                writer_handle.abort();
                self.on_pipe_disconnected();
                return;
            }
            Err(e) => {
                warn!("{} read error during HELLO handshake: {:?}. Rejecting connection.", transport_desc, e);
                writer_handle.abort();
                self.on_pipe_disconnected();
                return;
            }
        };

        let trimmed_first = first_line.trim();
        let hello_envelope: BridgeEnvelope<serde_json::Value> = match serde_json::from_str(trimmed_first) {
            Ok(env) => env,
            Err(e) => {
                let summary = Self::safe_frame_summary(trimmed_first);
                warn!(
                    "Protocol Gate: Failed to parse initial frame JSON: {}. Safe summary: [Length={}, Type={}, ReqId={}]. Rejecting client.",
                    e, summary.length, summary.message_type, summary.request_id
                );
                let _ = tx.send(format!("{}\n", serde_json::to_string(&BridgeEnvelope::new(
                    message_types::ERROR,
                    serde_json::json!({
                        "code": error_codes::MALFORMED_HELLO,
                        "message": "Malformed JSON in initial handshake"
                    }),
                )).unwrap())).await;
                tokio::time::sleep(tokio::time::Duration::from_millis(200)).await;
                writer_handle.abort();
                self.on_pipe_disconnected();
                return;
            }
        };

        let hello = match self.validate_hello_message(&hello_envelope, peer_pid) {
            Ok(h) => h,
            Err((code, msg)) => {
                warn!("Protocol Gate rejected client on {}: {} - {}", transport_desc, code, msg);
                let _ = tx.send(format!("{}\n", serde_json::to_string(&BridgeEnvelope::new(
                    message_types::ERROR,
                    serde_json::json!({
                        "code": code,
                        "message": msg
                    }),
                )).unwrap())).await;
                tokio::time::sleep(tokio::time::Duration::from_millis(200)).await;
                writer_handle.abort();
                if code != error_codes::BUSY_SINGLE_CLIENT_ONLY {
                    self.on_pipe_disconnected();
                }
                return;
            }
        };

        // 5-Layer Identity Gate for TCP connections
        #[cfg(windows)]
        let (verified_path, verified_platform_id) = if is_tcp {
            let actual_pid = peer_pid.unwrap_or(0);
            let policy = self.security_policy();
            match super::tcp_security::verify_tcp_client_identity_with_policy(
                actual_pid,
                hello.game_pid.unwrap_or(0),
                &policy,
            ) {
                Ok(report) => {
                    info!(
                        "5-layer TCP security gate PASSED for PID={}: image={}, steam_cn={}, platform={:?}",
                        report.actual_pid, report.executable_name, report.is_steam_cn_path, report.platform_id
                    );
                    (report.executable_path, report.platform_id)
                }
                Err(e) => {
                    warn!(
                        "5-layer TCP security gate REJECTED client on {}: {}. Denying authentication.",
                        transport_desc, e
                    );
                    let (code, msg) = match e {
                        super::tcp_security::SecurityError::PidSpoofingDetected { claimed, actual } => (
                            error_codes::PID_MISMATCH,
                            format!("game_pid {} does not match client process id {}", claimed, actual),
                        ),
                        super::tcp_security::SecurityError::InvalidPid => (
                            error_codes::MISSING_GAME_PID,
                            "game_pid is required and must be greater than 0".to_string(),
                        ),
                        other => (
                            error_codes::SECURITY_TRUST_VIOLATION,
                            format!("Security trust validation failed: {}", other),
                        ),
                    };
                    let _ = tx.send(format!("{}\n", serde_json::to_string(&BridgeEnvelope::new(
                        message_types::ERROR,
                        serde_json::json!({
                            "code": code,
                            "message": msg
                        }),
                    )).unwrap())).await;
                    tokio::time::sleep(tokio::time::Duration::from_millis(200)).await;
                    writer_handle.abort();
                    self.on_pipe_disconnected();
                    return;
                }
            }
        } else {
            (format!("PID_{}", hello.game_pid.unwrap_or(0)), None)
        };

        #[cfg(not(windows))]
        let (verified_path, verified_platform_id) = (
            format!("PID_{}", hello.game_pid.unwrap_or(0)),
            None,
        );

        let game_pid = hello.game_pid.unwrap_or(peer_pid.unwrap_or(0));
        let now = Utc::now().timestamp_millis();
        let connection_id = format!("conn_{}_{}", game_pid, now);

        // Authoritative platform must come from verified TCP peer PID -> executable path -> PlatformProfile -> verified_platform_id.
        // HELLO.platformId is only a claim.
        // Rules:
        // claimed platform absent: use verified platform
        // claimed platform == verified platform: PASS
        // claimed platform != verified platform: REJECT authentication
        let authoritative_platform_id = match verified_platform_id {
            Some(ref verified) => {
                if let Some(ref claimed) = hello.platform_id {
                    if !claimed.eq_ignore_ascii_case(verified) {
                        warn!(
                            "Platform spoofing detected on {}: claimed '{}', but OS process verified as '{}'. Rejecting authentication.",
                            transport_desc, claimed, verified
                        );
                        let _ = tx.send(format!("{}\n", serde_json::to_string(&BridgeEnvelope::new(
                            message_types::ERROR,
                            serde_json::json!({
                                "code": error_codes::SECURITY_TRUST_VIOLATION,
                                "message": format!("Platform mismatch: claimed '{}', but verified as '{}'", claimed, verified)
                            }),
                        )).unwrap())).await;
                        tokio::time::sleep(tokio::time::Duration::from_millis(200)).await;
                        writer_handle.abort();
                        self.on_pipe_disconnected();
                        return;
                    }
                }
                verified.clone()
            }
            None => {
                if let Some(resolved) = super::tcp_security::resolve_platform_id_from_path(std::path::Path::new(&verified_path)) {
                    if let Some(ref claimed) = hello.platform_id {
                        if !claimed.eq_ignore_ascii_case(&resolved) {
                            warn!(
                                "Platform spoofing detected on {}: claimed '{}', but path resolved as '{}'. Rejecting authentication.",
                                transport_desc, claimed, resolved
                            );
                            let _ = tx.send(format!("{}\n", serde_json::to_string(&BridgeEnvelope::new(
                                message_types::ERROR,
                                serde_json::json!({
                                    "code": error_codes::SECURITY_TRUST_VIOLATION,
                                    "message": format!("Platform mismatch: claimed '{}', but resolved as '{}'", claimed, resolved)
                                }),
                            )).unwrap())).await;
                            tokio::time::sleep(tokio::time::Duration::from_millis(200)).await;
                            writer_handle.abort();
                            self.on_pipe_disconnected();
                            return;
                        }
                    }
                    resolved
                } else if let Some(ref claimed) = hello.platform_id {
                    claimed.clone()
                } else {
                    "STEAM_CN".to_string()
                }
            }
        };

        let platform_id = authoritative_platform_id;

        // HELLO.role must NOT be authoritative. Initial server role is strictly UNBOUND.
        // Frontend / resolver assigns MAIN or USER_SPECTATOR from:
        // verified platform + real current playerId + GameAccountBinding.
        let role = "UNBOUND".to_string();

        let generation: u64 = 1;
        let expected_challenge = self.session_challenge();

        // Register in connection_manager
        let active_conn = ActiveConnection::new(
            connection_id.clone(),
            platform_id.clone(),
            game_pid,
            generation,
            tx.clone(),
        );
        self.connection_manager.insert(active_conn);

        // Register in session_manager
        let session = RuntimeSession::new(
            connection_id.clone(),
            platform_id.clone(),
            game_pid,
            verified_path.clone(),
            hello.player_id,
            hello.runtime_version.clone().unwrap_or_else(|| "1.0.0".to_string()),
            generation,
            role.clone(),
        );
        self.session_manager.insert(session.clone());
        self.emit_session_changed(&session);

        {
            let mut state = self.state.lock().unwrap();
            state.next_generation += 1;
            state.active_generation = state.next_generation;
            state.connection_state = BridgeServerState::RuntimeAuthenticated;
            state.runtime_connected = true;
            state.pipe_connected = !is_tcp;
            state.tcp_connected = is_tcp;
            state.active_transport = if is_tcp {
                ActiveTransport::LoopbackTcp
            } else {
                ActiveTransport::NamedPipeLegacy
            };
            state.session_id = Some(connection_id.clone());
            state.active_sender = Some(tx.clone());
            state.connected_at = Some(now);
            state.last_heartbeat_timestamp = Some(now);
            state.game_pid = Some(game_pid);
            state.game_version = hello.game_version.clone();
            state.runtime_version = hello.runtime_version.clone();
            // Reset spectator transport session state upon new Runtime HELLO session
            state.spectator_active_epoch = 0;
            state.spectator_highest_sequence = 0;
            state.latest_spectator_snapshot = None;
            state.total_messages_received += 1;

            let entry = BridgeLogEntry {
                id: Uuid::new_v4().to_string(),
                direction: "IN".to_string(),
                message_type: message_types::HELLO.to_string(),
                summary: format!("HELLO (PID={}, Platform={}, Role={}) Gen={}", game_pid, platform_id, role, generation),
                raw_json: self.scrub_credentials(trimmed_first),
                timestamp: now,
            };
            state.recent_messages.push_back(entry);
            if state.recent_messages.len() > 100 {
                state.recent_messages.pop_front();
            }
        }

        info!(
            "Protocol Gate PASSED: XingShu Runtime authenticated via {} (conn={}, Gen={}, PID={}, Platform={}, Role={})",
            transport_desc, connection_id, generation, game_pid, platform_id, role
        );

        // Send HELLO_ACK with connection_id, generation, and accepted status
        let ack_payload = HelloAckPayload {
            status: "ACCEPTED".to_string(),
            hub_version: env!("CARGO_PKG_VERSION").to_string(),
            protocol_version: XINGSHU_BRIDGE_PROTOCOL_VERSION.to_string(),
            heartbeat_interval_ms: 3000,
            session_challenge: Some(expected_challenge),
            session_id: Some(connection_id.clone()),
            connection_id: Some(connection_id.clone()),
            generation: Some(generation),
            capabilities: Some(vec![
                "spectator_hand_snapshot".to_string(),
                "status".to_string(),
                "events".to_string(),
            ]),
        };
        let ack_envelope = BridgeEnvelope::with_request_id(
            message_types::HELLO_ACK,
            hello_envelope.request_id,
            serde_json::to_value(&ack_payload).unwrap(),
        );
        let ack_line = format!("{}\n", serde_json::to_string(&ack_envelope).unwrap());
        let _ = tx.send(ack_line).await;

        // 2. Loop for subsequent messages with bounded line reading (64 KiB strict bound)
        loop {
            tokio::select! {
                _ = self.shutdown_notify.notified() => {
                    info!("{} handle_connection received shutdown signal [{} Gen={}].", transport_desc, connection_id, generation);
                    break;
                }
                msg_res = read_bounded_line(&mut buf_reader, MAX_MESSAGE_SIZE) => {
                    match msg_res {
                        Ok(Some(line)) => {
                            let trimmed = line.trim();
                            if !trimmed.is_empty() {
                                self.process_incoming_message_for_connection(
                                    trimmed,
                                    &connection_id,
                                    &platform_id,
                                    generation,
                                ).await;
                            }
                        }
                        Ok(None) => {
                            info!("{} client disconnected (EOF) [{} Gen={}].", transport_desc, connection_id, generation);
                            break;
                        }
                        Err(BoundedLineError::TooLong) => {
                            warn!("Incoming message exceeds 64KB bound [{} Gen={}]. Aborting session.", connection_id, generation);
                            break;
                        }
                        Err(BoundedLineError::Truncated) => {
                            warn!("Incoming message was truncated before newline [{} Gen={}]. Aborting session.", connection_id, generation);
                            break;
                        }
                        Err(BoundedLineError::InvalidUtf8) => {
                            warn!("Incoming message contained invalid UTF-8 [{} Gen={}]. Aborting session.", connection_id, generation);
                            break;
                        }
                        Err(BoundedLineError::Io(e)) => {
                            info!("{} read error [{} Gen={}]: {:?}", transport_desc, connection_id, generation, e);
                            break;
                        }
                    }
                }
            }
        }

        // Cleanup after client disconnection cleanly without resetting other clients!
        writer_handle.abort();
        self.connection_manager.remove(&connection_id);
        self.session_manager.remove(&connection_id);
        self.emit_session_disconnected(&connection_id);

        let is_empty = self.session_manager.is_empty();
        {
            let mut state = self.state.lock().unwrap();
            state.match_contexts.remove(&connection_id);
            if is_empty {
                state.connection_state = BridgeServerState::Disconnected;
                state.runtime_connected = false;
                state.pipe_connected = false;
                state.tcp_connected = false;
                state.active_transport = ActiveTransport::Disconnected;
                state.active_sender = None;
                state.active_generation = 0;
                state.pipe_client_pid = None;
                state.session_id = None;
            }
            let entry = BridgeLogEntry {
                id: Uuid::new_v4().to_string(),
                direction: "SYS".to_string(),
                message_type: "DISCONNECTED".to_string(),
                summary: format!("Runtime connection disconnected: {} (Gen={})", connection_id, generation),
                raw_json: "".to_string(),
                timestamp: Utc::now().timestamp_millis(),
            };
            state.recent_messages.push_back(entry);
            if state.recent_messages.len() > 100 {
                state.recent_messages.pop_front();
            }
            info!(
                "XingShu Bridge session cleanup: {} disconnected (remaining active sessions={}).",
                connection_id,
                self.session_manager.count()
            );
        }
    }

    #[cfg(windows)]
    async fn handle_connection(&self, server: tokio::net::windows::named_pipe::NamedPipeServer) {
        use std::os::windows::io::AsRawHandle;

        let raw_handle = server.as_raw_handle();
        let client_pid_res = verify_pipe_client_security(raw_handle);
        let client_pid = match client_pid_res {
            Ok(pid) => {
                info!("Named Pipe Security Trust validation succeeded (PID={})", pid);
                pid
            }
            Err(e) => {
                warn!("Named Pipe Security Trust validation failed: {}. Rejecting client.", e);
                return;
            }
        };

        let (reader, writer) = tokio::io::split(server);
        self.handle_transport_stream(
            reader,
            writer,
            Some(client_pid),
            &format!("Named Pipe (PID={})", client_pid),
            false,
        ).await;
    }

    fn on_pipe_disconnected(&self) {
        let mut state = self.state.lock().unwrap();
        if state.connection_state == BridgeServerState::PipeConnected {
            state.connection_state = BridgeServerState::Disconnected;
            state.pipe_connected = false;
            state.tcp_connected = false;
            state.active_transport = ActiveTransport::Disconnected;
            state.pipe_client_pid = None;
        }
    }

    pub fn on_client_disconnected(&self, generation: u64) {
        let mut state = self.state.lock().unwrap();
        if generation > 0 && state.active_generation == generation {
            state.connection_state = BridgeServerState::Disconnected;
            state.runtime_connected = false;
            state.pipe_connected = false;
            state.tcp_connected = false;
            state.active_transport = ActiveTransport::Disconnected;
            state.active_sender = None;
            state.active_generation = 0;
            state.pipe_client_pid = None;
            state.session_id = None;
            let entry = BridgeLogEntry {
                id: Uuid::new_v4().to_string(),
                direction: "SYS".to_string(),
                message_type: "DISCONNECTED".to_string(),
                summary: format!("XingShu Runtime client disconnected (Gen={})", generation),
                raw_json: "".to_string(),
                timestamp: Utc::now().timestamp_millis(),
            };
            state.recent_messages.push_back(entry);
            if state.recent_messages.len() > 100 {
                state.recent_messages.pop_front();
            }
            info!("XingShu Bridge state updated: Runtime Disconnected (Gen={}).", generation);
        } else {
            info!("Ignoring stale or unauthenticated disconnect for Gen={} (current active Gen={})", generation, state.active_generation);
        }
    }

    pub async fn process_incoming_message_for_connection(
        &self,
        raw_json: &str,
        connection_id: &str,
        platform_id: &str,
        generation: u64,
    ) {
        let envelope: BridgeEnvelope<serde_json::Value> = match serde_json::from_str(raw_json) {
            Ok(env) => env,
            Err(e) => {
                let summary = Self::safe_frame_summary(raw_json);
                warn!(
                    "Bridge envelope JSON parse error on {}: {}. Safe summary: [Length={}, Type={}, ReqId={}]",
                    connection_id, e, summary.length, summary.message_type, summary.request_id
                );
                return;
            }
        };

        if envelope.protocol_version != XINGSHU_BRIDGE_PROTOCOL_VERSION {
            warn!("Rejected incoming message with unsupported protocol version: {}", envelope.protocol_version);
            return;
        }

        for prohibited in PROHIBITED_CREDENTIAL_KEYS {
            if raw_json.contains(prohibited) {
                warn!("Security alert: Prohibited sensitive credential key '{}' detected in bridge message! Rejecting frame.", prohibited);
                return;
            }
        }

        let now = Utc::now().timestamp_millis();
        let summary = format!("[{}] Type: {}, ReqId: {}", connection_id, envelope.message_type, envelope.request_id);

        {
            let mut state = self.state.lock().unwrap();
            state.total_messages_received += 1;
            let entry = BridgeLogEntry {
                id: Uuid::new_v4().to_string(),
                direction: "IN".to_string(),
                message_type: envelope.message_type.clone(),
                summary,
                raw_json: self.scrub_credentials(raw_json),
                timestamp: now,
            };
            state.recent_messages.push_back(entry);
            if state.recent_messages.len() > 100 {
                state.recent_messages.pop_front();
            }
        }

        let msg_gen = Self::extract_json_u64_field(raw_json, "generation");
        if envelope.message_type != message_types::HELLO {
            if let Some(mg) = msg_gen {
                if mg != generation {
                    warn!(
                        "Rejected {} message on {}: message generation {} does not match connection generation {}",
                        envelope.message_type, connection_id, mg, generation
                    );
                    return;
                }
            }
        }

        match envelope.message_type.as_str() {
            message_types::HEARTBEAT => {
                let hb: HeartbeatPayload = serde_json::from_value(envelope.payload.clone()).unwrap_or_default();
                let latency_ms = hb.client_timestamp.map(|client_ts| (now - client_ts).max(0) as u64);

                {
                    let mut state = self.state.lock().unwrap();
                    state.last_heartbeat_timestamp = Some(now);
                    if let Some(lat) = latency_ms {
                        state.last_heartbeat_latency_ms = Some(lat);
                    }
                }

                if let Some(session) = self.session_manager.touch_session(connection_id) {
                    self.emit_session_changed(&session);
                }

                let reply_payload = HeartbeatPayload {
                    sequence: hb.sequence,
                    client_timestamp: hb.client_timestamp,
                    server_timestamp: Some(now),
                    echo_timestamp: hb.client_timestamp,
                };
                let reply_envelope = BridgeEnvelope::with_request_id(
                    message_types::HEARTBEAT,
                    envelope.request_id.clone(),
                    serde_json::to_value(&reply_payload).unwrap(),
                );
                self.send_envelope_to(connection_id, &reply_envelope);
            }
            message_types::SPECTATOR_HAND_SNAPSHOT => {
                let mut payload: SpectatorHandPayload = match serde_json::from_value(envelope.payload.clone()) {
                    Ok(p) => p,
                    Err(e) => {
                        warn!("Failed to parse SPECTATOR_HAND_SNAPSHOT payload on {}: {}", connection_id, e);
                        return;
                    }
                };

                if payload.schema_version != "1.1.0" {
                    warn!(
                        "Rejected SPECTATOR_HAND_SNAPSHOT with unsupported schema_version '{}' (expected '1.1.0')",
                        payload.schema_version
                    );
                    return;
                }

                // Tag payload with connection_id and platform_id so Hub knows exactly which runtime produced the event!
                payload.connection_id = Some(connection_id.to_string());
                payload.platform_id = Some(platform_id.to_string());

                // Check monotonicity per connection
                if let Err(err) = self.connection_manager.check_and_update_spectator_monotonicity(
                    connection_id,
                    payload.spectator_session_epoch,
                    payload.sequence_number,
                ) {
                    warn!(
                        "SPECTATOR_HAND_SNAPSHOT rejected for connection {}: {} (epoch={}, seq={})",
                        connection_id, err, payload.spectator_session_epoch, payload.sequence_number
                    );
                    return;
                }

                // Retain latest snapshot
                {
                    let mut state = self.state.lock().unwrap();
                    state.spectator_active_epoch = payload.spectator_session_epoch;
                    state.spectator_highest_sequence = payload.sequence_number;
                    state.latest_spectator_snapshot = Some(payload.clone());
                }

                // STRICT IDENTITY RULE:
                // Runtime identity may ONLY come from explicit authenticated local player identity,
                // spectatorAccountPlayerId, or another formally verified local-account identity source.
                // NEVER from snapshot.player_id / payload.player_id (which is the watched remote room player).
                if payload.spectator_account_player_id > 0 {
                    if let Some(session) = self.session_manager.get(connection_id) {
                        if session.player_id.is_none() || session.player_id == Some(0) {
                            if let Some(updated) = self.session_manager.update_player_id(connection_id, payload.spectator_account_player_id as i64) {
                                self.emit_session_changed(&updated);
                            }
                        }
                    }
                }

                info!(
                    "BRIDGE_SNAPSHOT_ACCEPTED conn={}, platform={}, roomId={}, epoch={}, seq={}, playerId={}, handSize={}",
                    connection_id,
                    platform_id,
                    payload.room_id,
                    payload.spectator_session_epoch,
                    payload.sequence_number,
                    payload.player_id,
                    payload.hand_size
                );

                let _ = self.spectator_broadcast_tx.send(payload.clone());

                // Track spectator session's match context with snapshot's room if not set
                if let Some(session) = self.session_manager.get(connection_id) {
                    if session.room_id.is_none() || session.room_id == Some(0) {
                        if let Some(updated) = self.session_manager.update_match_context(
                            connection_id,
                            payload.room_id,
                            true,
                            None,
                        ) {
                            self.emit_session_changed(&updated);
                        }
                    }
                }

                // HS4-P1.6R2.1 Lane 2: Targeted Secure Mirror Forwarding to verified Steam MAIN
                // Generic broadcast_except is REMOVED. Mirror is routed exclusively to verified Steam MAIN.
                if let Err(err) = self.send_hand_truth_mirror_to_main(connection_id, &payload) {
                    info!("HAND_TRUTH_MIRROR forwarding skipped or rejected: {}", err);
                }
            }
            message_types::RUNTIME_IDENTITY_UPDATE => {
                let payload: RuntimeIdentityUpdatePayload = match serde_json::from_value(envelope.payload.clone()) {
                    Ok(p) => p,
                    Err(e) => {
                        warn!("Failed to parse RUNTIME_IDENTITY_UPDATE on {}: {}", connection_id, e);
                        return;
                    }
                };
                let effective_player_id = payload.player_id;
                if effective_player_id > 0 {
                    info!(
                        "RUNTIME_IDENTITY_UPDATE received on conn={}: playerId={}, displayName={:?}, platformId={:?}, identitySource={:?}",
                        connection_id, effective_player_id, payload.display_name, payload.platform_id, payload.identity_source
                    );
                    if let Some(session) = self.session_manager.get(connection_id) {
                        if payload.platform_id.as_deref().is_some_and(|p| p != session.platform_id)
                            || payload.game_pid.is_some_and(|p| p != session.pid)
                            || payload.is_self == Some(false) {
                            warn!("Rejected runtime identity inconsistent with connection {}", connection_id);
                            return;
                        }
                        // STRICT MULTI-SESSION ISOLATION:
                        // Updates ONLY this specific connection's session!
                        if session.player_id != Some(effective_player_id) {
                            if let Some(updated) = self.session_manager.update_player_id(connection_id, effective_player_id) {
                                info!(
                                    "Updated session playerId for conn={}, platform={}: old={:?} -> new={}, role={}",
                                    connection_id, updated.platform_id, session.player_id, effective_player_id, updated.role
                                );
                                self.emit_session_changed(&updated);
                            }
                        }
                    }
                }
            }
            message_types::RUNTIME_STATUS => {
                let status: RuntimeStatusPayload = serde_json::from_value(envelope.payload.clone()).unwrap_or_default();
                let mut state = self.state.lock().unwrap();
                if let Some(pid) = status.game_pid {
                    state.game_pid = Some(pid);
                }
                if let Some(gv) = status.game_version {
                    state.game_version = Some(gv);
                }
                if let Some(rv) = status.runtime_version {
                    state.runtime_version = Some(rv);
                }
                drop(state);
                if let Some(pid) = status.player_id {
                    if pid > 0 {
                        if let Some(session) = self.session_manager.get(connection_id) {
                            if session.player_id != Some(pid) {
                                if let Some(updated) = self.session_manager.update_player_id(connection_id, pid) {
                                    self.emit_session_changed(&updated);
                                }
                            }
                        }
                    }
                }
                if let Some(im) = status.in_match {
                    if let Some(session) = self.session_manager.get(connection_id) {
                        // Legacy status cannot erase a full context, roster or watch code.
                        if session.match_context.is_none() {
                            let current_room = session.room_id.unwrap_or(0);
                            if let Some(updated) = self.session_manager.update_match_context(connection_id, current_room, im, session.is_pve) {
                                self.emit_session_changed(&updated);
                            }
                        }
                    }
                }
                if let Some(session) = self.session_manager.touch_session(connection_id) {
                    self.emit_session_changed(&session);
                }
            }
            message_types::RUNTIME_MATCH_CONTEXT => {
                let mut payload: RuntimeMatchContext = match serde_json::from_value(envelope.payload.clone()) {
                    Ok(p) => p,
                    Err(e) => {
                        warn!("Failed to parse RUNTIME_MATCH_CONTEXT on {}: {}", connection_id, e);
                        return;
                    }
                };
                if payload.connection_id.is_empty() {
                    payload.connection_id = connection_id.to_string();
                }
                if payload.platform_id.is_empty() {
                    payload.platform_id = platform_id.to_string();
                }
                if payload.generation.is_none() {
                    payload.generation = Some(generation);
                }
                payload.normalize();

                let previous_main = self.session_manager.get(connection_id)
                    .filter(|s| s.bound_role == "MAIN" && s.in_match);
                if let Some(updated) = self.session_manager.update_match_context_struct(
                    connection_id,
                    payload.clone(),
                ) {
                    self.emit_session_changed(&updated);
                } else {
                    warn!("Rejected stale or cross-connection match context on {}", connection_id);
                    return;
                }

                if let Some(previous) = previous_main {
                    let old_room = previous.room_id.unwrap_or(0);
                    if payload.match_state != "MAIN_IN_MATCH" || payload.get_internal_room_id() != Some(old_room as i64) {
                        for watcher in self.session_manager.list().into_iter().filter(|s| self.session_manager.is_bound_taptap_spectator(&s.connection_id)) {
                            let stop = BridgeEnvelope::new("WATCH_STOP", serde_json::json!({
                                "roomId": old_room, "expectedSpectatorPlayerId": watcher.live_player_id,
                                "targetPlatform": watcher.platform_id
                            }));
                            self.send_envelope_to(&watcher.connection_id, &stop);
                        }
                        self.state.lock().unwrap().latest_spectator_snapshot = None;
                        if let Some(app) = self.app_handle.lock().unwrap().as_ref() {
                            let _ = app.emit("spectator://session-ended", serde_json::json!({"roomId": old_room}));
                        }
                    }
                }

                {
                    let mut state = self.state.lock().unwrap();
                    state.match_contexts.insert(connection_id.to_string(), payload.clone());
                }

                info!(
                    "RUNTIME_MATCH_CONTEXT updated for conn={}, platform={}: internalRoomId={:?}, spectatorCode={:?}, matchState={}, matchKind={:?}, localPlayerId={:?}",
                    connection_id, platform_id, payload.internal_room_id, payload.spectator_code, payload.match_state, payload.match_kind, payload.local_player_id
                );
            }
            message_types::TEST_EVENT => {
                let test: TestEventPayload = serde_json::from_value(envelope.payload.clone()).unwrap_or_default();
                info!("XingShu Bridge received TEST_EVENT on {}: event_name={}, message={}", connection_id, test.event_name, test.message);

                if test.event_name == "PING_TEST" {
                    let ack = TestEventPayload {
                        event_name: "PING_TEST_ACK".to_string(),
                        sender: "HUB".to_string(),
                        message: format!("Hub acknowledged: {}", test.message),
                        extra: None,
                    };
                    let ack_envelope = BridgeEnvelope::with_request_id(
                        message_types::TEST_EVENT,
                        envelope.request_id.clone(),
                        serde_json::to_value(&ack).unwrap(),
                    );
                    self.send_envelope_to(connection_id, &ack_envelope);
                }
            }
            message_types::WATCH_JOIN_ACCEPTED
            | message_types::WATCH_JOIN_REJECTED
            | message_types::WATCH_JOIN_TIMEOUT
            | message_types::WATCH_ALREADY_JOINED
            | message_types::WATCH_TARGET_INVALID => {
                let status = envelope.message_type.clone();
                let request_id = envelope.request_id.clone();
                let reason = envelope
                    .payload
                    .get("reason")
                    .and_then(|r| r.as_str())
                    .or_else(|| envelope.payload.get("message").and_then(|m| m.as_str()))
                    .map(|s| s.to_string());
                let target = envelope.payload.get("target").cloned();
                let resp = WatchTargetResponsePayload {
                    status: status.clone(),
                    request_id: request_id.clone(),
                    target,
                    reason,
                    timestamp: Some(now),
                };
                info!(
                    "WATCH_TARGET response received on conn={}: status={}, reqId={}",
                    connection_id, status, request_id
                );
                self.emit_watch_target_response(&resp);
            }
            "WATCH_TARGET_RESPONSE" => {
                let status = envelope
                    .payload
                    .get("status")
                    .and_then(|s| s.as_str())
                    .unwrap_or(message_types::WATCH_JOIN_ACCEPTED)
                    .to_string();
                let request_id = envelope.request_id.clone();
                let reason = envelope
                    .payload
                    .get("reason")
                    .and_then(|r| r.as_str())
                    .or_else(|| envelope.payload.get("message").and_then(|m| m.as_str()))
                    .map(|s| s.to_string());
                let target = envelope.payload.get("target").cloned();
                let resp = WatchTargetResponsePayload {
                    status: status.clone(),
                    request_id: request_id.clone(),
                    target,
                    reason,
                    timestamp: Some(now),
                };
                info!(
                    "WATCH_TARGET_RESPONSE received on conn={}: status={}, reqId={}",
                    connection_id, status, request_id
                );
                self.emit_watch_target_response(&resp);
            }
            _ => {
                info!("Unhandled message type on {}: {}", connection_id, envelope.message_type);
            }
        }
    }

    pub async fn process_incoming_message(&self, raw_json: &str) {
        let generation = Self::extract_json_u64_field(raw_json, "generation");
        self.process_incoming_message_with_generation(raw_json, generation).await;
    }

    pub async fn process_incoming_message_with_generation(&self, raw_json: &str, generation: Option<u64>) {
        let envelope: BridgeEnvelope<serde_json::Value> = match serde_json::from_str(raw_json) {
            Ok(env) => env,
            Err(e) => {
                let summary = Self::safe_frame_summary(raw_json);
                warn!(
                    "Bridge envelope JSON parse error: {}. Safe summary: [Length={}, Type={}, ReqId={}]",
                    e, summary.length, summary.message_type, summary.request_id
                );
                return;
            }
        };

        // Protocol Version check
        if envelope.protocol_version != XINGSHU_BRIDGE_PROTOCOL_VERSION {
            warn!("Rejected incoming message with unsupported protocol version: {}", envelope.protocol_version);
            return;
        }

        // Check for prohibited sensitive credentials
        for prohibited in PROHIBITED_CREDENTIAL_KEYS {
            if raw_json.contains(prohibited) {
                warn!("Security alert: Prohibited sensitive credential key '{}' detected in bridge message! Rejecting frame.", prohibited);
                return;
            }
        }

        let now = Utc::now().timestamp_millis();
        let summary = format!("Type: {}, ReqId: {}", envelope.message_type, envelope.request_id);

        {
            let mut state = self.state.lock().unwrap();
            state.total_messages_received += 1;
            let entry = BridgeLogEntry {
                id: Uuid::new_v4().to_string(),
                direction: "IN".to_string(),
                message_type: envelope.message_type.clone(),
                summary,
                raw_json: self.scrub_credentials(raw_json),
                timestamp: now,
            };
            state.recent_messages.push_back(entry);
            if state.recent_messages.len() > 100 {
                state.recent_messages.pop_front();
            }
        }

        let effective_generation = generation.or_else(|| Self::extract_json_u64_field(raw_json, "generation"));
        self.process_incoming_envelope_with_generation(&envelope, effective_generation).await;
    }

    pub async fn process_incoming_envelope(&self, envelope: &BridgeEnvelope<serde_json::Value>) {
        self.process_incoming_envelope_with_generation(envelope, None).await;
    }

    pub async fn process_incoming_envelope_with_generation(
        &self,
        envelope: &BridgeEnvelope<serde_json::Value>,
        generation: Option<u64>,
    ) {
        let now = Utc::now().timestamp_millis();

        // Enforce generation validation for post-handshake messages
        if envelope.message_type != message_types::HELLO {
            let active_gen = self.state.lock().unwrap().active_generation;
            if let Some(msg_gen) = generation {
                if active_gen != msg_gen {
                    warn!(
                        "Rejected {} message: message generation {} does not match active_generation {}",
                        envelope.message_type, msg_gen, active_gen
                    );
                    return;
                }
            }
        }

        match envelope.message_type.as_str() {
            message_types::HELLO => {
                let hello = match self.validate_hello_message(envelope, None) {
                    Ok(h) => h,
                    Err((code, msg)) => {
                        warn!("Rejected HELLO: {} - {}", code, msg);
                        return;
                    }
                };
                let challenge = self.session_challenge();

                info!(
                    "XingShu Bridge received HELLO: component={:?}, runtime_version={:?}, pid={:?}",
                    hello.component, hello.runtime_version, hello.game_pid
                );

                let session_id = Uuid::new_v4().to_string();
                let active_gen = {
                    let mut state = self.state.lock().unwrap();
                    state.next_generation += 1;
                    state.active_generation = state.next_generation;
                    state.connection_state = BridgeServerState::RuntimeAuthenticated;
                    state.runtime_connected = true;
                    if state.active_transport == ActiveTransport::Disconnected {
                        state.active_transport = ActiveTransport::LoopbackTcp;
                        state.tcp_connected = true;
                    }
                    state.session_id = Some(session_id.clone());
                    state.connected_at = Some(now);
                    state.last_heartbeat_timestamp = Some(now);
                    state.game_pid = hello.game_pid;
                    state.game_version = hello.game_version;
                    state.runtime_version = hello.runtime_version.clone();

                    // Reset spectator transport session state upon new Runtime HELLO session
                    state.spectator_active_epoch = 0;
                    state.spectator_highest_sequence = 0;
                    state.latest_spectator_snapshot = None;
                    state.active_generation
                };

                let conn_id = format!("conn_{}_{}", hello.game_pid.unwrap_or(0), now);
                let platform_id = hello.platform_id.clone().unwrap_or_else(|| "STEAM_CN".to_string());
                let role = "UNBOUND".to_string();
                let runtime_session = RuntimeSession::new(
                    conn_id.clone(),
                    platform_id.clone(),
                    hello.game_pid.unwrap_or(0) as u32,
                    "",
                    hello.player_id,
                    hello.runtime_version.clone().unwrap_or_else(|| "1.0.0".to_string()),
                    active_gen,
                    role,
                );
                self.session_manager.insert(runtime_session.clone());
                self.emit_session_changed(&runtime_session);

                // Reply with HELLO_ACK
                let ack_payload = HelloAckPayload {
                    status: "ACCEPTED".to_string(),
                    hub_version: env!("CARGO_PKG_VERSION").to_string(),
                    protocol_version: XINGSHU_BRIDGE_PROTOCOL_VERSION.to_string(),
                    heartbeat_interval_ms: 3000,
                    session_challenge: Some(challenge),
                    session_id: Some(session_id),
                    connection_id: Some(conn_id),
                    generation: Some(active_gen),
                    capabilities: Some(ALLOWED_CAPABILITIES.iter().map(|&s| s.to_string()).collect()),
                };
                let ack_envelope = BridgeEnvelope::with_request_id(
                    message_types::HELLO_ACK,
                    envelope.request_id.clone(),
                    serde_json::to_value(&ack_payload).unwrap(),
                );
                self.send_envelope(&ack_envelope);
            }
            message_types::HEARTBEAT => {
                let hb: HeartbeatPayload = serde_json::from_value(envelope.payload.clone()).unwrap_or_default();
                let latency_ms = hb.client_timestamp.map(|client_ts| (now - client_ts).max(0) as u64);

                {
                    let mut state = self.state.lock().unwrap();
                    state.last_heartbeat_timestamp = Some(now);
                    if let Some(lat) = latency_ms {
                        state.last_heartbeat_latency_ms = Some(lat);
                    }
                }

                // Reply with HEARTBEAT ack
                let reply_payload = HeartbeatPayload {
                    sequence: hb.sequence,
                    client_timestamp: hb.client_timestamp,
                    server_timestamp: Some(now),
                    echo_timestamp: hb.client_timestamp,
                };
                let reply_envelope = BridgeEnvelope::with_request_id(
                    message_types::HEARTBEAT,
                    envelope.request_id.clone(),
                    serde_json::to_value(&reply_payload).unwrap(),
                );
                self.send_envelope(&reply_envelope);
            }
            message_types::RUNTIME_IDENTITY_UPDATE => {
                let payload: RuntimeIdentityUpdatePayload = match serde_json::from_value(envelope.payload.clone()) {
                    Ok(p) => p,
                    Err(e) => {
                        warn!("Failed to parse RUNTIME_IDENTITY_UPDATE: {}", e);
                        return;
                    }
                };
                let effective_player_id = payload.player_id;
                if effective_player_id > 0 {
                    let sessions = self.session_manager.list();
                    if let Some(first_session) = sessions.first() {
                        if first_session.player_id != Some(effective_player_id) {
                            if let Some(updated) = self.session_manager.update_player_id(&first_session.connection_id, effective_player_id) {
                                self.emit_session_changed(&updated);
                            }
                        }
                    }
                }
            }
            message_types::RUNTIME_STATUS => {
                let status: RuntimeStatusPayload = serde_json::from_value(envelope.payload.clone()).unwrap_or_default();
                let mut state = self.state.lock().unwrap();
                if let Some(pid) = status.game_pid {
                    state.game_pid = Some(pid);
                }
                if let Some(gv) = status.game_version {
                    state.game_version = Some(gv);
                }
                if let Some(rv) = status.runtime_version {
                    state.runtime_version = Some(rv);
                }
                drop(state);
                if let Some(pid) = status.player_id {
                    if pid > 0 {
                        let sessions = self.session_manager.list();
                        if let Some(first_session) = sessions.first() {
                            if first_session.player_id != Some(pid) {
                                if let Some(updated) = self.session_manager.update_player_id(&first_session.connection_id, pid) {
                                    self.emit_session_changed(&updated);
                                }
                            }
                        }
                    }
                }
            }
            message_types::TEST_EVENT => {
                let test: TestEventPayload = serde_json::from_value(envelope.payload.clone()).unwrap_or_default();
                info!("XingShu Bridge received TEST_EVENT: event_name={}, message={}", test.event_name, test.message);

                if test.event_name == "PING_TEST" {
                    let ack = TestEventPayload {
                        event_name: "PING_TEST_ACK".to_string(),
                        sender: "HUB".to_string(),
                        message: format!("Hub acknowledged: {}", test.message),
                        extra: None,
                    };
                    let ack_envelope = BridgeEnvelope::with_request_id(
                        message_types::TEST_EVENT,
                        envelope.request_id.clone(),
                        serde_json::to_value(&ack).unwrap(),
                    );
                    self.send_envelope(&ack_envelope);
                }
            }
            message_types::SPECTATOR_HAND_SNAPSHOT => {
                let mut payload: SpectatorHandPayload = match serde_json::from_value(envelope.payload.clone()) {
                    Ok(p) => p,
                    Err(e) => {
                        warn!("Failed to parse SPECTATOR_HAND_SNAPSHOT payload: {}", e);
                        return;
                    }
                };

                // Validate schema_version == "1.1.0"
                if payload.schema_version != "1.1.0" {
                    warn!(
                        "Rejected SPECTATOR_HAND_SNAPSHOT with unsupported schema_version '{}' (expected '1.1.0')",
                        payload.schema_version
                    );
                    return;
                }

                if payload.connection_id.is_none() {
                    payload.connection_id = Some("conn_default".to_string());
                }
                if payload.platform_id.is_none() {
                    payload.platform_id = Some("STEAM_CN".to_string());
                }

                // STRICT IDENTITY RULE:
                // Runtime identity may ONLY come from spectator_account_player_id, NEVER from payload.player_id.
                if payload.spectator_account_player_id > 0 {
                    let sessions = self.session_manager.list();
                    if let Some(first_session) = sessions.first() {
                        if first_session.player_id.is_none() || first_session.player_id == Some(0) {
                            if let Some(updated) = self.session_manager.update_player_id(&first_session.connection_id, payload.spectator_account_player_id as i64) {
                                self.emit_session_changed(&updated);
                            }
                        }
                    }
                }

                // Enforce epoch & sequence monotonicity:
                // - Reject if epoch < active_epoch
                // - Reject if epoch == active_epoch && sequence_number <= highest_sequence
                // - If epoch > active_epoch: new session epoch, highest_sequence = sequence_number, active_epoch = epoch
                // - If epoch == active_epoch && sequence_number > highest_sequence: highest_sequence = sequence_number
                let epoch = payload.spectator_session_epoch;
                let seq = payload.sequence_number;

                {
                    let mut state = self.state.lock().unwrap();

                    // The epoch and sequence monotonicity checks must strictly apply within the current authenticated generation.
                    if let Some(msg_gen) = generation {
                        if state.active_generation != msg_gen {
                            warn!(
                                "SPECTATOR_HAND_SNAPSHOT rejected: message generation {} does not match active_generation {}",
                                msg_gen, state.active_generation
                            );
                            return;
                        }
                    }

                    if epoch < state.spectator_active_epoch {
                        warn!(
                            "SPECTATOR_HAND_SNAPSHOT rejected: stale epoch {} < active_epoch {}",
                            epoch, state.spectator_active_epoch
                        );
                        return;
                    }
                    if epoch == state.spectator_active_epoch && seq <= state.spectator_highest_sequence {
                        warn!(
                            "SPECTATOR_HAND_SNAPSHOT rejected: stale/duplicate sequence {} <= highest_sequence {} for epoch {}",
                            seq, state.spectator_highest_sequence, epoch
                        );
                        return;
                    }

                    if epoch > state.spectator_active_epoch {
                        state.spectator_active_epoch = epoch;
                        state.spectator_highest_sequence = seq;
                    } else {
                        state.spectator_highest_sequence = seq;
                    }

                    // Retain latest snapshot in BridgeServer inner state
                    state.latest_spectator_snapshot = Some(payload.clone());
                }

                info!(
                    "BRIDGE_SNAPSHOT_ACCEPTED roomId={}, epoch={}, seq={}, playerId={}, handSize={}, cardIds={:?}",
                    payload.room_id,
                    payload.spectator_session_epoch,
                    payload.sequence_number,
                    payload.player_id,
                    payload.hand_size,
                    payload.card_ids
                );

                // Broadcast/dispatch to Hub subscribers
                let _ = self.spectator_broadcast_tx.send(payload.clone());

                // HS4-P1.6R2.1: Targeted Secure Mirror Forwarding to verified Steam MAIN
                // Generic broadcast is REMOVED.
                if let Some(conn_id) = payload.connection_id.as_deref() {
                    let _ = self.send_hand_truth_mirror_to_main(conn_id, &payload);
                }
            }
            message_types::RUNTIME_MATCH_CONTEXT => {
                let mut payload: RuntimeMatchContext = match serde_json::from_value(envelope.payload.clone()) {
                    Ok(p) => p,
                    Err(e) => {
                        warn!("Failed to parse RUNTIME_MATCH_CONTEXT: {}", e);
                        return;
                    }
                };
                payload.normalize();
                let sessions = self.session_manager.list();
                if let Some(first_session) = sessions.first() {
                    let conn_id = &first_session.connection_id;
                    if let Some(pid) = payload.local_player_id {
                        if pid > 0 && first_session.player_id != Some(pid) {
                            if let Some(updated) = self.session_manager.update_player_id(conn_id, pid) {
                                self.emit_session_changed(&updated);
                            }
                        }
                    }
                    if let Some(updated) = self.session_manager.update_match_context_struct(
                        conn_id,
                        payload.clone(),
                    ) {
                        self.emit_session_changed(&updated);
                    }
                    let mut state = self.state.lock().unwrap();
                    state.match_contexts.insert(conn_id.clone(), payload);
                }
            }
            _ => {
                info!("Unhandled message type: {}", envelope.message_type);
            }
        }
    }

    /// Helper to scrub any sensitive credentials before logging
    fn scrub_credentials(&self, json_str: &str) -> String {
        let mut scrubbed = crate::security::secret_filter::SecretFilter::scrub_string(json_str);
        for key in PROHIBITED_CREDENTIAL_KEYS {
            let key_lower = key.to_lowercase();
            let patterns = [
                format!("{key_lower}="),
                format!("{key_lower}:"),
                format!("\"{}\":", key_lower),
                format!("{key_lower} "),
            ];
            for p in &patterns {
                while let Some(pos) = scrubbed.to_lowercase().find(p) {
                    let mut start = pos + p.len();
                    while start < scrubbed.len() && (scrubbed.as_bytes()[start] == b' ' || scrubbed.as_bytes()[start] == b'"') {
                        start += 1;
                    }
                    if start >= scrubbed.len() {
                        break;
                    }
                    let end = scrubbed[start..]
                        .find(|c: char| c.is_whitespace() || c == '"' || c == ',' || c == '&' || c == ';')
                        .map(|idx| start + idx)
                        .unwrap_or(scrubbed.len());
                    if start == end {
                        break;
                    }
                    scrubbed.replace_range(start..end, "[REDACTED]");
                    break;
                }
            }
        }
        scrubbed
    }

    /// Send a typed envelope to a specific connection
    pub fn send_envelope_to<T: Serialize>(&self, connection_id: &str, envelope: &BridgeEnvelope<T>) -> bool {
        let json_str = match serde_json::to_string(envelope) {
            Ok(s) => s,
            Err(e) => {
                error!("Failed to serialize BridgeEnvelope: {}", e);
                return false;
            }
        };

        let now = Utc::now().timestamp_millis();
        let line = format!("{}\n", json_str);

        {
            let mut state = self.state.lock().unwrap();
            state.total_messages_sent += 1;
            let entry = BridgeLogEntry {
                id: Uuid::new_v4().to_string(),
                direction: "OUT".to_string(),
                message_type: envelope.message_type.clone(),
                summary: format!("[{}] Type: {}, ReqId: {}", connection_id, envelope.message_type, envelope.request_id),
                raw_json: self.scrub_credentials(&json_str),
                timestamp: now,
            };
            state.recent_messages.push_back(entry);
            if state.recent_messages.len() > 100 {
                state.recent_messages.pop_front();
            }
        }

        self.connection_manager.send_to(connection_id, &line)
    }

    /// Send a typed envelope to the active client or broadcast across active connections
    pub fn send_envelope<T: Serialize>(&self, envelope: &BridgeEnvelope<T>) -> bool {
        let json_str = match serde_json::to_string(envelope) {
            Ok(s) => s,
            Err(e) => {
                error!("Failed to serialize BridgeEnvelope: {}", e);
                return false;
            }
        };

        let now = Utc::now().timestamp_millis();
        let line = format!("{}\n", json_str);

        let mut sent = false;
        {
            let mut state = self.state.lock().unwrap();
            if let Some(sender) = state.active_sender.clone() {
                state.total_messages_sent += 1;
                let entry = BridgeLogEntry {
                    id: Uuid::new_v4().to_string(),
                    direction: "OUT".to_string(),
                    message_type: envelope.message_type.clone(),
                    summary: format!("Type: {}, ReqId: {}", envelope.message_type, envelope.request_id),
                    raw_json: self.scrub_credentials(&json_str),
                    timestamp: now,
                };
                state.recent_messages.push_back(entry);
                if state.recent_messages.len() > 100 {
                    state.recent_messages.pop_front();
                }
                let _ = sender.try_send(line.clone());
                sent = true;
            }
        }

        if self.connection_manager.count() > 0 {
            self.connection_manager.broadcast(&line);
            sent = true;
        }

        sent
    }

    /// Trigger manual test event from Hub
    pub fn send_test_event(&self, message: Option<String>) -> Result<(), String> {
        let msg_text = message.unwrap_or_else(|| "Manual test event from XingShu Hub".to_string());
        let payload = TestEventPayload {
            event_name: "PING_TEST".to_string(),
            sender: "HUB".to_string(),
            message: msg_text,
            extra: None,
        };

        let envelope = BridgeEnvelope::new(message_types::TEST_EVENT, serde_json::to_value(&payload).unwrap());
        if self.send_envelope(&envelope) {
            Ok(())
        } else {
            let entry = BridgeLogEntry {
                id: Uuid::new_v4().to_string(),
                direction: "OUT".to_string(),
                message_type: message_types::TEST_EVENT.to_string(),
                summary: "PING_TEST (Pending/No client connected)".to_string(),
                raw_json: self.scrub_credentials(&serde_json::to_string(&envelope).unwrap_or_default()),
                timestamp: Utc::now().timestamp_millis(),
            };
            let mut state = self.state.lock().unwrap();
            state.recent_messages.push_back(entry);
            Err("No XingShu Runtime client currently connected to Named Pipe".to_string())
        }
    }

    /// Dispatches WATCH_TARGET message to verified TapTap spectator runtime.
    /// STRICT SECURITY INVARIANT:
    /// Hub may send WATCH_TARGET ONLY to verified TAPTAP RuntimeSession matching
    /// bound USER_SPECTATOR (default 3045259). Never route to Steam MAIN.
    pub fn configure_runtime_bindings(&self, bindings: Vec<super::session::RuntimeBinding>) -> Result<(), String> {
        self.session_manager.configure_bindings(bindings)?;
        for session in self.session_manager.list() { self.emit_session_changed(&session); }
        Ok(())
    }

    pub fn send_watch_target(
        &self,
        connection_id: &str,
        target: serde_json::Value,
        expected_spectator_player_id: u64,
    ) -> Result<String, String> {
        let session = self
            .session_manager
            .get(connection_id)
            .ok_or_else(|| format!("Runtime connection '{}' not found", connection_id))?;

        // 1. Strict Platform Invariant: MUST be TAPTAP
        if !self.session_manager.is_bound_taptap_spectator(connection_id) ||
            session.live_player_id != Some(expected_spectator_player_id as i64) {
            let err = format!(
                "SECURITY_VIOLATION: Attempted to route WATCH_TARGET to platform '{}' (conn={}). Hub may send WATCH_TARGET ONLY to verified TAPTAP RuntimeSession, NEVER to Steam MAIN!",
                session.platform_id, connection_id
            );
            error!("{}", err);
            return Err(err);
        }

        // 2. Strict Identity Invariant: If player_id is known, must match expected_spectator_player_id
        if let Some(pid) = session.player_id {
            if pid > 0 && pid as u64 != expected_spectator_player_id {
                let err = format!(
                    "SECURITY_VIOLATION: Runtime player ID {} does not match bound USER_SPECTATOR {}!",
                    pid, expected_spectator_player_id
                );
                error!("{}", err);
                return Err(err);
            }
        }

        let request_id = Uuid::new_v4().to_string();
        let now = Utc::now().timestamp_millis();
        let payload = WatchTargetPayload {
            target,
            request_id: request_id.clone(),
            timestamp: now,
            target_platform: session.platform_id.clone(),
            expected_spectator_player_id,
        };

        let envelope = BridgeEnvelope::with_request_id(
            message_types::WATCH_TARGET,
            request_id.clone(),
            serde_json::to_value(&payload).map_err(|e| e.to_string())?,
        );

        if self.send_envelope_to(connection_id, &envelope) {
            info!(
                "WATCH_TARGET dispatched to TapTap spectator connection {} (reqId={})",
                connection_id, request_id
            );
            Ok(request_id)
        } else {
            Err(format!(
                "Failed to send WATCH_TARGET to connection {}: connection channel closed or disconnected",
                connection_id
            ))
        }
    }

    /// Get current Bridge snapshot status
    pub fn get_status(&self) -> BridgeStatus {
        let state = self.state.lock().unwrap();
        BridgeStatus {
            is_running: *self.is_server_started.lock().unwrap() || state.tcp_server_started,
            named_pipe_name: state.pipe_name.clone(),
            pipe_connected: state.pipe_connected,
            runtime_connected: state.runtime_connected,
            runtime_authenticated: state.connection_state == BridgeServerState::RuntimeAuthenticated,
            connection_state: state.connection_state,
            protocol_version: XINGSHU_BRIDGE_PROTOCOL_VERSION.to_string(),
            game_pid: state.game_pid,
            game_version: state.game_version.clone(),
            runtime_version: state.runtime_version.clone(),
            last_heartbeat_timestamp: state.last_heartbeat_timestamp,
            last_heartbeat_latency_ms: state.last_heartbeat_latency_ms,
            connected_at: state.connected_at,
            total_messages_received: state.total_messages_received,
            total_messages_sent: state.total_messages_sent,
            recent_messages: state.recent_messages.iter().cloned().collect(),
            tcp_addr: state.tcp_addr.map(|a| a.to_string()),
            active_transport: state.active_transport,
            tcp_connected: state.tcp_connected,
        }
    }

    pub fn clear_logs(&self) {
        let mut state = self.state.lock().unwrap();
        state.recent_messages.clear();
    }

    fn log_sys_event(&self, message_type: &str, summary: String) {
        let mut state = self.state.lock().unwrap();
        let entry = BridgeLogEntry {
            id: Uuid::new_v4().to_string(),
            direction: "SYS".to_string(),
            message_type: message_type.to_string(),
            summary,
            raw_json: "".to_string(),
            timestamp: Utc::now().timestamp_millis(),
        };
        state.recent_messages.push_back(entry);
        if state.recent_messages.len() > 100 {
            state.recent_messages.pop_front();
        }
    }

    // ==========================================
    // Legacy Handshake Compatibility
    // ==========================================

    pub fn handle_handshake(&self, handshake: BridgeHandshake) -> HubResult<BridgeEnvelope<BridgeMessage>> {
        let session_id = Uuid::new_v4().to_string();
        info!(
            "XingShu Bridge established session {} with component {:?}",
            session_id, handshake.component
        );

        let client = ConnectedClient {
            session_id: session_id.clone(),
            component: handshake.component.clone(),
            capabilities: handshake.capabilities.clone(),
            connected_at: Utc::now(),
        };

        {
            let mut guard = self.state.lock().unwrap();
            guard.legacy_clients.push(client);
        }

        let ack = BridgeHandshakeAck {
            session_id: session_id.clone(),
            accepted_capabilities: handshake.capabilities,
            heartbeat_interval_ms: 3000,
        };

        Ok(BridgeEnvelope {
            protocol_version: XINGSHU_BRIDGE_PROTOCOL_VERSION.to_string(),
            message_type: "HandshakeAck".into(),
            request_id: session_id,
            timestamp: Utc::now().timestamp_millis(),
            payload: BridgeMessage::HandshakeAck(ack),
        })
    }

    pub fn list_connected_clients(&self) -> Vec<ConnectedClient> {
        self.state.lock().unwrap().legacy_clients.clone()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_bridge_server_initial_status() {
        let server = BridgeServer::new();
        let status = server.get_status();
        assert!(!status.runtime_connected);
        assert_eq!(status.connection_state, BridgeServerState::Disconnected);
        assert_eq!(status.protocol_version, "0.1");
        assert_eq!(status.named_pipe_name, XINGSHU_BRIDGE_NAMED_PIPE);
        assert_eq!(status.total_messages_received, 0);
        assert!(!server.session_challenge().is_empty());
    }

    #[tokio::test]
    async fn test_process_hello_message() {
        let server = BridgeServer::new();
        let expected_challenge = server.session_challenge();

        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0-rc1.1".into()),
            game_pid: Some(54321),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into()]),
            session_challenge: Some(expected_challenge),
                    ..Default::default()
        };

        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        let raw = serde_json::to_string(&env).unwrap();

        server.process_incoming_message(&raw).await;

        let status = server.get_status();
        assert!(status.runtime_connected);
        assert_eq!(status.connection_state, BridgeServerState::RuntimeAuthenticated);
        assert_eq!(status.game_pid, Some(54321));
        assert_eq!(status.game_version.as_deref(), Some("1.3.8"));
        assert_eq!(status.runtime_version.as_deref(), Some("1.0.0-rc1.1"));
        assert_eq!(status.total_messages_received, 1);
    }

    // ==========================================
    // Requirement 4: Bounded Message Framing Tests
    // ==========================================

    /// 4a: Exactly max size (64 KiB = 65536 bytes)
    #[tokio::test]
    async fn test_bounded_line_reading_exact_max_size() {
        use std::io::Cursor;
        let mut buf = vec![b'A'; MAX_MESSAGE_SIZE];
        buf.push(b'\n');
        let mut cursor = Cursor::new(buf);

        let res = read_bounded_line(&mut cursor, MAX_MESSAGE_SIZE).await;
        assert!(res.is_ok(), "Expected Ok for exactly max size (64 KiB)");
        let line = res.unwrap().expect("Should have line");
        assert_eq!(line.len(), MAX_MESSAGE_SIZE);
        assert!(line.chars().all(|c| c == 'A'));
    }

    /// 4b: Max + 1 byte (65537 bytes, rejected)
    #[tokio::test]
    async fn test_bounded_line_reading_max_plus_one_byte() {
        use std::io::Cursor;
        let mut buf = vec![b'B'; MAX_MESSAGE_SIZE + 1];
        buf.push(b'\n');
        let mut cursor = Cursor::new(buf);

        let res = read_bounded_line(&mut cursor, MAX_MESSAGE_SIZE).await;
        assert_eq!(res, Err(BoundedLineError::TooLong));
    }

    /// 4c: Huge stream without newline (rejected safely without OOM)
    #[tokio::test]
    async fn test_bounded_line_reading_huge_stream_no_newline() {
        use std::io::Cursor;
        let huge_len = 10 * 1024 * 1024; // 10 MB
        let mut cursor = Cursor::new(vec![b'C'; huge_len]);

        let res = read_bounded_line(&mut cursor, MAX_MESSAGE_SIZE).await;
        assert_eq!(res, Err(BoundedLineError::TooLong));
        // Verify cursor did not have to read the entire 10MB stream
        assert!(cursor.position() <= (MAX_MESSAGE_SIZE + 16384) as u64);
    }

    /// 4d: Invalid length / malformed frame (invalid UTF-8 bytes)
    #[tokio::test]
    async fn test_bounded_line_reading_invalid_utf8() {
        use std::io::Cursor;
        let invalid_utf8 = vec![0xFF, 0xFE, 0xFD, b'\n'];
        let mut cursor = Cursor::new(invalid_utf8);

        let res = read_bounded_line(&mut cursor, MAX_MESSAGE_SIZE).await;
        assert_eq!(res, Err(BoundedLineError::InvalidUtf8));
    }

    /// 4e: Truncated frame (EOF before newline)
    #[tokio::test]
    async fn test_bounded_line_reading_truncated_frame() {
        use std::io::Cursor;
        let truncated = b"{\"protocolVersion\":\"0.1\",\"messageType\":\"HELLO\"}";
        let mut cursor = Cursor::new(truncated.to_vec());

        let res = read_bounded_line(&mut cursor, MAX_MESSAGE_SIZE).await;
        assert_eq!(res, Err(BoundedLineError::Truncated));
    }

    #[tokio::test]
    async fn test_bounded_line_reading_clean_eof() {
        use std::io::Cursor;
        let mut cursor = Cursor::new(Vec::<u8>::new());

        let res = read_bounded_line(&mut cursor, MAX_MESSAGE_SIZE).await;
        assert_eq!(res, Ok(None));
    }

    // ==========================================
    // Requirement 2: Session Challenge Auth Tests
    // ==========================================

    #[tokio::test]
    async fn test_session_challenge_validation_success() {
        let server = BridgeServer::new();
        let expected_challenge = server.session_challenge();

        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(12345),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into()]),
            session_challenge: Some(expected_challenge),
                    ..Default::default()
        };

        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        let raw = serde_json::to_string(&env).unwrap();

        server.process_incoming_message(&raw).await;

        let status = server.get_status();
        assert!(status.runtime_connected);
        assert_eq!(status.connection_state, BridgeServerState::RuntimeAuthenticated);
    }

    #[tokio::test]
    async fn test_session_challenge_validation_failure() {
        let server = BridgeServer::new();

        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(12345),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into()]),
            session_challenge: Some("wrong-challenge-token".into()),
                    ..Default::default()
        };

        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        let raw = serde_json::to_string(&env).unwrap();

        server.process_incoming_message(&raw).await;

        let status = server.get_status();
        assert!(!status.runtime_connected);
        assert_eq!(status.connection_state, BridgeServerState::Disconnected);
    }

    // ==========================================
    // Requirement 3: Single Client Model & Stale Disconnect Tests
    // ==========================================

    #[test]
    fn test_stale_disconnect_does_not_clear_active_client() {
        let server = BridgeServer::new();
        {
            let mut state = server.state.lock().unwrap();
            state.active_generation = 2;
            state.connection_state = BridgeServerState::RuntimeAuthenticated;
            state.runtime_connected = true;
            state.pipe_connected = true;
        }

        // Stale disconnect from older client Gen=1
        server.on_client_disconnected(1);

        let status = server.get_status();
        assert!(status.runtime_connected, "Active client Gen=2 must remain connected");
        assert_eq!(status.connection_state, BridgeServerState::RuntimeAuthenticated);

        // Matching disconnect for Gen=2
        server.on_client_disconnected(2);
        let status_after = server.get_status();
        assert!(!status_after.runtime_connected, "Client Gen=2 must disconnect");
        assert_eq!(status_after.connection_state, BridgeServerState::Disconnected);
    }

    // ==========================================
    // Requirement 5: Log Redaction & Safe Frame Summary Tests
    // ==========================================

    #[test]
    fn test_safe_frame_summary_extraction() {
        let json = r#"{"protocolVersion":"0.1","messageType":"RUNTIME_STATUS","requestId":"req-abc-123","payload":{"secret":"super-secret-password"}}"#;
        let summary = BridgeServer::safe_frame_summary(json);
        assert_eq!(summary.message_type, "RUNTIME_STATUS");
        assert_eq!(summary.request_id, "req-abc-123");
        assert_eq!(summary.length, json.len());
    }

    #[test]
    fn test_secret_filter_scrubs_credentials() {
        let server = BridgeServer::new();
        let sensitive = r#"{"password":"mypassword123","steam_ticket":"secret-ticket-abc"}"#;
        let scrubbed = server.scrub_credentials(sensitive);
        assert!(!scrubbed.contains("mypassword123"));
        assert!(!scrubbed.contains("secret-ticket-abc"));
    }

    // ==========================================
    // Requirement 1 & 3: Pipe Name Squatting Protection (Fail Closed) & LOCAL_SESSION_RESTRICTED
    // ==========================================

    #[cfg(windows)]
    #[tokio::test]
    async fn test_pipe_name_squatting_fails_closed_with_pipe_name_already_owned() {
        let pipe_name = format!(r"\\.\pipe\test_squat_{}", Uuid::new_v4());

        // 1. First server / instance creates the pipe with first_pipe_instance(true)
        let _first_instance = create_named_pipe_server(&pipe_name, true)
            .expect("First pipe instance must succeed");

        // 2. Attempting to start/create a second server instance with first_pipe_instance(true) on the already owned pipe
        //    MUST fail closed with PIPE_NAME_ALREADY_OWNED and NOT fall back to standard create.
        let second_instance_res = create_named_pipe_server(&pipe_name, true);
        assert!(
            second_instance_res.is_err(),
            "Second instance with first_pipe_instance(true) must fail closed"
        );
        let err = second_instance_res.unwrap_err();
        assert_eq!(
            err,
            error_codes::PIPE_NAME_ALREADY_OWNED,
            "Error code must be PIPE_NAME_ALREADY_OWNED"
        );

        // 3. Second BridgeServer attempting to run listener on the already owned pipe MUST fail closed
        let second_server = BridgeServer::new();
        {
            let mut started = second_server.is_server_started.lock().unwrap();
            *started = true;
        }
        let run_res = second_server.clone().run_listener(pipe_name.clone()).await;
        assert_eq!(
            run_res,
            Err(error_codes::PIPE_NAME_ALREADY_OWNED.to_string()),
            "run_listener must return Err(PIPE_NAME_ALREADY_OWNED)"
        );

        // 4. Verify second server state reflects failed startup / not running
        let status = second_server.get_status();
        assert!(!status.is_running, "Server must not be marked as running");
        assert!(!status.pipe_connected, "Pipe must not be marked as connected");
        assert!(!status.runtime_connected, "Runtime must not be marked as connected");
        assert_eq!(
            status.connection_state,
            BridgeServerState::Disconnected,
            "Connection state must be Disconnected"
        );

        // 5. Verify system event logs reflect failed startup / squatting error
        let error_log = status.recent_messages.iter().find(|m| {
            m.message_type == "SERVER_ERROR" && m.summary.contains("PIPE_NAME_ALREADY_OWNED")
        });
        assert!(
            error_log.is_some(),
            "Server recent messages must contain SERVER_ERROR with PIPE_NAME_ALREADY_OWNED"
        );
    }

    #[cfg(windows)]
    #[tokio::test]
    async fn test_pipe_squatting_public_startup_path_fails_closed() {
        let pipe_name = format!(r"\\.\pipe\test_squat_pub_{}", Uuid::new_v4());

        // 1. Pre-create a pipe with the test name (simulating a rogue/squatting server)
        let _pre_created_pipe = create_named_pipe_server(&pipe_name, true)
            .expect("Pre-created pipe instance must succeed");

        // 2. Call server.start_with_pipe(pipe_name) through the public startup path
        let server = BridgeServer::new();
        server.start_with_pipe(&pipe_name);

        // 3. Wait 200ms for background listener startup failure to process
        tokio::time::sleep(tokio::time::Duration::from_millis(200)).await;

        // 4. Assert fail closed invariants on public server status
        let status = server.get_status();
        assert_eq!(status.is_running, false, "server.get_status().is_running must be false");
        assert_eq!(status.runtime_connected, false, "server.get_status().runtime_connected must be false");
        assert_eq!(
            status.connection_state,
            BridgeServerState::Disconnected,
            "server.get_status().connection_state must be BridgeServerState::Disconnected"
        );

        // 5. System events contain SERVER_ERROR or PIPE_NAME_ALREADY_OWNED
        let has_expected_event = status.recent_messages.iter().any(|m| {
            m.message_type == "SERVER_ERROR" || m.summary.contains(error_codes::PIPE_NAME_ALREADY_OWNED)
        });
        assert!(
            has_expected_event,
            "System events must contain SERVER_ERROR or PIPE_NAME_ALREADY_OWNED"
        );
    }

    // ==========================================
    // Security & Auth Gate Tests
    // ==========================================

    #[cfg(windows)]
    #[tokio::test]
    async fn test_missing_session_challenge_rejected() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        let pipe_name = format!(r"\\.\pipe\test_missing_chal_{}", Uuid::new_v4());
        let server = BridgeServer::new();
        server.start_with_pipe(&pipe_name);
        tokio::time::sleep(tokio::time::Duration::from_millis(150)).await;

        let client = tokio::net::windows::named_pipe::ClientOptions::new()
            .open(&pipe_name)
            .expect("Failed to open pipe");

        let (r, mut w) = tokio::io::split(client);
        let mut buf_r = BufReader::new(r);

        let my_pid = std::process::id();
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into()]),
            session_challenge: None, // Missing!,
            ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        let json = format!("{}\n", serde_json::to_string(&env).unwrap());
        w.write_all(json.as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut resp_line = String::new();
        buf_r.read_line(&mut resp_line).await.unwrap();
        assert!(
            resp_line.contains(error_codes::AUTH_CHALLENGE_REQUIRED),
            "Expected AUTH_CHALLENGE_REQUIRED, got: {}",
            resp_line
        );

        drop(buf_r);
        drop(w);
        tokio::time::sleep(tokio::time::Duration::from_millis(300)).await;

        let status = server.get_status();
        assert_eq!(status.connection_state, BridgeServerState::Disconnected);
        server.stop();
    }

    #[cfg(windows)]
    #[tokio::test]
    async fn test_wrong_session_challenge_rejected() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        let pipe_name = format!(r"\\.\pipe\test_wrong_chal_{}", Uuid::new_v4());
        let server = BridgeServer::new();
        server.start_with_pipe(&pipe_name);
        tokio::time::sleep(tokio::time::Duration::from_millis(150)).await;

        let client = tokio::net::windows::named_pipe::ClientOptions::new()
            .open(&pipe_name)
            .expect("Failed to open pipe");

        let (r, mut w) = tokio::io::split(client);
        let mut buf_r = BufReader::new(r);

        let my_pid = std::process::id();
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into()]),
            session_challenge: Some("wrong-session-challenge-token".into()),
                    ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        let json = format!("{}\n", serde_json::to_string(&env).unwrap());
        w.write_all(json.as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut resp_line = String::new();
        buf_r.read_line(&mut resp_line).await.unwrap();
        assert!(
            resp_line.contains(error_codes::AUTH_CHALLENGE_FAILED),
            "Expected AUTH_CHALLENGE_FAILED, got: {}",
            resp_line
        );

        drop(buf_r);
        drop(w);
        tokio::time::sleep(tokio::time::Duration::from_millis(300)).await;

        let status = server.get_status();
        assert_eq!(status.connection_state, BridgeServerState::Disconnected);
        server.stop();
    }

    #[cfg(windows)]
    #[tokio::test]
    async fn test_valid_session_challenge_accepted() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        let pipe_name = format!(r"\\.\pipe\test_valid_chal_{}", Uuid::new_v4());
        let server = BridgeServer::new();
        server.start_with_pipe(&pipe_name);
        tokio::time::sleep(tokio::time::Duration::from_millis(150)).await;

        let client = tokio::net::windows::named_pipe::ClientOptions::new()
            .open(&pipe_name)
            .expect("Failed to open pipe");

        let (r, mut w) = tokio::io::split(client);
        let mut buf_r = BufReader::new(r);

        let my_pid = std::process::id();
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into()]),
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        let json = format!("{}\n", serde_json::to_string(&env).unwrap());
        w.write_all(json.as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut resp_line = String::new();
        buf_r.read_line(&mut resp_line).await.unwrap();
        assert!(
            resp_line.contains(message_types::HELLO_ACK) && resp_line.contains("ACCEPTED"),
            "Expected HELLO_ACK ACCEPTED, got: {}",
            resp_line
        );

        tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;
        let status = server.get_status();
        assert_eq!(status.connection_state, BridgeServerState::RuntimeAuthenticated);
        assert!(status.runtime_connected);
        server.stop();
    }

    #[cfg(windows)]
    #[tokio::test]
    async fn test_pid_mismatch_rejected() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        let pipe_name = format!(r"\\.\pipe\test_pid_mismatch_{}", Uuid::new_v4());
        let server = BridgeServer::new();
        server.start_with_pipe(&pipe_name);
        tokio::time::sleep(tokio::time::Duration::from_millis(150)).await;

        let client = tokio::net::windows::named_pipe::ClientOptions::new()
            .open(&pipe_name)
            .expect("Failed to open pipe");

        let (r, mut w) = tokio::io::split(client);
        let mut buf_r = BufReader::new(r);

        let fake_pid = std::process::id() + 999999;
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(fake_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into()]),
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        let json = format!("{}\n", serde_json::to_string(&env).unwrap());
        w.write_all(json.as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut resp_line = String::new();
        buf_r.read_line(&mut resp_line).await.unwrap();
        assert!(
            resp_line.contains(error_codes::PID_MISMATCH),
            "Expected PID_MISMATCH, got: {}",
            resp_line
        );

        drop(buf_r);
        drop(w);
        tokio::time::sleep(tokio::time::Duration::from_millis(300)).await;

        let status = server.get_status();
        assert_eq!(status.connection_state, BridgeServerState::Disconnected);
        server.stop();
    }

    #[cfg(windows)]
    #[tokio::test]
    async fn test_missing_game_pid_rejected() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        let pipe_name = format!(r"\\.\pipe\test_missing_pid_{}", Uuid::new_v4());
        let server = BridgeServer::new();
        server.start_with_pipe(&pipe_name);
        tokio::time::sleep(tokio::time::Duration::from_millis(150)).await;

        let client = tokio::net::windows::named_pipe::ClientOptions::new()
            .open(&pipe_name)
            .expect("Failed to open pipe");

        let (r, mut w) = tokio::io::split(client);
        let mut buf_r = BufReader::new(r);

        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: None, // Missing!
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into()]),
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        let json = format!("{}\n", serde_json::to_string(&env).unwrap());
        w.write_all(json.as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut resp_line = String::new();
        buf_r.read_line(&mut resp_line).await.unwrap();
        assert!(
            resp_line.contains(error_codes::MISSING_GAME_PID),
            "Expected MISSING_GAME_PID, got: {}",
            resp_line
        );

        drop(buf_r);
        drop(w);
        tokio::time::sleep(tokio::time::Duration::from_millis(300)).await;

        let status = server.get_status();
        assert_eq!(status.connection_state, BridgeServerState::Disconnected);
        server.stop();
    }

    #[cfg(windows)]
    #[tokio::test]
    async fn test_empty_capabilities_rejected() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        let pipe_name = format!(r"\\.\pipe\test_empty_caps_{}", Uuid::new_v4());
        let server = BridgeServer::new();
        server.start_with_pipe(&pipe_name);
        tokio::time::sleep(tokio::time::Duration::from_millis(150)).await;

        let client = tokio::net::windows::named_pipe::ClientOptions::new()
            .open(&pipe_name)
            .expect("Failed to open pipe");

        let (r, mut w) = tokio::io::split(client);
        let mut buf_r = BufReader::new(r);

        let my_pid = std::process::id();
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec![]), // Empty!
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        let json = format!("{}\n", serde_json::to_string(&env).unwrap());
        w.write_all(json.as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut resp_line = String::new();
        buf_r.read_line(&mut resp_line).await.unwrap();
        assert!(
            resp_line.contains(error_codes::CAPABILITIES_REQUIRED),
            "Expected CAPABILITIES_REQUIRED, got: {}",
            resp_line
        );

        drop(buf_r);
        drop(w);
        tokio::time::sleep(tokio::time::Duration::from_millis(300)).await;

        let status = server.get_status();
        assert_eq!(status.connection_state, BridgeServerState::Disconnected);
        server.stop();
    }

    #[cfg(windows)]
    #[tokio::test]
    async fn test_unknown_capabilities_rejected() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        let pipe_name = format!(r"\\.\pipe\test_unknown_caps_{}", Uuid::new_v4());
        let server = BridgeServer::new();
        server.start_with_pipe(&pipe_name);
        tokio::time::sleep(tokio::time::Duration::from_millis(150)).await;

        let client = tokio::net::windows::named_pipe::ClientOptions::new()
            .open(&pipe_name)
            .expect("Failed to open pipe");

        let (r, mut w) = tokio::io::split(client);
        let mut buf_r = BufReader::new(r);

        let my_pid = std::process::id();
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "UNKNOWN_UNSUPPORTED_CAP".into()]),
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        let json = format!("{}\n", serde_json::to_string(&env).unwrap());
        w.write_all(json.as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut resp_line = String::new();
        buf_r.read_line(&mut resp_line).await.unwrap();
        assert!(
            resp_line.contains(error_codes::UNKNOWN_CAPABILITY),
            "Expected UNKNOWN_CAPABILITY, got: {}",
            resp_line
        );

        drop(buf_r);
        drop(w);
        tokio::time::sleep(tokio::time::Duration::from_millis(300)).await;

        let status = server.get_status();
        assert_eq!(status.connection_state, BridgeServerState::Disconnected);
        server.stop();
    }

    #[cfg(windows)]
    #[tokio::test]
    async fn test_valid_capabilities_accepted() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        let pipe_name = format!(r"\\.\pipe\test_valid_caps_{}", Uuid::new_v4());
        let server = BridgeServer::new();
        server.start_with_pipe(&pipe_name);
        tokio::time::sleep(tokio::time::Duration::from_millis(150)).await;

        let client = tokio::net::windows::named_pipe::ClientOptions::new()
            .open(&pipe_name)
            .expect("Failed to open pipe");

        let (r, mut w) = tokio::io::split(client);
        let mut buf_r = BufReader::new(r);

        let my_pid = std::process::id();
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into(), "RUNTIME".into(), "SPECTATOR".into(), "HOOKS".into()]),
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        let json = format!("{}\n", serde_json::to_string(&env).unwrap());
        w.write_all(json.as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut resp_line = String::new();
        buf_r.read_line(&mut resp_line).await.unwrap();
        assert!(
            resp_line.contains(message_types::HELLO_ACK) && resp_line.contains("ACCEPTED"),
            "Expected HELLO_ACK ACCEPTED, got: {}",
            resp_line
        );

        tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;
        let status = server.get_status();
        assert_eq!(status.connection_state, BridgeServerState::RuntimeAuthenticated);
        assert!(status.runtime_connected);
        server.stop();
    }

    #[cfg(windows)]
    #[tokio::test]
    async fn test_valid_game_pid_accepted() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        let pipe_name = format!(r"\\.\pipe\test_valid_pid_{}", Uuid::new_v4());
        let server = BridgeServer::new();
        server.start_with_pipe(&pipe_name);
        tokio::time::sleep(tokio::time::Duration::from_millis(150)).await;

        let client = tokio::net::windows::named_pipe::ClientOptions::new()
            .open(&pipe_name)
            .expect("Failed to open pipe");

        let (r, mut w) = tokio::io::split(client);
        let mut buf_r = BufReader::new(r);

        let my_pid = std::process::id();
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into()]),
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        let json = format!("{}\n", serde_json::to_string(&env).unwrap());
        w.write_all(json.as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut resp_line = String::new();
        buf_r.read_line(&mut resp_line).await.unwrap();
        assert!(
            resp_line.contains(message_types::HELLO_ACK) && resp_line.contains("ACCEPTED"),
            "Expected HELLO_ACK ACCEPTED, got: {}",
            resp_line
        );

        tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;
        let status = server.get_status();
        assert_eq!(status.connection_state, BridgeServerState::RuntimeAuthenticated);
        assert_eq!(status.game_pid, Some(my_pid));
        assert!(status.runtime_connected);
        server.stop();
    }

    #[cfg(windows)]
    #[tokio::test]
    async fn test_security_trust_validation_trusts_current_process() {
        use std::os::windows::io::AsRawHandle;
        let pipe_name = format!(r"\\.\pipe\test_sec_trust_{}", Uuid::new_v4());
        let server = create_named_pipe_server(&pipe_name, true)
            .expect("Create pipe server");

        let connect_fut = server.connect();
        let client_fut = async {
            tokio::net::windows::named_pipe::ClientOptions::new().open(&pipe_name)
        };

        let (_, client_res) = tokio::join!(connect_fut, client_fut);
        let _client = client_res.expect("Client connects");

        let raw_handle = server.as_raw_handle();
        let res = verify_pipe_client_security(raw_handle);
        assert!(res.is_ok(), "verify_pipe_client_security must succeed for current process: {:?}", res);
        assert_eq!(res.unwrap(), std::process::id());
    }

    #[test]
    fn test_bridge_status_does_not_serialize_session_challenge() {
        let server = BridgeServer::new();
        let status = server.get_status();
        let json = serde_json::to_string(&status).expect("Serialize BridgeStatus");

        assert!(
            !json.contains("sessionChallenge"),
            "BridgeStatus JSON serialization must NOT contain sessionChallenge"
        );
        assert!(
            !json.contains(&server.session_challenge()),
            "BridgeStatus JSON serialization must NOT leak session_challenge secret value"
        );
    }

    // ==========================================
    // HS3 Spectator Hand Snapshot Tests
    // ==========================================

    fn make_test_snapshot(epoch: u64, seq: u64, schema_version: &str) -> SpectatorHandPayload {
        use crate::xing_shu::bridge::protocol::{CardCountEntry, PlayerHandSummary};
        let mut room_players = std::collections::HashMap::new();
        room_players.insert(
            "2081758".to_string(),
            PlayerHandSummary {
                player_id: 2081758,
                hand_size: 2,
                card_ids: vec![10005, 20002],
                cards: vec![
                    CardCountEntry { card_id: 10005, count: 1 },
                    CardCountEntry { card_id: 20002, count: 1 },
                ],
                draw_count_total: 4,
                play_count_total: 2,
            },
        );

        SpectatorHandPayload {
            schema_version: schema_version.to_string(),
            room_id: 7777,
            spectator_session_epoch: epoch,
            spectator_account_player_id: 1668707,
            sequence_number: seq,
            snapshot_timestamp: "2026-10-16T15:35:12.450Z".to_string(),
            player_id: 2081758,
            card_ids: vec![10005, 20002],
            cards: vec![
                CardCountEntry { card_id: 10005, count: 1 },
                CardCountEntry { card_id: 20002, count: 1 },
            ],
            hand_size: 2,
            draw_count_total: 4,
            play_count_total: 2,
            room_players,
            ..Default::default()
        }
    }

    #[tokio::test]
    async fn test_spectator_hand_snapshot_ingestion_and_state() {
        let server = BridgeServer::new();
        assert!(server.get_latest_spectator_snapshot().is_none());

        let snapshot = make_test_snapshot(1, 1, "1.1.0");
        let env = BridgeEnvelope::new(
            message_types::SPECTATOR_HAND_SNAPSHOT,
            serde_json::to_value(&snapshot).unwrap(),
        );

        server.process_incoming_envelope(&env).await;

        let latest = server.get_latest_spectator_snapshot();
        assert!(latest.is_some());
        let s = latest.unwrap();
        assert_eq!(s.schema_version, "1.1.0");
        assert_eq!(s.spectator_session_epoch, 1);
        assert_eq!(s.sequence_number, 1);
        assert_eq!(s.room_id, 7777);
        assert_eq!(s.player_id, 2081758);
        assert_eq!(s.cards.len(), 2);
    }

    #[tokio::test]
    async fn test_spectator_hand_snapshot_broadcast_channel() {
        let server = BridgeServer::new();
        let mut rx = server.subscribe_spectator_snapshots();

        let snapshot = make_test_snapshot(1, 1, "1.1.0");
        let env = BridgeEnvelope::new(
            message_types::SPECTATOR_HAND_SNAPSHOT,
            serde_json::to_value(&snapshot).unwrap(),
        );

        server.process_incoming_envelope(&env).await;

        let received = rx.recv().await.expect("Expected snapshot broadcast");
        assert_eq!(received.schema_version, "1.1.0");
        assert_eq!(received.spectator_session_epoch, 1);
        assert_eq!(received.sequence_number, 1);
        assert_eq!(received.room_id, 7777);
    }

    #[tokio::test]
    async fn test_spectator_hand_snapshot_invalid_schema_version_rejected() {
        let server = BridgeServer::new();
        let mut rx = server.subscribe_spectator_snapshots();

        let snapshot = make_test_snapshot(1, 1, "1.0.0"); // Invalid schemaVersion!
        let env = BridgeEnvelope::new(
            message_types::SPECTATOR_HAND_SNAPSHOT,
            serde_json::to_value(&snapshot).unwrap(),
        );

        server.process_incoming_envelope(&env).await;

        assert!(server.get_latest_spectator_snapshot().is_none());
        assert!(rx.try_recv().is_err());
    }

    #[tokio::test]
    async fn test_spectator_hand_snapshot_epoch_and_sequence_monotonicity() {
        let server = BridgeServer::new();
        let mut rx = server.subscribe_spectator_snapshots();

        // 1. Initial snapshot (epoch 1, seq 1) -> Accepted
        let snap1 = make_test_snapshot(1, 1, "1.1.0");
        server.process_incoming_envelope(&BridgeEnvelope::new(
            message_types::SPECTATOR_HAND_SNAPSHOT,
            serde_json::to_value(&snap1).unwrap(),
        )).await;
        assert_eq!(server.get_latest_spectator_snapshot().unwrap().sequence_number, 1);
        assert_eq!(rx.recv().await.unwrap().sequence_number, 1);

        // 2. Monotonic increase in same epoch (epoch 1, seq 2) -> Accepted
        let snap2 = make_test_snapshot(1, 2, "1.1.0");
        server.process_incoming_envelope(&BridgeEnvelope::new(
            message_types::SPECTATOR_HAND_SNAPSHOT,
            serde_json::to_value(&snap2).unwrap(),
        )).await;
        assert_eq!(server.get_latest_spectator_snapshot().unwrap().sequence_number, 2);
        assert_eq!(rx.recv().await.unwrap().sequence_number, 2);

        // 3. Duplicate sequence (epoch 1, seq 2) -> Rejected
        let snap2_dup = make_test_snapshot(1, 2, "1.1.0");
        server.process_incoming_envelope(&BridgeEnvelope::new(
            message_types::SPECTATOR_HAND_SNAPSHOT,
            serde_json::to_value(&snap2_dup).unwrap(),
        )).await;
        assert_eq!(server.get_latest_spectator_snapshot().unwrap().sequence_number, 2);
        assert!(rx.try_recv().is_err(), "Duplicate sequence must not broadcast");

        // 4. Stale/reordered sequence (epoch 1, seq 1) -> Rejected
        let snap1_stale = make_test_snapshot(1, 1, "1.1.0");
        server.process_incoming_envelope(&BridgeEnvelope::new(
            message_types::SPECTATOR_HAND_SNAPSHOT,
            serde_json::to_value(&snap1_stale).unwrap(),
        )).await;
        assert_eq!(server.get_latest_spectator_snapshot().unwrap().sequence_number, 2);
        assert!(rx.try_recv().is_err(), "Stale sequence must not broadcast");

        // 5. Advance to new epoch (epoch 2, seq 1) -> Accepted (resets sequence tracking)
        let snap3 = make_test_snapshot(2, 1, "1.1.0");
        server.process_incoming_envelope(&BridgeEnvelope::new(
            message_types::SPECTATOR_HAND_SNAPSHOT,
            serde_json::to_value(&snap3).unwrap(),
        )).await;
        let latest = server.get_latest_spectator_snapshot().unwrap();
        assert_eq!(latest.spectator_session_epoch, 2);
        assert_eq!(latest.sequence_number, 1);
        let rec = rx.recv().await.unwrap();
        assert_eq!(rec.spectator_session_epoch, 2);
        assert_eq!(rec.sequence_number, 1);

        // 6. Monotonic increase in new epoch (epoch 2, seq 5) -> Accepted
        let snap4 = make_test_snapshot(2, 5, "1.1.0");
        server.process_incoming_envelope(&BridgeEnvelope::new(
            message_types::SPECTATOR_HAND_SNAPSHOT,
            serde_json::to_value(&snap4).unwrap(),
        )).await;
        assert_eq!(server.get_latest_spectator_snapshot().unwrap().sequence_number, 5);
        assert_eq!(rx.recv().await.unwrap().sequence_number, 5);

        // 7. Stale epoch (epoch 1, seq 999) -> Rejected
        let snap_old_epoch = make_test_snapshot(1, 999, "1.1.0");
        server.process_incoming_envelope(&BridgeEnvelope::new(
            message_types::SPECTATOR_HAND_SNAPSHOT,
            serde_json::to_value(&snap_old_epoch).unwrap(),
        )).await;
        assert_eq!(server.get_latest_spectator_snapshot().unwrap().spectator_session_epoch, 2);
        assert_eq!(server.get_latest_spectator_snapshot().unwrap().sequence_number, 5);
        assert!(rx.try_recv().is_err(), "Stale epoch must not broadcast");

        // 8. Stale sequence in current epoch (epoch 2, seq 4) -> Rejected
        let snap_stale_seq = make_test_snapshot(2, 4, "1.1.0");
        server.process_incoming_envelope(&BridgeEnvelope::new(
            message_types::SPECTATOR_HAND_SNAPSHOT,
            serde_json::to_value(&snap_stale_seq).unwrap(),
        )).await;
        assert_eq!(server.get_latest_spectator_snapshot().unwrap().sequence_number, 5);
        assert!(rx.try_recv().is_err(), "Stale sequence must not broadcast");
    }

    #[tokio::test]
    async fn test_spectator_hand_snapshot_via_process_incoming_message() {
        let server = BridgeServer::new();
        let mut rx = server.subscribe_spectator_snapshots();

        let snapshot = make_test_snapshot(1, 1, "1.1.0");
        let env = BridgeEnvelope::new(
            message_types::SPECTATOR_HAND_SNAPSHOT,
            serde_json::to_value(&snapshot).unwrap(),
        );
        let raw_json = serde_json::to_string(&env).unwrap();

        server.process_incoming_message(&raw_json).await;

        let latest = server.get_latest_spectator_snapshot();
        assert!(latest.is_some());
        assert_eq!(latest.unwrap().sequence_number, 1);

        let rec = rx.recv().await.expect("Broadcast must succeed via process_incoming_message");
        assert_eq!(rec.sequence_number, 1);
    }

    #[cfg(windows)]
    #[tokio::test]
    async fn test_runtime_process_restart_resets_spectator_epoch_and_sequence() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        let pipe_name = format!(r"\\.\pipe\test_restart_gen_{}", Uuid::new_v4());
        let server = BridgeServer::new();
        server.start_with_pipe(&pipe_name);
        tokio::time::sleep(tokio::time::Duration::from_millis(150)).await;

        let my_pid = std::process::id();

        // 1. Client 1 (Gen 1) connects and HELLO succeeds
        let client1 = tokio::net::windows::named_pipe::ClientOptions::new()
            .open(&pipe_name)
            .expect("Client 1 connects to pipe");
        let (r1, mut w1) = tokio::io::split(client1);
        let mut buf_r1 = BufReader::new(r1);

        let hello1 = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into(), "SPECTATOR".into()]),
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let hello_env1 = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello1).unwrap());
        w1.write_all(format!("{}\n", serde_json::to_string(&hello_env1).unwrap()).as_bytes()).await.unwrap();
        w1.flush().await.unwrap();

        let mut ack1 = String::new();
        buf_r1.read_line(&mut ack1).await.unwrap();
        assert!(
            ack1.contains(message_types::HELLO_ACK) && ack1.contains("ACCEPTED"),
            "Expected HELLO_ACK ACCEPTED for Client 1, got: {}",
            ack1
        );

        tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;
        assert_eq!(server.active_generation(), 1);
        assert_eq!(server.connection_state(), BridgeServerState::RuntimeAuthenticated);

        // 2. Gen 1 sends epoch=3, sequence=20 -> ACCEPTED. Latest snapshot epoch=3, seq=20.
        let snap1 = make_test_snapshot(3, 20, "1.1.0");
        let snap_env1 = BridgeEnvelope::new(message_types::SPECTATOR_HAND_SNAPSHOT, serde_json::to_value(&snap1).unwrap());
        w1.write_all(format!("{}\n", serde_json::to_string(&snap_env1).unwrap()).as_bytes()).await.unwrap();
        w1.flush().await.unwrap();

        tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;
        let s1 = server.get_latest_spectator_snapshot().expect("Expected snapshot from Gen 1");
        assert_eq!(s1.spectator_session_epoch, 3);
        assert_eq!(s1.sequence_number, 20);
        assert_eq!(server.get_spectator_epoch_and_sequence(), (3, 20));

        // 3. Client 1 disconnects (or pipe drops)
        drop(buf_r1);
        drop(w1);
        tokio::time::sleep(tokio::time::Duration::from_millis(150)).await;
        assert_eq!(server.connection_state(), BridgeServerState::Disconnected);

        // 4. Client 2 (Gen 2) connects and HELLO succeeds -> creates new generation
        let client2 = tokio::net::windows::named_pipe::ClientOptions::new()
            .open(&pipe_name)
            .expect("Client 2 connects to pipe");
        let (r2, mut w2) = tokio::io::split(client2);
        let mut buf_r2 = BufReader::new(r2);

        let hello2 = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into(), "SPECTATOR".into()]),
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let hello_env2 = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello2).unwrap());
        w2.write_all(format!("{}\n", serde_json::to_string(&hello_env2).unwrap()).as_bytes()).await.unwrap();
        w2.flush().await.unwrap();

        let mut ack2 = String::new();
        buf_r2.read_line(&mut ack2).await.unwrap();
        assert!(
            ack2.contains(message_types::HELLO_ACK) && ack2.contains("ACCEPTED"),
            "Expected HELLO_ACK ACCEPTED for Client 2, got: {}",
            ack2
        );

        tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;
        assert_eq!(server.active_generation(), 2);
        assert_eq!(server.connection_state(), BridgeServerState::RuntimeAuthenticated);

        // Verify spectator transport state was reset upon Gen 2 HELLO
        assert!(server.get_latest_spectator_snapshot().is_none());
        assert_eq!(server.get_spectator_epoch_and_sequence(), (0, 0));

        // 5. Gen 2 sends epoch=1, sequence=1 -> MUST BE ACCEPTED (not rejected as stale 1 < 3)!
        let snap2 = make_test_snapshot(1, 1, "1.1.0");
        let snap_env2 = BridgeEnvelope::new(message_types::SPECTATOR_HAND_SNAPSHOT, serde_json::to_value(&snap2).unwrap());
        w2.write_all(format!("{}\n", serde_json::to_string(&snap_env2).unwrap()).as_bytes()).await.unwrap();
        w2.flush().await.unwrap();

        tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;

        // 6. Verify latest snapshot in Hub is now epoch=1, seq=1
        let s2 = server.get_latest_spectator_snapshot().expect("Expected snapshot from Gen 2");
        assert_eq!(s2.spectator_session_epoch, 1);
        assert_eq!(s2.sequence_number, 1);
        assert_eq!(server.get_spectator_epoch_and_sequence(), (1, 1));

        // 7. Verify old Gen 1 packet cannot mutate Gen 2 state
        // Attempt sending old Gen 1 packet via process_incoming_envelope_with_generation
        let old_gen1_snap = make_test_snapshot(3, 21, "1.1.0");
        let old_gen1_env = BridgeEnvelope::new(message_types::SPECTATOR_HAND_SNAPSHOT, serde_json::to_value(&old_gen1_snap).unwrap());
        server.process_incoming_envelope_with_generation(&old_gen1_env, Some(1)).await;

        // Also test sending old Gen 1 packet via raw JSON with generation field
        let old_gen1_json = format!(
            "{{\"protocolVersion\":\"0.1\",\"messageType\":\"SPECTATOR_HAND_SNAPSHOT\",\"generation\":1,\"payload\":{}}}\n",
            serde_json::to_string(&make_test_snapshot(1, 999, "1.1.0")).unwrap()
        );
        server.process_incoming_message(&old_gen1_json).await;

        // Verify snapshot in Hub is STILL epoch=1, seq=1 from Gen 2
        let s_final = server.get_latest_spectator_snapshot().expect("Snapshot must remain from Gen 2");
        assert_eq!(s_final.spectator_session_epoch, 1);
        assert_eq!(s_final.sequence_number, 1);
        assert_eq!(server.get_spectator_epoch_and_sequence(), (1, 1));

        // Also verify stale/unauthenticated disconnect does not disrupt active Gen 2
        server.on_client_disconnected(1);
        assert_eq!(server.active_generation(), 2);
        assert_eq!(server.connection_state(), BridgeServerState::RuntimeAuthenticated);

        server.on_client_disconnected(0);
        assert_eq!(server.active_generation(), 2);
        assert_eq!(server.connection_state(), BridgeServerState::RuntimeAuthenticated);

        drop(buf_r2);
        drop(w2);
        server.stop();
    }

    #[cfg(not(windows))]
    #[tokio::test]
    async fn test_runtime_process_restart_resets_spectator_epoch_and_sequence() {
        let server = BridgeServer::new();
        let expected_challenge = server.session_challenge();

        // 1. Client 1 (Gen 1) connects and HELLO succeeds
        let hello1 = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(12345),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into(), "SPECTATOR".into()]),
            session_challenge: Some(expected_challenge.clone()),
                    ..Default::default()
        };
        let hello_env1 = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello1).unwrap());
        server.process_incoming_envelope(&hello_env1).await;
        assert_eq!(server.active_generation(), 1);

        // 2. Gen 1 sends epoch=3, sequence=20 -> ACCEPTED
        let snap1 = make_test_snapshot(3, 20, "1.1.0");
        let snap_env1 = BridgeEnvelope::new(message_types::SPECTATOR_HAND_SNAPSHOT, serde_json::to_value(&snap1).unwrap());
        server.process_incoming_envelope_with_generation(&snap_env1, Some(1)).await;
        let s1 = server.get_latest_spectator_snapshot().expect("Expected snapshot from Gen 1");
        assert_eq!(s1.spectator_session_epoch, 3);
        assert_eq!(s1.sequence_number, 20);

        // 3. Client 1 disconnects
        server.on_client_disconnected(1);
        assert_eq!(server.connection_state(), BridgeServerState::Disconnected);

        // 4. Client 2 (Gen 2) connects and HELLO succeeds
        let hello2 = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(12345),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into(), "SPECTATOR".into()]),
            session_challenge: Some(expected_challenge),
                    ..Default::default()
        };
        let hello_env2 = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello2).unwrap());
        server.process_incoming_envelope(&hello_env2).await;
        assert_eq!(server.active_generation(), 2);
        assert!(server.get_latest_spectator_snapshot().is_none());
        assert_eq!(server.get_spectator_epoch_and_sequence(), (0, 0));

        // 5. Gen 2 sends epoch=1, sequence=1 -> MUST BE ACCEPTED
        let snap2 = make_test_snapshot(1, 1, "1.1.0");
        let snap_env2 = BridgeEnvelope::new(message_types::SPECTATOR_HAND_SNAPSHOT, serde_json::to_value(&snap2).unwrap());
        server.process_incoming_envelope_with_generation(&snap_env2, Some(2)).await;

        // 6. Verify latest snapshot in Hub is now epoch=1, seq=1
        let s2 = server.get_latest_spectator_snapshot().expect("Expected snapshot from Gen 2");
        assert_eq!(s2.spectator_session_epoch, 1);
        assert_eq!(s2.sequence_number, 1);

        // 7. Verify old Gen 1 packet cannot mutate Gen 2 state
        let old_gen1_snap = make_test_snapshot(3, 21, "1.1.0");
        let old_gen1_env = BridgeEnvelope::new(message_types::SPECTATOR_HAND_SNAPSHOT, serde_json::to_value(&old_gen1_snap).unwrap());
        server.process_incoming_envelope_with_generation(&old_gen1_env, Some(1)).await;

        let s_final = server.get_latest_spectator_snapshot().expect("Snapshot must remain from Gen 2");
        assert_eq!(s_final.spectator_session_epoch, 1);
        assert_eq!(s_final.sequence_number, 1);
    }

    #[tokio::test]
    async fn test_tcp_prohibits_0_0_0_0() {
        let server = BridgeServer::new();
        let res = server.start_tcp("0.0.0.0:25890").await;
        assert!(res.is_err(), "Binding to 0.0.0.0 must fail closed");
        let err = res.err().unwrap();
        assert!(
            err.contains("strictly prohibited") || err.contains("non-127.0.0.1"),
            "Expected prohibition error, got: {}",
            err
        );

        let res_ext = server.start_tcp("192.168.1.100:25890").await;
        assert!(res_ext.is_err(), "Binding to external IP must fail closed");

        let res_ipv6 = server.start_tcp("[::1]:25890").await;
        assert!(res_ipv6.is_err(), "Binding to IPv6 must fail closed");
    }

    #[tokio::test]
    async fn test_tcp_port_squatting_fails_closed() {
        let server1 = BridgeServer::new();
        let addr = server1.start_tcp("127.0.0.1:0").await.expect("Server 1 binds");

        let server2 = BridgeServer::new();
        let res = server2.start_tcp(&addr.to_string()).await;
        assert!(res.is_err(), "Binding to already bound port must fail closed");
        let err = res.err().unwrap();
        assert!(
            err.contains("squatting") || err.contains("failed to bind") || err.contains("already in use"),
            "Expected port squatting error, got: {}",
            err
        );

        server1.stop();
    }

    #[test]
    fn test_validate_session_challenge_unit() {
        let server = BridgeServer::new();
        let challenge = server.session_challenge();

        assert!(server.validate_session_challenge(Some(&challenge)).is_ok());
        assert_eq!(
            server.validate_session_challenge(None),
            Err(error_codes::AUTH_CHALLENGE_REQUIRED)
        );
        assert_eq!(
            server.validate_session_challenge(Some("wrong-token")),
            Err(error_codes::AUTH_CHALLENGE_FAILED)
        );
    }

    #[test]
    fn test_validate_hello_message_unit() {
        let server = BridgeServer::new();
        let my_pid = std::process::id();
        let challenge = server.session_challenge();

        // Valid
        let valid_hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into(), "spectator_hand_snapshot".into()]),
            session_challenge: Some(challenge.clone()),
                    ..Default::default()
        };
        let valid_env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&valid_hello).unwrap());
        assert!(server.validate_hello_message(&valid_env, Some(my_pid)).is_ok());

        // Unsupported protocol
        let mut bad_ver_env = valid_env.clone();
        bad_ver_env.protocol_version = "9.9".into();
        let err = server.validate_hello_message(&bad_ver_env, Some(my_pid)).unwrap_err();
        assert_eq!(err.0, error_codes::UNSUPPORTED_PROTOCOL);

        // Not HELLO
        let mut not_hello_env = valid_env.clone();
        not_hello_env.message_type = "HEARTBEAT".into();
        let err = server.validate_hello_message(&not_hello_env, Some(my_pid)).unwrap_err();
        assert_eq!(err.0, error_codes::PROTOCOL_GATE_VIOLATION);

        // Invalid component
        let mut bad_comp = valid_hello.clone();
        bad_comp.component = Some("InvalidComponent".into());
        let bad_comp_env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&bad_comp).unwrap());
        let err = server.validate_hello_message(&bad_comp_env, Some(my_pid)).unwrap_err();
        assert_eq!(err.0, error_codes::INVALID_COMPONENT);

        // Missing challenge
        let mut no_ch = valid_hello.clone();
        no_ch.session_challenge = None;
        let no_ch_env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&no_ch).unwrap());
        let err = server.validate_hello_message(&no_ch_env, Some(my_pid)).unwrap_err();
        assert_eq!(err.0, error_codes::AUTH_CHALLENGE_REQUIRED);

        // Wrong challenge
        let mut wrong_ch = valid_hello.clone();
        wrong_ch.session_challenge = Some("wrong-uuid".into());
        let wrong_ch_env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&wrong_ch).unwrap());
        let err = server.validate_hello_message(&wrong_ch_env, Some(my_pid)).unwrap_err();
        assert_eq!(err.0, error_codes::AUTH_CHALLENGE_FAILED);

        // Missing PID
        let mut no_pid = valid_hello.clone();
        no_pid.game_pid = None;
        let no_pid_env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&no_pid).unwrap());
        let err = server.validate_hello_message(&no_pid_env, Some(my_pid)).unwrap_err();
        assert_eq!(err.0, error_codes::MISSING_GAME_PID);

        // PID mismatch
        let mut mismatch_pid = valid_hello.clone();
        mismatch_pid.game_pid = Some(999999);
        let mismatch_pid_env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&mismatch_pid).unwrap());
        let err = server.validate_hello_message(&mismatch_pid_env, Some(my_pid)).unwrap_err();
        assert_eq!(err.0, error_codes::PID_MISMATCH);

        // Missing capabilities
        let mut no_caps = valid_hello.clone();
        no_caps.capabilities = None;
        let no_caps_env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&no_caps).unwrap());
        let err = server.validate_hello_message(&no_caps_env, Some(my_pid)).unwrap_err();
        assert_eq!(err.0, error_codes::CAPABILITIES_REQUIRED);

        // Unknown capability
        let mut unk_cap = valid_hello.clone();
        unk_cap.capabilities = Some(vec!["status".into(), "MALICIOUS_HOOK".into()]);
        let unk_cap_env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&unk_cap).unwrap());
        let err = server.validate_hello_message(&unk_cap_env, Some(my_pid)).unwrap_err();
        assert_eq!(err.0, error_codes::UNKNOWN_CAPABILITY);
    }

    #[test]
    fn test_validate_hello_ack_unit() {
        let ack = HelloAckPayload {
            status: "ACCEPTED".into(),
            hub_version: "0.1.0".into(),
            protocol_version: "0.1".into(),
            heartbeat_interval_ms: 3000,
            session_challenge: Some("ch-123".into()),
            session_id: Some("sess-456".into()),
            generation: Some(1),
            capabilities: Some(vec!["spectator_hand_snapshot".into()]),
            ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO_ACK, serde_json::to_value(&ack).unwrap());

        let res = BridgeServer::validate_hello_ack(&env, Some("ch-123"));
        assert!(res.is_ok());
        let parsed = res.unwrap();
        assert_eq!(parsed.status, "ACCEPTED");
        assert_eq!(parsed.generation, Some(1));

        // Wrong challenge
        assert!(BridgeServer::validate_hello_ack(&env, Some("ch-999")).is_err());

        // Wrong message type
        let mut wrong_type = env.clone();
        wrong_type.message_type = "HELLO".into();
        assert!(BridgeServer::validate_hello_ack(&wrong_type, Some("ch-123")).is_err());
    }

    #[tokio::test]
    async fn test_production_policy_rejects_test_runner() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        use tokio::net::TcpStream;

        let server = BridgeServer::new(); // Default production policy: allow_test_runners = false
        let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        let client = TcpStream::connect(local_addr).await.expect("Connect to TCP");
        let (r, mut w) = client.into_split();
        let mut buf_r = BufReader::new(r);

        let my_pid = std::process::id();
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into(), "spectator_hand_snapshot".into()]),
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        w.write_all(format!("{}\n", serde_json::to_string(&env).unwrap()).as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut line = String::new();
        buf_r.read_line(&mut line).await.unwrap();
        assert!(
            line.contains(error_codes::SECURITY_TRUST_VIOLATION),
            "Expected SECURITY_TRUST_VIOLATION under production policy, got: {}",
            line
        );
        tokio::time::sleep(tokio::time::Duration::from_millis(250)).await;
        assert_eq!(server.connection_state(), BridgeServerState::Disconnected);
        assert_eq!(server.active_generation(), 0);

        server.stop();
    }

    #[tokio::test]
    async fn test_production_tcp_hello_handshake() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        use tokio::net::TcpStream;

        let server = BridgeServer::new_test();
        let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        let client = TcpStream::connect(local_addr).await.expect("Connect to TCP");
        let (r, mut w) = client.into_split();
        let mut buf_r = BufReader::new(r);

        let my_pid = std::process::id();
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into(), "spectator_hand_snapshot".into()]),
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        w.write_all(format!("{}\n", serde_json::to_string(&env).unwrap()).as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut line = String::new();
        buf_r.read_line(&mut line).await.unwrap();
        assert!(line.contains(message_types::HELLO_ACK) && line.contains("ACCEPTED"), "Got: {}", line);

        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;
        assert_eq!(server.active_generation(), 1);
        assert_eq!(server.connection_state(), BridgeServerState::RuntimeAuthenticated);

        let ack_env: BridgeEnvelope<serde_json::Value> = serde_json::from_str(line.trim()).unwrap();
        let ack = BridgeServer::validate_hello_ack(&ack_env, Some(&server.session_challenge())).unwrap();
        assert_eq!(ack.status, "ACCEPTED");
        assert_eq!(ack.protocol_version, "0.1");
        assert_eq!(ack.generation, Some(1));

        server.stop();
    }

    #[tokio::test]
    async fn test_production_tcp_generation_reset() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        use tokio::net::TcpStream;

        let server = BridgeServer::new_test();
        let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        let my_pid = std::process::id();

        // 1. Client 1 connects (Gen 1)
        let client1 = TcpStream::connect(local_addr).await.unwrap();
        let (r1, mut w1) = client1.into_split();
        let mut buf_r1 = BufReader::new(r1);

        let hello1 = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into(), "spectator_hand_snapshot".into()]),
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let env1 = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello1).unwrap());
        w1.write_all(format!("{}\n", serde_json::to_string(&env1).unwrap()).as_bytes()).await.unwrap();
        w1.flush().await.unwrap();

        let mut ack1 = String::new();
        buf_r1.read_line(&mut ack1).await.unwrap();
        assert!(ack1.contains("ACCEPTED"));

        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;
        assert_eq!(server.active_generation(), 1);

        // Gen 1 sends spectator snapshot: epoch 3, seq 20
        let snap1 = make_test_snapshot(3, 20, "1.1.0");
        let snap_env1 = BridgeEnvelope::new(message_types::SPECTATOR_HAND_SNAPSHOT, serde_json::to_value(&snap1).unwrap());
        w1.write_all(format!("{}\n", serde_json::to_string(&snap_env1).unwrap()).as_bytes()).await.unwrap();
        w1.flush().await.unwrap();

        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;
        assert_eq!(server.get_spectator_epoch_and_sequence(), (3, 20));

        // Client 1 disconnects
        drop(buf_r1);
        drop(w1);
        tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;
        assert_eq!(server.connection_state(), BridgeServerState::Disconnected);

        // 2. Client 2 connects (Gen 2)
        let client2 = TcpStream::connect(local_addr).await.unwrap();
        let (r2, mut w2) = client2.into_split();
        let mut buf_r2 = BufReader::new(r2);

        let hello2 = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into(), "spectator_hand_snapshot".into()]),
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let env2 = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello2).unwrap());
        w2.write_all(format!("{}\n", serde_json::to_string(&env2).unwrap()).as_bytes()).await.unwrap();
        w2.flush().await.unwrap();

        let mut ack2 = String::new();
        buf_r2.read_line(&mut ack2).await.unwrap();
        assert!(ack2.contains("ACCEPTED"));

        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;
        assert_eq!(server.active_generation(), 2);
        // Epoch and sequence reset upon Gen 2
        assert_eq!(server.get_spectator_epoch_and_sequence(), (0, 0));
        assert!(server.get_latest_spectator_snapshot().is_none());

        // Gen 2 sends epoch 1, seq 1 -> Accepted!
        let snap2 = make_test_snapshot(1, 1, "1.1.0");
        let snap_env2 = BridgeEnvelope::new(message_types::SPECTATOR_HAND_SNAPSHOT, serde_json::to_value(&snap2).unwrap());
        w2.write_all(format!("{}\n", serde_json::to_string(&snap_env2).unwrap()).as_bytes()).await.unwrap();
        w2.flush().await.unwrap();

        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;
        assert_eq!(server.get_spectator_epoch_and_sequence(), (1, 1));
        assert_eq!(server.get_latest_spectator_snapshot().unwrap().sequence_number, 1);

        drop(buf_r2);
        drop(w2);
        server.stop();
    }

    #[tokio::test]
    async fn test_production_tcp_invalid_protocol_version_rejected() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        use tokio::net::TcpStream;

        let server = BridgeServer::new_test();
        let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        let client = TcpStream::connect(local_addr).await.expect("Connect to TCP");
        let (r, mut w) = client.into_split();
        let mut buf_r = BufReader::new(r);

        let my_pid = std::process::id();
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into()]),
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let mut env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        env.protocol_version = "9.9".into(); // Invalid protocol version!

        w.write_all(format!("{}\n", serde_json::to_string(&env).unwrap()).as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut line = String::new();
        buf_r.read_line(&mut line).await.unwrap();
        assert!(line.contains(error_codes::UNSUPPORTED_PROTOCOL), "Expected UNSUPPORTED_PROTOCOL, got: {}", line);

        server.stop();
    }

    #[tokio::test]
    async fn test_production_tcp_bad_challenge_rejected() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        use tokio::net::TcpStream;

        let server = BridgeServer::new_test();
        let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        let client = TcpStream::connect(local_addr).await.expect("Connect to TCP");
        let (r, mut w) = client.into_split();
        let mut buf_r = BufReader::new(r);

        let my_pid = std::process::id();
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into()]),
            session_challenge: Some("forged-challenge-token".into()), // Bad challenge!,
            ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());

        w.write_all(format!("{}\n", serde_json::to_string(&env).unwrap()).as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut line = String::new();
        buf_r.read_line(&mut line).await.unwrap();
        assert!(line.contains(error_codes::AUTH_CHALLENGE_FAILED), "Expected AUTH_CHALLENGE_FAILED, got: {}", line);

        server.stop();
    }

    #[tokio::test]
    async fn test_production_tcp_missing_capabilities_rejected() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        use tokio::net::TcpStream;

        let server = BridgeServer::new_test();
        let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        let client = TcpStream::connect(local_addr).await.expect("Connect to TCP");
        let (r, mut w) = client.into_split();
        let mut buf_r = BufReader::new(r);

        let my_pid = std::process::id();
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec![]), // Empty capabilities!
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());

        w.write_all(format!("{}\n", serde_json::to_string(&env).unwrap()).as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut line = String::new();
        buf_r.read_line(&mut line).await.unwrap();
        assert!(line.contains(error_codes::CAPABILITIES_REQUIRED), "Expected CAPABILITIES_REQUIRED, got: {}", line);

        server.stop();
    }

    #[tokio::test]
    async fn test_production_tcp_unknown_capability_rejected() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        use tokio::net::TcpStream;

        let server = BridgeServer::new_test();
        let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        let client = TcpStream::connect(local_addr).await.expect("Connect to TCP");
        let (r, mut w) = client.into_split();
        let mut buf_r = BufReader::new(r);

        let my_pid = std::process::id();
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "DISALLOWED_FEATURE".into()]),
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());

        w.write_all(format!("{}\n", serde_json::to_string(&env).unwrap()).as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut line = String::new();
        buf_r.read_line(&mut line).await.unwrap();
        assert!(line.contains(error_codes::UNKNOWN_CAPABILITY), "Expected UNKNOWN_CAPABILITY, got: {}", line);

        server.stop();
    }

    #[tokio::test]
    async fn test_production_tcp_frame_boundary_64k() {
        use tokio::io::{AsyncReadExt, AsyncWriteExt};
        use tokio::net::TcpStream;

        let server = BridgeServer::new_test();
        let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        let mut client = TcpStream::connect(local_addr).await.expect("Connect to TCP");

        // Send a frame exceeding 64 KiB without newline delimiter (65537 bytes)
        let huge_frame = vec![b'A'; 65537];
        let _ = client.write_all(&huge_frame).await;
        let _ = client.write_all(b"\n").await;
        let _ = client.flush().await;

        // Server must close connection on frame overflow
        let mut resp = Vec::new();
        let mut buf = [0u8; 128];
        loop {
            match client.read(&mut buf).await {
                Ok(0) => break, // Clean EOF / connection closed by server
                Ok(n) => resp.extend_from_slice(&buf[..n]),
                Err(_) => break, // Connection reset / aborted by server
            }
        }

        // Connection must have terminated
        server.stop();
    }

    #[tokio::test]
    async fn test_production_tcp_heartbeat_ping_pong() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        use tokio::net::TcpStream;

        let server = BridgeServer::new_test();
        let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        let client = TcpStream::connect(local_addr).await.expect("Connect to TCP");
        let (r, mut w) = client.into_split();
        let mut buf_r = BufReader::new(r);

        let my_pid = std::process::id();
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into(), "spectator_hand_snapshot".into()]),
            session_challenge: Some(server.session_challenge()),
                    ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        w.write_all(format!("{}\n", serde_json::to_string(&env).unwrap()).as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut ack_line = String::new();
        buf_r.read_line(&mut ack_line).await.unwrap();
        assert!(ack_line.contains("ACCEPTED"));

        // Send Heartbeat
        let hb = HeartbeatPayload {
            sequence: 42,
            client_timestamp: Some(123456789),
            server_timestamp: None,
            echo_timestamp: None,
        };
        let hb_env = BridgeEnvelope::new(message_types::HEARTBEAT, serde_json::to_value(&hb).unwrap());
        w.write_all(format!("{}\n", serde_json::to_string(&hb_env).unwrap()).as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut hb_resp = String::new();
        buf_r.read_line(&mut hb_resp).await.unwrap();
        assert!(hb_resp.contains(message_types::HEARTBEAT));
        assert!(hb_resp.contains("\"sequence\":42"));
        assert!(hb_resp.contains("\"echoTimestamp\":123456789"));

        server.stop();
    }

    #[tokio::test]
    async fn test_multi_runtime_concurrent_sessions_and_disconnect_isolation() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        use tokio::net::TcpStream;

        let server = BridgeServer::new_test();
        let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        let my_pid = std::process::id();

        // Connect Client 1: Steam CN (MAIN)
        let client1 = TcpStream::connect(local_addr).await.expect("Connect client 1");
        let (r1, mut w1) = client1.into_split();
        let mut buf_r1 = BufReader::new(r1);

        let hello1_payload = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into()]),
            session_challenge: Some(server.session_challenge()),
            platform_id: Some("STEAM_CN".into()),
            player_id: Some(1556514),
            role: Some("MAIN".into()),
            ..Default::default()
        };
        let env1 = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello1_payload).unwrap());
        w1.write_all(format!("{}\n", serde_json::to_string(&env1).unwrap()).as_bytes()).await.unwrap();
        w1.flush().await.unwrap();

        let mut ack1_line = String::new();
        buf_r1.read_line(&mut ack1_line).await.unwrap();
        assert!(ack1_line.contains("ACCEPTED"), "Client 1 HELLO accepted");
        let ack1_env: BridgeEnvelope<HelloAckPayload> = serde_json::from_str(&ack1_line).unwrap();
        let conn1_id = ack1_env.payload.connection_id.expect("connection_id in ack 1");
        let gen1 = ack1_env.payload.generation.expect("generation in ack 1");

        // Connect Client 2: TapTap (USER_SPECTATOR)
        let client2 = TcpStream::connect(local_addr).await.expect("Connect client 2");
        let (r2, mut w2) = client2.into_split();
        let mut buf_r2 = BufReader::new(r2);

        let hello2_payload = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into(), "spectator_hand_snapshot".into()]),
            session_challenge: Some(server.session_challenge()),
            platform_id: Some("TAPTAP".into()),
            player_id: Some(3045259),
            role: Some("USER_SPECTATOR".into()),
            ..Default::default()
        };
        let env2 = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello2_payload).unwrap());
        w2.write_all(format!("{}\n", serde_json::to_string(&env2).unwrap()).as_bytes()).await.unwrap();
        w2.flush().await.unwrap();

        let mut ack2_line = String::new();
        buf_r2.read_line(&mut ack2_line).await.unwrap();
        assert!(ack2_line.contains("ACCEPTED"), "Client 2 HELLO accepted");
        let ack2_env: BridgeEnvelope<HelloAckPayload> = serde_json::from_str(&ack2_line).unwrap();
        let conn2_id = ack2_env.payload.connection_id.expect("connection_id in ack 2");
        let gen2 = ack2_env.payload.generation.expect("generation in ack 2");

        // Verify isolation of connections and generations
        assert_ne!(conn1_id, conn2_id, "Each connection must have a unique connection_id");
        assert_eq!(gen1, 1, "Client 1 starts at generation 1");
        assert_eq!(gen2, 1, "Client 2 starts at generation 1 independently");

        tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;

        // Verify both sessions registered concurrently in SessionManager
        let sessions = server.get_runtime_sessions();
        assert_eq!(sessions.len(), 2, "Both client 1 and client 2 must be concurrently active");
        assert!(sessions.iter().any(|s| s.connection_id == conn1_id && s.platform_id == "STEAM_CN" && s.role == "UNBOUND"));
        assert!(sessions.iter().any(|s| s.connection_id == conn2_id && s.platform_id == "TAPTAP" && s.role == "UNBOUND"));

        // Both clients can exchange heartbeats concurrently
        let hb1 = HeartbeatPayload { sequence: 101, client_timestamp: Some(1000), ..Default::default() };
        let hb1_env = BridgeEnvelope::new(message_types::HEARTBEAT, serde_json::to_value(&hb1).unwrap());
        w1.write_all(format!("{}\n", serde_json::to_string(&hb1_env).unwrap()).as_bytes()).await.unwrap();
        w1.flush().await.unwrap();

        let mut resp1 = String::new();
        buf_r1.read_line(&mut resp1).await.unwrap();
        assert!(resp1.contains(message_types::HEARTBEAT));
        assert!(resp1.contains("\"sequence\":101"));

        let hb2 = HeartbeatPayload { sequence: 202, client_timestamp: Some(2000), ..Default::default() };
        let hb2_env = BridgeEnvelope::new(message_types::HEARTBEAT, serde_json::to_value(&hb2).unwrap());
        w2.write_all(format!("{}\n", serde_json::to_string(&hb2_env).unwrap()).as_bytes()).await.unwrap();
        w2.flush().await.unwrap();

        let mut resp2 = String::new();
        buf_r2.read_line(&mut resp2).await.unwrap();
        assert!(resp2.contains(message_types::HEARTBEAT));
        assert!(resp2.contains("\"sequence\":202"));

        // Disconnect Client 1 cleanly
        drop(w1);
        drop(buf_r1);
        tokio::time::sleep(tokio::time::Duration::from_millis(150)).await;

        // Disconnecting Client 1 MUST NOT terminate or affect Client 2!
        let sessions_after_disconnect = server.get_runtime_sessions();
        assert_eq!(sessions_after_disconnect.len(), 1, "Only client 2 remains connected");
        assert_eq!(sessions_after_disconnect[0].connection_id, conn2_id);
        assert_eq!(sessions_after_disconnect[0].platform_id, "TAPTAP");

        // Client 2 remains fully operational
        let hb2_again = HeartbeatPayload { sequence: 203, client_timestamp: Some(3000), ..Default::default() };
        let hb2_again_env = BridgeEnvelope::new(message_types::HEARTBEAT, serde_json::to_value(&hb2_again).unwrap());
        w2.write_all(format!("{}\n", serde_json::to_string(&hb2_again_env).unwrap()).as_bytes()).await.unwrap();
        w2.flush().await.unwrap();

        let mut resp2_again = String::new();
        buf_r2.read_line(&mut resp2_again).await.unwrap();
        assert!(resp2_again.contains(message_types::HEARTBEAT));
        assert!(resp2_again.contains("\"sequence\":203"));

        drop(w2);
        drop(buf_r2);
        server.stop();
    }

    #[tokio::test]
    async fn test_multi_runtime_spectator_snapshot_tagging() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        use tokio::net::TcpStream;

        let server = BridgeServer::new_test();
        let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        let my_pid = std::process::id();
        let client = TcpStream::connect(local_addr).await.expect("Connect to TCP");
        let (r, mut w) = client.into_split();
        let mut buf_r = BufReader::new(r);

        // TapTap handshake
        let hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into(), "spectator_hand_snapshot".into()]),
            session_challenge: Some(server.session_challenge()),
            platform_id: Some("TAPTAP".into()),
            player_id: None,
            role: Some("USER_SPECTATOR".into()),
            ..Default::default()
        };
        let env = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap());
        w.write_all(format!("{}\n", serde_json::to_string(&env).unwrap()).as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        let mut ack_line = String::new();
        buf_r.read_line(&mut ack_line).await.unwrap();
        assert!(ack_line.contains("ACCEPTED"));
        let ack_env: BridgeEnvelope<HelloAckPayload> = serde_json::from_str(&ack_line).unwrap();
        let conn_id = ack_env.payload.connection_id.expect("connection_id in ack");

        // Send spectator snapshot
        let snap = make_test_snapshot(1, 1, "1.1.0");
        let snap_env = BridgeEnvelope::new(message_types::SPECTATOR_HAND_SNAPSHOT, serde_json::to_value(&snap).unwrap());
        w.write_all(format!("{}\n", serde_json::to_string(&snap_env).unwrap()).as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;

        let latest = server.get_latest_spectator_snapshot().expect("Snapshot ingested");
        // Verify tagged with connection_id and platform_id!
        assert_eq!(latest.connection_id, Some(conn_id.clone()));
        assert_eq!(latest.platform_id, Some("TAPTAP".to_string()));

        // Verify spectator_account_player_id was promoted in session (STRICT IDENTITY RULE: NEVER player_id 2081758!)
        let session = server.get_runtime_session(&conn_id).expect("Session found");
        assert_eq!(session.player_id, Some(1668707));

        drop(w);
        drop(buf_r);
        server.stop();
    }

    #[tokio::test]
    async fn test_platform_identity_authority_and_spoofing_rejection() {
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
        use tokio::net::TcpStream;

        let cur_exe = std::env::current_exe().unwrap();
        let cur_name = cur_exe.file_name().unwrap().to_string_lossy().to_string();
        let my_pid = std::process::id();

        // ---------------------------------------------------------------------
        // Case 1: Process verified as STEAM_CN, claims TAPTAP -> REJECT
        // ---------------------------------------------------------------------
        let mut policy_steam = crate::xing_shu::bridge::SecurityVerificationPolicy::test_policy();
        policy_steam.allowed_executables = vec![cur_name.clone()];
        policy_steam.expected_steam_cn_path = Some(cur_exe.clone());
        policy_steam.expected_taptap_path = Some(std::path::PathBuf::from(r"F:\NonExistent\TapTap\吉星派对.exe"));

        let server_steam = BridgeServer::with_security_policy_internal(policy_steam);
        let addr_steam = server_steam.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        let client1 = TcpStream::connect(addr_steam).await.expect("Connect client 1");
        let (r1, mut w1) = client1.into_split();
        let mut buf_r1 = BufReader::new(r1);

        let spoofed_taptap_hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into()]),
            session_challenge: Some(server_steam.session_challenge()),
            platform_id: Some("TAPTAP".into()), // Spoofed claim!
            ..Default::default()
        };
        let env1 = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&spoofed_taptap_hello).unwrap());
        w1.write_all(format!("{}\n", serde_json::to_string(&env1).unwrap()).as_bytes()).await.unwrap();
        w1.flush().await.unwrap();

        let mut resp1 = String::new();
        let _ = buf_r1.read_line(&mut resp1).await;
        assert!(resp1.contains(message_types::ERROR) || resp1.contains("Platform mismatch"), "Steam process claiming TAPTAP must be rejected: {}", resp1);
        server_steam.stop();

        // ---------------------------------------------------------------------
        // Case 2: Process verified as TAPTAP, claims STEAM_CN -> REJECT
        // ---------------------------------------------------------------------
        let mut policy_tap = crate::xing_shu::bridge::SecurityVerificationPolicy::test_policy();
        policy_tap.allowed_executables = vec![cur_name.clone()];
        policy_tap.expected_steam_cn_path = Some(std::path::PathBuf::from(r"E:\NonExistent\Steam\AstralParty_CN.exe"));
        policy_tap.expected_taptap_path = Some(cur_exe.clone());

        let server_tap = BridgeServer::with_security_policy_internal(policy_tap);
        let addr_tap = server_tap.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        let client2 = TcpStream::connect(addr_tap).await.expect("Connect client 2");
        let (r2, mut w2) = client2.into_split();
        let mut buf_r2 = BufReader::new(r2);

        let spoofed_steam_hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into()]),
            session_challenge: Some(server_tap.session_challenge()),
            platform_id: Some("STEAM_CN".into()), // Spoofed claim!
            ..Default::default()
        };
        let env2 = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&spoofed_steam_hello).unwrap());
        w2.write_all(format!("{}\n", serde_json::to_string(&env2).unwrap()).as_bytes()).await.unwrap();
        w2.flush().await.unwrap();

        let mut resp2 = String::new();
        let _ = buf_r2.read_line(&mut resp2).await;
        assert!(resp2.contains(message_types::ERROR) || resp2.contains("Platform mismatch"), "TapTap process claiming STEAM_CN must be rejected: {}", resp2);

        // ---------------------------------------------------------------------
        // Case 3: Process verified as TAPTAP, claims TAPTAP -> PASS
        // ---------------------------------------------------------------------
        let client3 = TcpStream::connect(addr_tap).await.expect("Connect client 3");
        let (r3, mut w3) = client3.into_split();
        let mut buf_r3 = BufReader::new(r3);

        let valid_tap_hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into()]),
            session_challenge: Some(server_tap.session_challenge()),
            platform_id: Some("TAPTAP".into()),
            role: Some("MAIN".into()), // Attempting to claim role="MAIN" from TapTap!
            ..Default::default()
        };
        let env3 = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&valid_tap_hello).unwrap());
        w3.write_all(format!("{}\n", serde_json::to_string(&env3).unwrap()).as_bytes()).await.unwrap();
        w3.flush().await.unwrap();

        let mut resp3 = String::new();
        buf_r3.read_line(&mut resp3).await.unwrap();
        assert!(resp3.contains("ACCEPTED"), "Valid TapTap claim must be accepted: {}", resp3);
        let ack3: BridgeEnvelope<HelloAckPayload> = serde_json::from_str(&resp3).unwrap();
        let conn3_id = ack3.payload.connection_id.expect("connection_id present");

        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        // ---------------------------------------------------------------------
        // Case 4: role="MAIN" from TapTap HELLO must NOT override server role.
        // Server registers initial role as UNBOUND.
        // ---------------------------------------------------------------------
        let session = server_tap.get_runtime_session(&conn3_id).expect("Session registered");
        assert_eq!(session.platform_id, "TAPTAP", "Authoritative platform is TAPTAP");
        assert_eq!(session.role, "UNBOUND", "HELLO.role must NOT be authoritative; initial server role is UNBOUND");

        server_tap.stop();
    }

    #[tokio::test]
    async fn test_dual_runtime_independent_identity_isolation_zero_leakage() {
        let my_pid = std::process::id();

        use tokio::net::TcpStream;
        use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};

        let server = BridgeServer::new_test();
        let addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        // 1. Connect Client 1: Steam CN (PID X)
        let client1 = TcpStream::connect(addr).await.expect("Connect client 1");
        let (r1, mut w1) = client1.into_split();
        let mut buf_r1 = BufReader::new(r1);

        let steam_hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into(), "RUNTIME".into(), "HOOKS".into()]),
            session_challenge: Some(server.session_challenge()),
            platform_id: Some("STEAM_CN".into()),
            ..Default::default()
        };
        let env1 = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&steam_hello).unwrap());
        w1.write_all(format!("{}\n", serde_json::to_string(&env1).unwrap()).as_bytes()).await.unwrap();
        w1.flush().await.unwrap();

        let mut resp1 = String::new();
        buf_r1.read_line(&mut resp1).await.unwrap();
        assert!(resp1.contains("ACCEPTED"), "Steam CN client accepted: {}", resp1);
        let ack1: BridgeEnvelope<HelloAckPayload> = serde_json::from_str(&resp1).unwrap();
        let conn1_id = ack1.payload.connection_id.expect("conn1_id");

        // 2. Connect Client 2: TapTap (PID Y)
        let client2 = TcpStream::connect(addr).await.expect("Connect client 2");
        let (r2, mut w2) = client2.into_split();
        let mut buf_r2 = BufReader::new(r2);

        let tap_hello = HelloPayload {
            component: Some("XingShu_Runtime".into()),
            runtime_version: Some("1.0.0".into()),
            game_pid: Some(my_pid),
            game_version: Some("1.3.8".into()),
            capabilities: Some(vec!["status".into(), "events".into(), "SPECTATOR".into(), "HOOKS".into()]),
            session_challenge: Some(server.session_challenge()),
            platform_id: Some("TAPTAP".into()),
            ..Default::default()
        };
        let env2 = BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&tap_hello).unwrap());
        w2.write_all(format!("{}\n", serde_json::to_string(&env2).unwrap()).as_bytes()).await.unwrap();
        w2.flush().await.unwrap();

        let mut resp2 = String::new();
        buf_r2.read_line(&mut resp2).await.unwrap();
        assert!(resp2.contains("ACCEPTED"), "TapTap client accepted: {}", resp2);
        let ack2: BridgeEnvelope<HelloAckPayload> = serde_json::from_str(&resp2).unwrap();
        let conn2_id = ack2.payload.connection_id.expect("conn2_id");

        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        // Verify initial state: both sessions connected, player_id is initially None
        let s1 = server.get_runtime_session(&conn1_id).expect("Session 1 exists");
        let s2 = server.get_runtime_session(&conn2_id).expect("Session 2 exists");
        assert_eq!(s1.player_id, None, "Steam CN player_id initially None");
        assert_eq!(s2.player_id, None, "TapTap player_id initially None");

        // 3. Client 1 (Steam CN) emits RUNTIME_IDENTITY_UPDATE with authoritative local player 1556514
        let update1 = RuntimeIdentityUpdatePayload {
            player_id: 1556514,
            account_id: Some(1556514),
            display_name: Some("SteamMainPlayer".into()),
            game_pid: Some(my_pid),
            platform_id: Some("STEAM_CN".into()),
            is_self: Some(true),
            identity_source: Some("AccountLogic".into()),
        };
        let env_update1 = BridgeEnvelope::new(message_types::RUNTIME_IDENTITY_UPDATE, serde_json::to_value(&update1).unwrap());
        w1.write_all(format!("{}\n", serde_json::to_string(&env_update1).unwrap()).as_bytes()).await.unwrap();
        w1.flush().await.unwrap();

        tokio::time::sleep(tokio::time::Duration::from_millis(60)).await;

        // Check isolation: Session 1 updated to 1556514 (MAIN), Session 2 strictly untouched!
        let s1_post = server.get_runtime_session(&conn1_id).expect("Session 1 exists");
        let s2_post = server.get_runtime_session(&conn2_id).expect("Session 2 exists");
        assert_eq!(s1_post.player_id, Some(1556514), "Steam CN session promoted to 1556514");
        assert_eq!(s1_post.role, "MAIN", "Steam CN session promoted to MAIN");
        assert_eq!(s2_post.player_id, None, "TapTap session unaffected (zero cross-session leakage)");

        // 4. Client 2 (TapTap) emits RUNTIME_IDENTITY_UPDATE with authoritative local spectator account 3045259
        let update2 = RuntimeIdentityUpdatePayload {
            player_id: 3045259,
            account_id: Some(3045259),
            display_name: Some("TapTapSpectator".into()),
            game_pid: Some(my_pid),
            platform_id: Some("TAPTAP".into()),
            is_self: Some(true),
            identity_source: Some("AccountLogic".into()),
        };
        let env_update2 = BridgeEnvelope::new(message_types::RUNTIME_IDENTITY_UPDATE, serde_json::to_value(&update2).unwrap());
        w2.write_all(format!("{}\n", serde_json::to_string(&env_update2).unwrap()).as_bytes()).await.unwrap();
        w2.flush().await.unwrap();

        tokio::time::sleep(tokio::time::Duration::from_millis(60)).await;

        let s1_post2 = server.get_runtime_session(&conn1_id).expect("Session 1 exists");
        let s2_post2 = server.get_runtime_session(&conn2_id).expect("Session 2 exists");
        assert_eq!(s1_post2.player_id, Some(1556514), "Steam CN session strictly retains 1556514");
        assert_eq!(s1_post2.role, "MAIN", "Steam CN session remains MAIN");
        assert_eq!(s2_post2.player_id, Some(3045259), "TapTap session promoted to 3045259");
        assert_eq!(s2_post2.role, "USER_SPECTATOR", "TapTap session promoted to USER_SPECTATOR");

        // 4b. Client 1 (Steam CN) reports active match context for room 88888
        let mut match_ctx1 = RuntimeMatchContext {
            connection_id: "conn1".into(),
            platform_id: "STEAM_CN".into(),
            process_id: my_pid,
            local_player_id: Some(1556514),
            internal_room_id: Some(88888),
            room_id: 88888,
            in_match: true,
            match_state: "MAIN_IN_MATCH".into(),
            is_pve: Some(true),
            match_kind: Some("PVE".into()),
            timestamp: chrono::Utc::now().timestamp_millis() as u64,
            generation: Some(1),
            ..Default::default()
        };
        match_ctx1.normalize();
        let env_ctx1 = BridgeEnvelope::new(message_types::RUNTIME_MATCH_CONTEXT, serde_json::to_value(&match_ctx1).unwrap());
        w1.write_all(format!("{}\n", serde_json::to_string(&env_ctx1).unwrap()).as_bytes()).await.unwrap();
        w1.flush().await.unwrap();
        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        // 5. Client 2 (TapTap) streams match packets where remote watched player is 212667
        let spectator_snap = SpectatorHandPayload {
            schema_version: "1.1.0".to_string(),
            spectator_session_epoch: 1,
            sequence_number: 1,
            player_id: 212667, // Remote player being watched!
            spectator_account_player_id: 3045259, // Authentic spectator UID
            room_id: 88888,
            hand_size: 4,
            cards: vec![],
            ..Default::default()
        };
        let env_snap = BridgeEnvelope::new(message_types::SPECTATOR_HAND_SNAPSHOT, serde_json::to_value(&spectator_snap).unwrap());
        w2.write_all(format!("{}\n", serde_json::to_string(&env_snap).unwrap()).as_bytes()).await.unwrap();
        w2.flush().await.unwrap();

        tokio::time::sleep(tokio::time::Duration::from_millis(60)).await;

        // HS4-P1.6R2: Client 1 (Steam CN) must receive mirrored HAND_TRUTH_MIRROR message!
        let mut mirror_line = String::new();
        let read_result = tokio::time::timeout(tokio::time::Duration::from_millis(500), buf_r1.read_line(&mut mirror_line)).await;
        assert!(read_result.is_ok(), "Client 1 must receive mirrored message within timeout");
        assert!(mirror_line.contains(message_types::HAND_TRUTH_MIRROR), "Must receive HAND_TRUTH_MIRROR: {}", mirror_line);

        let s1_final = server.get_runtime_session(&conn1_id).expect("Session 1 exists");
        let s2_final = server.get_runtime_session(&conn2_id).expect("Session 2 exists");
        assert_eq!(s1_final.player_id, Some(1556514), "Steam CN session strictly 1556514, immune to remote player 212667");
        assert_eq!(s2_final.player_id, Some(3045259), "TapTap session strictly 3045259, immune to remote player 212667");

        server.stop();
    }

    #[tokio::test]
    async fn test_mirror_producer_must_be_bound_taptap_spectator() {
        let server = BridgeServer::new_test();
        let (tx_steam, _rx_steam) = tokio::sync::mpsc::channel(16);
        let conn_steam = ActiveConnection::new("conn_steam", "STEAM_CN", 1000, 1, tx_steam);
        server.connection_manager.insert(conn_steam);

        let mut s_steam = RuntimeSession::new("conn_steam", "STEAM_CN", 1000, "", Some(1556514), "1.0", 1, "MAIN");
        s_steam.in_match = true;
        s_steam.room_id = Some(88888);
        server.session_manager.insert(s_steam);

        let snap = SpectatorHandPayload {
            schema_version: "1.1.0".to_string(),
            room_id: 88888,
            spectator_session_epoch: 1,
            spectator_account_player_id: 3045259,
            sequence_number: 1,
            player_id: 2000,
            ..Default::default()
        };

        // 1. Steam MAIN runtime attempts to be a mirror producer -> MUST FAIL
        let res_steam = server.send_hand_truth_mirror_to_main("conn_steam", &snap);
        assert_eq!(res_steam, Err("PRODUCER_INVARIANT_VIOLATION".to_string()));

        // 2. Unbound TapTap runtime attempts to be a mirror producer -> MUST FAIL
        let (tx_tap_unbound, _rx_tap_unbound) = tokio::sync::mpsc::channel(16);
        server.connection_manager.insert(ActiveConnection::new("conn_tap_unbound", "TAPTAP", 2000, 1, tx_tap_unbound));
        server.session_manager.insert(RuntimeSession::new("conn_tap_unbound", "TAPTAP", 2000, "", None, "1.0", 1, "UNBOUND"));
        let res_tap_unbound = server.send_hand_truth_mirror_to_main("conn_tap_unbound", &snap);
        assert_eq!(res_tap_unbound, Err("PRODUCER_INVARIANT_VIOLATION".to_string()));

        // 3. TapTap runtime with spoofed/wrong player ID -> MUST FAIL
        let (tx_tap_wrong, _rx_tap_wrong) = tokio::sync::mpsc::channel(16);
        server.connection_manager.insert(ActiveConnection::new("conn_tap_wrong", "TAPTAP", 2001, 1, tx_tap_wrong));
        server.session_manager.insert(RuntimeSession::new("conn_tap_wrong", "TAPTAP", 2001, "", Some(9999999), "1.0", 1, "UNBOUND"));
        let res_tap_wrong = server.send_hand_truth_mirror_to_main("conn_tap_wrong", &snap);
        assert_eq!(res_tap_wrong, Err("PRODUCER_INVARIANT_VIOLATION".to_string()));

        // 4. Non-existent session -> MUST FAIL
        let res_ghost = server.send_hand_truth_mirror_to_main("conn_non_existent", &snap);
        assert_eq!(res_ghost, Err("SOURCE_SESSION_NOT_FOUND".to_string()));

        // 5. Authorized bound TapTap spectator (platform_id="TAPTAP", player_id=3045259, role="USER_SPECTATOR") -> MUST SUCCEED
        let (tx_tap_ok, _rx_tap_ok) = tokio::sync::mpsc::channel(16);
        server.connection_manager.insert(ActiveConnection::new("conn_tap_ok", "TAPTAP", 2002, 1, tx_tap_ok));
        let s_tap_ok = RuntimeSession::new("conn_tap_ok", "TAPTAP", 2002, "", Some(3045259), "1.0", 1, "USER_SPECTATOR");
        server.session_manager.insert(s_tap_ok);

        let res_ok = server.send_hand_truth_mirror_to_main("conn_tap_ok", &snap);
        assert_eq!(res_ok, Ok("conn_steam".to_string()));
    }

    #[tokio::test]
    async fn test_mirror_target_must_be_bound_steam_main() {
        let server = BridgeServer::new_test();

        // Register bound TapTap spectator producer
        let (tx_tap, _rx_tap) = tokio::sync::mpsc::channel(16);
        server.connection_manager.insert(ActiveConnection::new("conn_tap", "TAPTAP", 2000, 1, tx_tap));
        server.session_manager.insert(RuntimeSession::new("conn_tap", "TAPTAP", 2000, "", Some(3045259), "1.0", 1, "USER_SPECTATOR"));

        let snap = SpectatorHandPayload {
            schema_version: "1.1.0".to_string(),
            room_id: 88888,
            spectator_session_epoch: 1,
            spectator_account_player_id: 3045259,
            sequence_number: 1,
            ..Default::default()
        };

        // Case A: No Steam target connected at all
        let res_no_target = server.send_hand_truth_mirror_to_main("conn_tap", &snap);
        assert_eq!(res_no_target, Err("TARGET_NOT_FOUND".to_string()));

        // Case B: Connected Steam client is UNBOUND (player_id None)
        let (tx_steam_unbound, _rx_steam_unbound) = tokio::sync::mpsc::channel(16);
        server.connection_manager.insert(ActiveConnection::new("conn_steam_unbound", "STEAM_CN", 1000, 1, tx_steam_unbound));
        server.session_manager.insert(RuntimeSession::new("conn_steam_unbound", "STEAM_CN", 1000, "", None, "1.0", 1, "UNBOUND"));
        let res_unbound = server.send_hand_truth_mirror_to_main("conn_tap", &snap);
        assert_eq!(res_unbound, Err("TARGET_NOT_FOUND".to_string()));

        // Case C: Connected Steam client has wrong player_id (not 1556514)
        let (tx_steam_wrong, _rx_steam_wrong) = tokio::sync::mpsc::channel(16);
        server.connection_manager.insert(ActiveConnection::new("conn_steam_wrong", "STEAM_CN", 1001, 1, tx_steam_wrong));
        server.session_manager.insert(RuntimeSession::new("conn_steam_wrong", "STEAM_CN", 1001, "", Some(999999), "1.0", 1, "UNBOUND"));
        let res_wrong = server.send_hand_truth_mirror_to_main("conn_tap", &snap);
        assert_eq!(res_wrong, Err("TARGET_NOT_FOUND".to_string()));

        // Case D: Bound Steam CN client (1556514, MAIN) connects
        let (tx_steam_main, _rx_steam_main) = tokio::sync::mpsc::channel(16);
        server.connection_manager.insert(ActiveConnection::new("conn_steam_main", "STEAM_CN", 1002, 1, tx_steam_main));
        let mut s_main = RuntimeSession::new("conn_steam_main", "STEAM_CN", 1002, "", Some(1556514), "1.0", 1, "MAIN");
        s_main.in_match = true;
        s_main.room_id = Some(88888);
        server.session_manager.insert(s_main);

        let res_main = server.send_hand_truth_mirror_to_main("conn_tap", &snap);
        assert_eq!(res_main, Ok("conn_steam_main".to_string()));
    }

    #[tokio::test]
    async fn test_mirror_room_mismatch_rejected() {
        let server = BridgeServer::new_test();

        // Producer: bound TapTap spectator
        let (tx_tap, _rx_tap) = tokio::sync::mpsc::channel(16);
        server.connection_manager.insert(ActiveConnection::new("conn_tap", "TAPTAP", 2000, 1, tx_tap));
        server.session_manager.insert(RuntimeSession::new("conn_tap", "TAPTAP", 2000, "", Some(3045259), "1.0", 1, "USER_SPECTATOR"));

        // Target: bound Steam MAIN
        let (tx_steam, mut rx_steam) = tokio::sync::mpsc::channel(16);
        server.connection_manager.insert(ActiveConnection::new("conn_steam", "STEAM_CN", 1000, 1, tx_steam));
        let mut s_main = RuntimeSession::new("conn_steam", "STEAM_CN", 1000, "", Some(1556514), "1.0", 1, "MAIN");
        s_main.in_match = false; // Not in match
        s_main.room_id = Some(88888);
        server.session_manager.insert(s_main);

        let snap = SpectatorHandPayload {
            schema_version: "1.1.0".to_string(),
            room_id: 88888,
            spectator_session_epoch: 1,
            spectator_account_player_id: 3045259,
            sequence_number: 1,
            ..Default::default()
        };

        // 1. Steam MAIN is not in match -> rejected
        let res_not_in_match = server.send_hand_truth_mirror_to_main("conn_tap", &snap);
        assert_eq!(res_not_in_match, Err("STEAM_MAIN_NOT_IN_MATCH".to_string()));
        assert!(rx_steam.try_recv().is_err(), "Target must receive nothing when not in match");

        // 2. Steam MAIN is in match, but room ID differs (spectator 88888 != main 77777)
        server.session_manager.update_match_context("conn_steam", 77777, true, Some(false));
        let res_diff_room = server.send_hand_truth_mirror_to_main("conn_tap", &snap);
        assert!(res_diff_room.is_err());
        let err_msg = res_diff_room.unwrap_err();
        assert!(
            err_msg.contains("HAND_TRUTH_MIRROR_ROOM_MISMATCH_REJECTED: spectator_room=88888 != main_room=77777"),
            "Error message must contain exact mismatch log format: {}",
            err_msg
        );
        assert!(rx_steam.try_recv().is_err(), "Target must receive nothing when room IDs differ");

        // 3. Spectator snapshot room is 0 -> rejected
        let snap_zero = SpectatorHandPayload { room_id: 0, ..snap.clone() };
        server.session_manager.update_match_context("conn_steam", 88888, true, Some(false));
        let res_zero = server.send_hand_truth_mirror_to_main("conn_tap", &snap_zero);
        assert!(res_zero.is_err());
        assert!(rx_steam.try_recv().is_err(), "Target must receive nothing when spectator room is 0");
    }

    #[tokio::test]
    async fn test_mirror_room_match_forwarded() {
        let server = BridgeServer::new_test();

        // 1. Producer: bound TapTap spectator
        let (tx_tap, _rx_tap) = tokio::sync::mpsc::channel(16);
        server.connection_manager.insert(ActiveConnection::new("conn_tap", "TAPTAP", 2000, 1, tx_tap));
        server.session_manager.insert(RuntimeSession::new("conn_tap", "TAPTAP", 2000, "", Some(3045259), "1.0", 1, "USER_SPECTATOR"));

        // 2. Target: bound Steam MAIN in matching room 88888
        let (tx_steam, mut rx_steam) = tokio::sync::mpsc::channel(16);
        server.connection_manager.insert(ActiveConnection::new("conn_steam_main", "STEAM_CN", 1000, 1, tx_steam));
        let mut s_main = RuntimeSession::new("conn_steam_main", "STEAM_CN", 1000, "", Some(1556514), "1.0", 1, "MAIN");
        s_main.in_match = true;
        s_main.room_id = Some(88888);
        server.session_manager.insert(s_main);

        // 3. Bystander client connected on loopback
        let (tx_bystander, mut rx_bystander) = tokio::sync::mpsc::channel(16);
        server.connection_manager.insert(ActiveConnection::new("conn_bystander", "STEAM_CN", 1001, 1, tx_bystander));
        server.session_manager.insert(RuntimeSession::new("conn_bystander", "STEAM_CN", 1001, "", None, "1.0", 1, "UNBOUND"));

        // 4. Send spectator snapshot matching room 88888
        let snap = SpectatorHandPayload {
            schema_version: "1.1.0".to_string(),
            room_id: 88888,
            spectator_session_epoch: 3,
            spectator_account_player_id: 3045259,
            sequence_number: 42,
            player_id: 212667,
            hand_size: 3,
            card_ids: vec![101, 102, 103],
            ..Default::default()
        };

        let forward_res = server.send_hand_truth_mirror_to_main("conn_tap", &snap);
        assert_eq!(forward_res, Ok("conn_steam_main".to_string()));

        // Verify Steam MAIN receives the mirrored message
        let received_line = rx_steam.try_recv().expect("Steam MAIN must receive mirrored line");
        assert!(received_line.contains(message_types::HAND_TRUTH_MIRROR));
        let env: BridgeEnvelope<SpectatorHandPayload> = serde_json::from_str(&received_line).expect("Parse mirrored envelope");
        assert_eq!(env.message_type, message_types::HAND_TRUTH_MIRROR);
        assert_eq!(env.payload.room_id, 88888);
        assert_eq!(env.payload.sequence_number, 42);
        assert_eq!(env.payload.player_id, 212667);
        assert_eq!(env.payload.hand_size, 3);

        // Verify bystander receives NOTHING (zero broadcast leakage)
        assert!(rx_bystander.try_recv().is_err(), "Bystander must receive ZERO mirrored messages");
    }

    #[tokio::test]
    async fn configured_platform_match_end_releases_only_the_spectator() {
        use super::super::session::RuntimeBinding;
        let server = BridgeServer::new_test();
        server.configure_runtime_bindings(vec![
            RuntimeBinding { platform_id: "STEAM_CN".into(), player_id: 111, role: "MAIN".into() },
            RuntimeBinding { platform_id: "STEAM_CN".into(), player_id: 222, role: "USER_SPECTATOR".into() },
        ]).unwrap();
        let (main_tx, mut main_rx) = tokio::sync::mpsc::channel(16);
        let (spec_tx, mut spec_rx) = tokio::sync::mpsc::channel(16);
        for (name, id, tx) in [("main", 111, main_tx), ("spec", 222, spec_tx)] {
            server.session_manager.insert(RuntimeSession::new(name, "STEAM_CN", id as u32, "", Some(id), "test", 1, "UNBOUND"));
            server.connection_manager.insert(ActiveConnection::new(name, "STEAM_CN", id as u32, 1, tx));
        }
        assert!(server.send_watch_target("main", serde_json::json!({"roomId":71}), 111).is_err());
        server.send_watch_target("spec", serde_json::json!({"roomId":71}), 222).unwrap();
        let command: serde_json::Value = serde_json::from_str(&spec_rx.try_recv().unwrap()).unwrap();
        assert_eq!(command["payload"]["targetPlatform"], "STEAM_CN");
        let mut ctx = RuntimeMatchContext { connection_id: "main".into(), platform_id: "STEAM_CN".into(),
            process_id: 111, local_player_id: Some(111), internal_room_id: Some(71), room_id: 71,
            match_state: "MAIN_IN_MATCH".into(), in_match: true, sequence: 1, generation: Some(1), ..Default::default() };
        let raw = serde_json::to_string(&BridgeEnvelope::new(message_types::RUNTIME_MATCH_CONTEXT, serde_json::to_value(&ctx).unwrap())).unwrap();
        server.process_incoming_message_for_connection(&raw, "main", "STEAM_CN", 1).await;
        server.state.lock().unwrap().latest_spectator_snapshot = Some(SpectatorHandPayload { room_id: 71, ..Default::default() });
        ctx.match_state = "MATCH_ENDED".into(); ctx.in_match = false; ctx.sequence = 2;
        let raw = serde_json::to_string(&BridgeEnvelope::new(message_types::RUNTIME_MATCH_CONTEXT, serde_json::to_value(&ctx).unwrap())).unwrap();
        server.process_incoming_message_for_connection(&raw, "main", "STEAM_CN", 1).await;
        let stop: serde_json::Value = serde_json::from_str(&spec_rx.try_recv().unwrap()).unwrap();
        assert_eq!(stop["messageType"], "WATCH_STOP");
        assert_eq!(stop["payload"]["roomId"], 71);
        assert!(main_rx.try_recv().is_err());
        assert!(server.get_latest_spectator_snapshot().is_none());
        // Replay of an older main context cannot emit another stop.
        server.process_incoming_message_for_connection(&raw, "main", "STEAM_CN", 1).await;
        assert!(spec_rx.try_recv().is_err());
    }

    #[tokio::test]
    async fn test_runtime_match_context_tracking() {
        let server = BridgeServer::new_test();
        let (tx, _rx) = tokio::sync::mpsc::channel(16);
        server.connection_manager.insert(ActiveConnection::new("conn_rt_ctx", "STEAM_CN", 3456, 1, tx));
        server.session_manager.insert(RuntimeSession::new("conn_rt_ctx", "STEAM_CN", 3456, "", None, "1.0", 1, "UNBOUND"));

        // Initially no match context
        let s_init = server.get_runtime_session("conn_rt_ctx").expect("Session exists");
        assert_eq!(s_init.room_id, None);
        assert!(!s_init.in_match);
        assert_eq!(server.get_runtime_match_context("conn_rt_ctx"), None);

        // Send RUNTIME_MATCH_CONTEXT via process_incoming_message_for_connection
        let mut ctx = RuntimeMatchContext {
            connection_id: "conn_rt_ctx".into(),
            platform_id: "STEAM_CN".into(),
            process_id: 3456,
            local_player_id: Some(1556514),
            internal_room_id: Some(654321),
            room_id: 654321,
            spectator_code: Some("C05FE7B".into()),
            match_state: "MAIN_IN_MATCH".into(),
            match_kind: Some("PVE".into()),
            player_count: 4,
            roster_player_ids: vec![1556514, 212667, 3045259, 999888],
            scene: Some("PveBossScene".into()),
            in_match: true,
            is_pve: Some(true),
            timestamp: 1690000000000,
            sequence: 1,
            generation: Some(1),
        };
        ctx.normalize();
        let env = BridgeEnvelope::new(message_types::RUNTIME_MATCH_CONTEXT, serde_json::to_value(&ctx).unwrap());
        let raw_json = serde_json::to_string(&env).unwrap();

        server.process_incoming_message_for_connection(&raw_json, "conn_rt_ctx", "STEAM_CN", 1).await;

        // Verify session updated
        let s_updated = server.get_runtime_session("conn_rt_ctx").expect("Session exists");
        assert_eq!(s_updated.player_id, Some(1556514));
        assert_eq!(s_updated.live_player_id, Some(1556514));
        assert_eq!(s_updated.role, "MAIN");
        assert_eq!(s_updated.bound_role, "MAIN");
        assert_eq!(s_updated.room_id, Some(654321));
        assert_eq!(s_updated.internal_room_id, Some(654321));
        assert_eq!(s_updated.spectator_code.as_deref(), Some("C05FE7B"));
        assert!(s_updated.in_match);
        assert_eq!(s_updated.is_pve, Some(true));
        assert!(s_updated.match_context_updated_at.is_some());
        assert!(s_updated.match_context.is_some());

        // Verify InnerServerState match_contexts updated
        let stored_ctx = server.get_runtime_match_context("conn_rt_ctx").expect("Match context exists");
        assert_eq!(stored_ctx.room_id, 654321);
        assert_eq!(stored_ctx.internal_room_id, Some(654321));
        assert_eq!(stored_ctx.spectator_code.as_deref(), Some("C05FE7B"));
        assert_eq!(stored_ctx.match_state, "MAIN_IN_MATCH");
        assert_eq!(stored_ctx.match_kind.as_deref(), Some("PVE"));
        assert_eq!(stored_ctx.player_count, 4);
        assert!(stored_ctx.in_match);
        assert_eq!(stored_ctx.is_pve, Some(true));
        assert_eq!(stored_ctx.local_player_id, Some(1556514));

        // Update match context to in_match=false, roomId=0
        let mut ctx_leave = RuntimeMatchContext {
            connection_id: "conn_rt_ctx".into(),
            platform_id: "STEAM_CN".into(),
            process_id: 3456,
            local_player_id: Some(1556514),
            internal_room_id: Some(0),
            room_id: 0,
            spectator_code: None,
            match_state: "MAIN_MENU".into(),
            match_kind: None,
            player_count: 0,
            roster_player_ids: vec![],
            scene: Some("LobbyScene".into()),
            in_match: false,
            is_pve: Some(false),
            timestamp: 1690000010000,
            sequence: 2,
            generation: Some(1),
        };
        ctx_leave.normalize();
        let env_leave = BridgeEnvelope::new(message_types::RUNTIME_MATCH_CONTEXT, serde_json::to_value(&ctx_leave).unwrap());
        server.process_incoming_message_for_connection(&serde_json::to_string(&env_leave).unwrap(), "conn_rt_ctx", "STEAM_CN", 1).await;

        let s_leave = server.get_runtime_session("conn_rt_ctx").expect("Session exists");
        assert_eq!(s_leave.room_id, Some(0));
        assert!(!s_leave.in_match);
        let stored_leave = server.get_runtime_match_context("conn_rt_ctx").expect("Match context exists");
        assert_eq!(stored_leave.room_id, 0);
        assert!(!stored_leave.in_match);
    }
}
