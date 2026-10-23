// -----------------------------------------------------------------------
// PROJECT XINGSHU — HS3.2.1 Diagnostic Hub TCP Endpoint
// Strictly bound to 127.0.0.1:25890 (Prohibits 0.0.0.0 / External interfaces)
// Non-production diagnostic listener; Named Pipe remains production transport.
// -----------------------------------------------------------------------

use std::net::{IpAddr, Ipv4Addr, SocketAddr};
use std::path::PathBuf;
use std::sync::atomic::{AtomicU64, AtomicUsize, Ordering};
use std::sync::Arc;
use tokio::io::{AsyncWriteExt, BufReader};
use tokio::net::{TcpListener, TcpStream};
use serde::{Deserialize, Serialize};

use super::protocol::{MAX_MESSAGE_SIZE, XINGSHU_BRIDGE_PROTOCOL_VERSION};
use super::server::{read_bounded_line, BoundedLineError};

pub const DIAGNOSTIC_TCP_DEFAULT_PORT: u16 = 25890;
pub const DIAGNOSTIC_TCP_DEFAULT_ADDR: &str = "127.0.0.1:25890";
pub const DIAGNOSTIC_TCP_MAX_FRAME_BYTES: usize = MAX_MESSAGE_SIZE; // 64 KiB

#[derive(Debug, thiserror::Error)]
pub enum DiagnosticTcpError {
    #[error("Prohibited bind address: {0}")]
    ProhibitedBindAddress(String),
    #[error("Invalid address: {0}")]
    InvalidAddress(String),
    #[error("I/O error: {0}")]
    Io(#[from] std::io::Error),
    #[error("Framing error: {0}")]
    Frame(#[from] BoundedLineError),
    #[error("JSON error: {0}")]
    Json(#[from] serde_json::Error),
}

/// Resolves the file targets for appending diagnostic logs.
/// Supports both repository root execution and src-tauri execution paths.
pub fn get_diagnostic_log_paths() -> Vec<PathBuf> {
    let mut paths = Vec::new();

    // 1. AstralParty-Hub/src-tauri/diagnostic_hub_tcp.log (from workspace root)
    let p1 = PathBuf::from("AstralParty-Hub/src-tauri/diagnostic_hub_tcp.log");
    if let Some(parent) = p1.parent() {
        if parent.is_dir() {
            paths.push(p1);
        }
    }

    // 2. diagnostic_hub_tcp.log in the current working directory
    let p2 = PathBuf::from("diagnostic_hub_tcp.log");
    if !paths.contains(&p2) {
        paths.push(p2);
    }

    paths
}

/// Write diagnostic logs to stdout and append to diagnostic_hub_tcp.log
pub fn log_diagnostic(tag: &str, message: &str) {
    let now = chrono::Local::now().format("%Y-%m-%d %H:%M:%S%.3f");
    let file_line = format!("[{now}] {tag} {message}");
    let stdout_line = format!("{tag} {message}");

    // Print to stdout
    #[cfg(feature = "web-ui")]
    println!("{stdout_line}");
    #[cfg(not(feature = "web-ui"))]
    eprintln!("{stdout_line}");

    // Append to log files
    for path in get_diagnostic_log_paths() {
        if let Some(parent) = path.parent() {
            if !parent.as_os_str().is_empty() && !parent.exists() {
                let _ = std::fs::create_dir_all(parent);
            }
        }
        if let Ok(mut file) = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(&path)
        {
            use std::io::Write;
            let _ = writeln!(file, "{file_line}");
        }
    }
}

/// Extracted client handshake information
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DiagnosticClientInfo {
    pub component: Option<String>,
    pub runtime_version: Option<String>,
    pub game_pid: Option<u32>,
    pub game_version: Option<String>,
    pub capabilities: Vec<String>,
    pub session_challenge: Option<String>,
}

/// Diagnostic TCP Server instance
pub struct DiagnosticTcpServer {
    listener: TcpListener,
    local_addr: SocketAddr,
    generation: Arc<AtomicU64>,
    shutdown_notify: Arc<tokio::sync::Notify>,
    active_connections: Arc<AtomicUsize>,
}

impl DiagnosticTcpServer {
    /// Bind strictly to 127.0.0.1. Rejects 0.0.0.0 or any non-loopback IP address.
    pub async fn bind(addr_str: &str) -> Result<Self, DiagnosticTcpError> {
        let parsed_addr: SocketAddr = addr_str
            .parse()
            .map_err(|e| DiagnosticTcpError::InvalidAddress(format!("{}: {}", addr_str, e)))?;

        // STRICT SECURITY INVARIANT:
        // Must bind strictly to 127.0.0.1. Prohibit 0.0.0.0 and non-loopback addresses.
        if parsed_addr.ip() != IpAddr::V4(Ipv4Addr::new(127, 0, 0, 1)) {
            let msg = format!(
                "Security violation: Diagnostic TCP server strictly prohibited from binding to non-127.0.0.1 address '{}'. 0.0.0.0 and external interfaces are forbidden.",
                parsed_addr
            );
            log_diagnostic("[HUB_TCP_SECURITY_REJECT]", &msg);
            return Err(DiagnosticTcpError::ProhibitedBindAddress(msg));
        }

        let listener = super::tcp_security::create_exclusive_tcp_listener(parsed_addr)
            .map_err(|e| match e {
                super::tcp_security::SecurityError::BindError(msg) => {
                    DiagnosticTcpError::ProhibitedBindAddress(msg)
                }
                other => DiagnosticTcpError::ProhibitedBindAddress(other.to_string()),
            })?;
        let local_addr = listener.local_addr().map_err(DiagnosticTcpError::Io)?;

        log_diagnostic(
            "[HUB_TCP_BIND]",
            &format!("Diagnostic TCP listener bound strictly to {}", local_addr),
        );

        Ok(Self {
            listener,
            local_addr,
            generation: Arc::new(AtomicU64::new(0)),
            shutdown_notify: Arc::new(tokio::sync::Notify::new()),
            active_connections: Arc::new(AtomicUsize::new(0)),
        })
    }

    /// Bind to default diagnostic address `127.0.0.1:25890`
    pub async fn bind_default() -> Result<Self, DiagnosticTcpError> {
        Self::bind(DIAGNOSTIC_TCP_DEFAULT_ADDR).await
    }

    pub fn local_addr(&self) -> SocketAddr {
        self.local_addr
    }

    pub fn current_generation(&self) -> u64 {
        self.generation.load(Ordering::SeqCst)
    }

    pub fn active_connection_count(&self) -> usize {
        self.active_connections.load(Ordering::SeqCst)
    }

    pub fn shutdown(&self) {
        self.shutdown_notify.notify_waiters();
    }

    pub fn shutdown_handle(&self) -> Arc<tokio::sync::Notify> {
        self.shutdown_notify.clone()
    }

    /// Runs the diagnostic server accept loop until shutdown is signaled.
    pub async fn run(&self) -> Result<(), DiagnosticTcpError> {
        log_diagnostic(
            "[HUB_TCP_RUNNING]",
            &format!("Diagnostic TCP listener running on {}", self.local_addr),
        );

        loop {
            tokio::select! {
                _ = self.shutdown_notify.notified() => {
                    log_diagnostic("[HUB_TCP_SHUTDOWN]", "Diagnostic TCP listener received shutdown signal");
                    break;
                }
                accept_result = self.listener.accept() => {
                    let (socket, remote_addr) = match accept_result {
                        Ok(conn) => conn,
                        Err(e) => {
                            log_diagnostic("[HUB_TCP_ERROR]", &format!("Accept error: {}", e));
                            continue;
                        }
                    };

                    // Strict remote address verification: verify loopback (127.0.0.1)
                    if remote_addr.ip() != IpAddr::V4(Ipv4Addr::new(127, 0, 0, 1)) && !remote_addr.ip().is_loopback() {
                        log_diagnostic(
                            "[HUB_TCP_REJECT]",
                            &format!("Rejected non-loopback connection from {}", remote_addr),
                        );
                        continue;
                    }

                    let session_gen = self.generation.fetch_add(1, Ordering::SeqCst) + 1;
                    log_diagnostic(
                        "[HUB_TCP_ACCEPT]",
                        &format!("Remote address: {} (verify 127.0.0.1) - Session Generation: {}", remote_addr, session_gen),
                    );

                    let shutdown = self.shutdown_notify.clone();
                    let active_conns = self.active_connections.clone();
                    active_conns.fetch_add(1, Ordering::SeqCst);

                    tokio::spawn(async move {
                        Self::handle_connection(socket, remote_addr, session_gen, shutdown).await;
                        active_conns.fetch_sub(1, Ordering::SeqCst);
                    });
                }
            }
        }

        Ok(())
    }

    /// Accepts and handles a single connection synchronously (useful for targeted testing).
    pub async fn accept_one(&self) -> Result<(SocketAddr, u64), DiagnosticTcpError> {
        let (socket, remote_addr) = self.listener.accept().await.map_err(DiagnosticTcpError::Io)?;

        if remote_addr.ip() != IpAddr::V4(Ipv4Addr::new(127, 0, 0, 1)) && !remote_addr.ip().is_loopback() {
            log_diagnostic(
                "[HUB_TCP_REJECT]",
                &format!("Rejected non-loopback connection from {}", remote_addr),
            );
            return Err(DiagnosticTcpError::ProhibitedBindAddress(format!(
                "Non-loopback client: {}",
                remote_addr
            )));
        }

        let session_gen = self.generation.fetch_add(1, Ordering::SeqCst) + 1;
        log_diagnostic(
            "[HUB_TCP_ACCEPT]",
            &format!("Remote address: {} (verify 127.0.0.1) - Session Generation: {}", remote_addr, session_gen),
        );

        let shutdown = self.shutdown_notify.clone();
        Self::handle_connection(socket, remote_addr, session_gen, shutdown).await;
        Ok((remote_addr, session_gen))
    }

    /// Handles an individual client connection:
    /// 1. Reads incoming line (max 64 KiB)
    /// 2. Validates HELLO message and extracts client info
    /// 3. Sends HELLO_ACK response line with status: "ACCEPTED", protocolVersion: "0.1", generation
    /// 4. Keeps connection alive for subsequent messages or handles graceful disconnect.
    async fn handle_connection(
        socket: TcpStream,
        remote_addr: SocketAddr,
        generation: u64,
        shutdown: Arc<tokio::sync::Notify>,
    ) {
        let (read_half, mut write_half) = socket.into_split();
        let mut reader = BufReader::new(read_half);

        // 1. Read initial frame (HELLO)
        let first_line = match read_bounded_line(&mut reader, DIAGNOSTIC_TCP_MAX_FRAME_BYTES).await {
            Ok(Some(line)) => line,
            Ok(None) => {
                log_diagnostic(
                    "[HUB_TCP_DISCONNECT]",
                    &format!("Client {} closed connection before sending handshake", remote_addr),
                );
                return;
            }
            Err(e) => {
                log_diagnostic(
                    "[HUB_TCP_ERROR]",
                    &format!("Framing error during handshake from {}: {:?}", remote_addr, e),
                );
                return;
            }
        };

        let trimmed_first = first_line.trim();
        log_diagnostic("[HUB_TCP_RECV_HELLO]", trimmed_first);

        // 2. Parse BridgeEnvelope and validate HELLO
        let raw_val: serde_json::Value = match serde_json::from_str(trimmed_first) {
            Ok(v) => v,
            Err(e) => {
                log_diagnostic(
                    "[HUB_TCP_ERROR]",
                    &format!("Malformed JSON handshake from {}: {}", remote_addr, e),
                );
                return;
            }
        };

        let msg_type = raw_val.get("messageType").and_then(|v| v.as_str()).unwrap_or("");
        if msg_type != "HELLO" {
            log_diagnostic(
                "[HUB_TCP_ERROR]",
                &format!("Expected messageType HELLO but received '{}' from {}", msg_type, remote_addr),
            );
            return;
        }

        // Extract client info
        let payload = raw_val.get("payload");
        let client_info = DiagnosticClientInfo {
            component: payload.and_then(|p| p.get("component")).and_then(|v| v.as_str()).map(String::from),
            runtime_version: payload.and_then(|p| p.get("runtimeVersion")).and_then(|v| v.as_str()).map(String::from),
            game_pid: payload.and_then(|p| p.get("gamePid")).and_then(|v| v.as_u64()).map(|p| p as u32),
            game_version: payload.and_then(|p| p.get("gameVersion")).and_then(|v| v.as_str()).map(String::from),
            capabilities: payload.and_then(|p| p.get("capabilities")).and_then(|v| v.as_array())
                .map(|arr| arr.iter().filter_map(|s| s.as_str().map(String::from)).collect())
                .unwrap_or_default(),
            session_challenge: payload.and_then(|p| p.get("sessionChallenge").or_else(|| p.get("authChallenge")))
                .and_then(|v| v.as_str()).map(String::from),
        };

        log_diagnostic(
            "[HUB_TCP_CLIENT_INFO]",
            &format!(
                "Client Info: component={:?}, runtime_version={:?}, game_pid={:?}, game_version={:?}, capabilities={:?}",
                client_info.component,
                client_info.runtime_version,
                client_info.game_pid,
                client_info.game_version,
                client_info.capabilities
            ),
        );

        // 3. Construct HELLO_ACK envelope
        let req_id = raw_val
            .get("requestId")
            .or_else(|| raw_val.get("messageId"))
            .and_then(|v| v.as_str())
            .unwrap_or("ack_001");

        let ack = serde_json::json!({
            "protocolVersion": XINGSHU_BRIDGE_PROTOCOL_VERSION,
            "messageType": "HELLO_ACK",
            "requestId": req_id,
            "messageId": req_id,
            "timestamp": chrono::Utc::now().timestamp_millis(),
            "payload": {
                "status": "ACCEPTED",
                "protocolVersion": XINGSHU_BRIDGE_PROTOCOL_VERSION,
                "generation": generation,
                "hubVersion": env!("CARGO_PKG_VERSION"),
                "capabilities": ["spectator_hand_snapshot", "status", "events"],
                "heartbeatIntervalMs": 3000
            }
        });

        let ack_str = ack.to_string();
        log_diagnostic("[HUB_TCP_SEND_HELLO_ACK]", &ack_str);

        if let Err(e) = write_half.write_all(format!("{}\n", ack_str).as_bytes()).await {
            log_diagnostic(
                "[HUB_TCP_ERROR]",
                &format!("Failed to send HELLO_ACK to {}: {}", remote_addr, e),
            );
            return;
        }
        if let Err(e) = write_half.flush().await {
            log_diagnostic(
                "[HUB_TCP_ERROR]",
                &format!("Failed to flush HELLO_ACK to {}: {}", remote_addr, e),
            );
            return;
        }

        // 4. Message loop: Keep connection alive or handle graceful disconnect
        loop {
            tokio::select! {
                _ = shutdown.notified() => {
                    log_diagnostic(
                        "[HUB_TCP_DISCONNECT]",
                        &format!("Server shutdown: closing connection with {}", remote_addr),
                    );
                    break;
                }
                res = read_bounded_line(&mut reader, DIAGNOSTIC_TCP_MAX_FRAME_BYTES) => {
                    match res {
                        Ok(Some(line)) => {
                            let trimmed = line.trim();
                            if trimmed.is_empty() {
                                continue;
                            }

                            if let Ok(val) = serde_json::from_str::<serde_json::Value>(trimmed) {
                                let msg_type = val.get("messageType").and_then(|v| v.as_str()).unwrap_or("");
                                match msg_type {
                                    "HEARTBEAT" => {
                                        let seq = val.get("payload")
                                            .and_then(|p| p.get("sequence"))
                                            .and_then(|s| s.as_u64())
                                            .unwrap_or(0);
                                        let hb_ack = serde_json::json!({
                                            "protocolVersion": XINGSHU_BRIDGE_PROTOCOL_VERSION,
                                            "messageType": "HEARTBEAT",
                                            "requestId": uuid::Uuid::new_v4().to_string(),
                                            "timestamp": chrono::Utc::now().timestamp_millis(),
                                            "payload": {
                                                "sequence": seq,
                                                "serverTimestamp": chrono::Utc::now().timestamp_millis()
                                            }
                                        });
                                        let hb_str = hb_ack.to_string();
                                        log_diagnostic("[HUB_TCP_SEND_HEARTBEAT_ACK]", &hb_str);
                                        if let Err(e) = write_half.write_all(format!("{}\n", hb_str).as_bytes()).await {
                                            log_diagnostic("[HUB_TCP_ERROR]", &format!("Failed writing heartbeat ack: {}", e));
                                            break;
                                        }
                                        let _ = write_half.flush().await;
                                    }
                                    "SPECTATOR_HAND_SNAPSHOT" => {
                                        log_diagnostic(
                                            "[HUB_TCP_RECV_SNAPSHOT]",
                                            &format!("Received SPECTATOR_HAND_SNAPSHOT frame ({} bytes)", trimmed.len()),
                                        );
                                    }
                                    other => {
                                        log_diagnostic(
                                            "[HUB_TCP_RECV]",
                                            &format!("Received messageType='{}' ({} bytes)", other, trimmed.len()),
                                        );
                                    }
                                }
                            } else {
                                log_diagnostic("[HUB_TCP_RECV_RAW]", trimmed);
                            }
                        }
                        Ok(None) => {
                            log_diagnostic(
                                "[HUB_TCP_DISCONNECT]",
                                &format!("Client {} disconnected gracefully (EOF)", remote_addr),
                            );
                            break;
                        }
                        Err(e) => {
                            log_diagnostic(
                                "[HUB_TCP_ERROR]",
                                &format!("Error reading frame from {}: {:?}", remote_addr, e),
                            );
                            break;
                        }
                    }
                }
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tokio::io::AsyncBufReadExt;

    #[tokio::test]
    async fn test_strict_bind_prohibits_0_0_0_0() {
        let res = DiagnosticTcpServer::bind("0.0.0.0:25890").await;
        assert!(res.is_err(), "Must prohibit binding to 0.0.0.0");
        match res.err().unwrap() {
            DiagnosticTcpError::ProhibitedBindAddress(msg) => {
                assert!(msg.contains("0.0.0.0"), "Error message must cite 0.0.0.0 prohibited");
            }
            other => panic!("Expected ProhibitedBindAddress, got: {:?}", other),
        }
    }

    #[tokio::test]
    async fn test_strict_bind_prohibits_external_ip() {
        let res = DiagnosticTcpServer::bind("192.168.1.100:25890").await;
        assert!(res.is_err(), "Must prohibit binding to external IP");
        match res.err().unwrap() {
            DiagnosticTcpError::ProhibitedBindAddress(msg) => {
                assert!(msg.contains("non-127.0.0.1"), "Error message must cite non-127.0.0.1 prohibited");
            }
            other => panic!("Expected ProhibitedBindAddress, got: {:?}", other),
        }
    }

    #[tokio::test]
    async fn test_strict_bind_allows_127_0_0_1() {
        let server = DiagnosticTcpServer::bind("127.0.0.1:0").await.expect("Failed binding loopback");
        let local_addr = server.local_addr();
        assert_eq!(local_addr.ip(), IpAddr::V4(Ipv4Addr::new(127, 0, 0, 1)));
        assert!(local_addr.ip().is_loopback());
        assert_ne!(local_addr.ip().to_string(), "0.0.0.0");
    }

    #[tokio::test]
    async fn test_handshake_loop_hello_hello_ack() {
        let server = DiagnosticTcpServer::bind("127.0.0.1:0").await.expect("Bind failed");
        let server_addr = server.local_addr();

        let server_handle = tokio::spawn(async move {
            server.accept_one().await.expect("accept_one failed");
        });

        let mut client = TcpStream::connect(server_addr).await.expect("Client connect failed");
        let (read_half, mut write_half) = client.split();
        let mut reader = BufReader::new(read_half);
        let mut line = String::new();

        // Send HELLO
        let hello = serde_json::json!({
            "protocolVersion": "0.1",
            "messageType": "HELLO",
            "requestId": "test_req_42",
            "timestamp": 1729000000000i64,
            "payload": {
                "component": "XingShu_Runtime",
                "runtimeVersion": "3.3.0",
                "gamePid": 98765,
                "gameVersion": "1.3.8",
                "capabilities": ["spectator_hand_snapshot", "status"]
            }
        });
        write_half.write_all(format!("{}\n", hello).as_bytes()).await.unwrap();
        write_half.flush().await.unwrap();

        // Read HELLO_ACK
        reader.read_line(&mut line).await.unwrap();
        let ack: serde_json::Value = serde_json::from_str(&line).expect("Invalid ACK JSON");

        assert_eq!(ack["messageType"], "HELLO_ACK");
        assert_eq!(ack["protocolVersion"], "0.1");
        assert_eq!(ack["requestId"], "test_req_42");
        assert_eq!(ack["payload"]["status"], "ACCEPTED");
        assert_eq!(ack["payload"]["protocolVersion"], "0.1");
        assert_eq!(ack["payload"]["generation"], 1);

        // Drop client stream to signal EOF to server
        drop(reader);
        drop(write_half);
        drop(client);

        server_handle.await.unwrap();
    }

    #[tokio::test]
    async fn test_generation_advances_on_reconnection() {
        let server = Arc::new(DiagnosticTcpServer::bind("127.0.0.1:0").await.expect("Bind failed"));
        let server_addr = server.local_addr();

        let s_clone = server.clone();
        let server_handle = tokio::spawn(async move {
            let _ = s_clone.run().await;
        });

        // Connection 1
        {
            let mut c1 = TcpStream::connect(server_addr).await.unwrap();
            c1.write_all(b"{\"protocolVersion\":\"0.1\",\"messageType\":\"HELLO\",\"requestId\":\"r1\"}\n").await.unwrap();
            let mut reader1 = BufReader::new(&mut c1);
            let mut line1 = String::new();
            reader1.read_line(&mut line1).await.unwrap();
            let ack1: serde_json::Value = serde_json::from_str(&line1).unwrap();
            assert_eq!(ack1["payload"]["generation"], 1);
        }

        tokio::time::sleep(std::time::Duration::from_millis(30)).await;

        // Connection 2 (Reconnection)
        {
            let mut c2 = TcpStream::connect(server_addr).await.unwrap();
            c2.write_all(b"{\"protocolVersion\":\"0.1\",\"messageType\":\"HELLO\",\"requestId\":\"r2\"}\n").await.unwrap();
            let mut reader2 = BufReader::new(&mut c2);
            let mut line2 = String::new();
            reader2.read_line(&mut line2).await.unwrap();
            let ack2: serde_json::Value = serde_json::from_str(&line2).unwrap();
            assert_eq!(ack2["payload"]["generation"], 2);
        }

        server.shutdown();
        let _ = server_handle.await;
        assert_eq!(server.current_generation(), 2);
    }
}
