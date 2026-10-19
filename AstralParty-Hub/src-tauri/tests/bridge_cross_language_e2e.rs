use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::Arc;
use std::time::Duration;
use astralparty_hub_lib::xing_shu::bridge::server::BridgeServer;

fn get_csharp_runner_path() -> PathBuf {
    let manifest_dir = std::env::var("CARGO_MANIFEST_DIR").unwrap_or_else(|_| ".".into());
    let candidate1 = Path::new(&manifest_dir).join("../../APX-Dev/build/bin/Release/BridgeTestRunner.exe");
    let candidate2 = PathBuf::from(r"E:\工作流\星引擎插件\APX-Dev\build\bin\Release\BridgeTestRunner.exe");
    let candidate3 = Path::new(&manifest_dir).join("../../APX-Dev/tools/BridgeTestRunner/bin/BridgeTestRunner.exe");

    for p in &[&candidate1, &candidate2, &candidate3] {
        if let Ok(c) = p.canonicalize() {
            if c.exists() {
                return c;
            }
        }
        if p.exists() {
            return (*p).clone();
        }
    }
    panic!("BridgeTestRunner.exe not found at {}", candidate1.display());
}

async fn run_csharp_runner(test_id: &str, endpoint: &str, challenge: &str) -> (i32, String) {
    let test_id = test_id.to_string();
    let endpoint = endpoint.to_string();
    let challenge = challenge.to_string();
    tokio::task::spawn_blocking(move || {
        let exe = get_csharp_runner_path();
        let mut cmd = Command::new(&exe);
        cmd.current_dir(exe.parent().unwrap())
            .arg("--test")
            .arg(&test_id)
            .arg("--endpoint")
            .arg(&endpoint)
            .arg("--pipe")
            .arg(&endpoint);
        if !challenge.is_empty() {
            cmd.arg("--challenge").arg(&challenge);
        }
        let output = cmd
            .output()
            .expect("Failed to execute BridgeTestRunner.exe");

        let exit_code = output.status.code().unwrap_or(-1);
        let stdout = String::from_utf8_lossy(&output.stdout).to_string();
        let stderr = String::from_utf8_lossy(&output.stderr).to_string();
        let combined = format!("STDOUT:\n{}\nSTDERR:\n{}", stdout, stderr);
        (exit_code, combined)
    })
    .await
    .expect("spawn_blocking failed")
}

// =========================================================================
// 1. HELLO / HELLO_ACK handshake
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_01_hello_handshake() {
    let _ = tracing_subscriber::fmt().with_test_writer().try_init();
    let server = BridgeServer::new_test();
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("Failed to start TCP server");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let ch = challenge.clone();
    let ep = endpoint.clone();
    let runner_handle = tokio::spawn(async move { run_csharp_runner("1", &ep, &ch).await });

    // Wait until C# client has connected and completed handshake
    for _ in 0..60 {
        if server.get_status().runtime_connected {
            break;
        }
        tokio::time::sleep(Duration::from_millis(50)).await;
    }

    let status = server.get_status();
    assert!(status.runtime_connected, "Server should register client as connected");
    assert_eq!(status.protocol_version, "0.1");
    assert!(status.total_messages_received >= 1);

    let (code, output) = runner_handle.await.expect("Runner task failed");
    println!("Test 1 Output:\n{}", output);

    assert_eq!(code, 0, "C# runner exited with failure code");
    assert!(output.contains("[PASS:HELLO_ACK_RECEIVED]"), "Missing PASS token in output");
    server.stop();
}

// =========================================================================
// 2. wrong protocol version reject (0.2 != 0.1)
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_02_wrong_protocol_version_rejected() {
    let server = BridgeServer::new_test();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let (code, output) = run_csharp_runner("2", &endpoint, "").await;
    println!("Test 2 Output:\n{}", output);

    assert_eq!(code, 0);
    assert!(output.contains("[PASS:WRONG_PROTOCOL_REJECTED]"));

    let status = server.get_status();
    assert!(!status.runtime_connected, "runtime_connected must remain FALSE on wrong protocol");
    server.stop();
}

// =========================================================================
// 3. malformed HELLO payload reject
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_03_malformed_hello_payload_rejected() {
    let server = BridgeServer::new_test();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let (code, output) = run_csharp_runner("3", &endpoint, "").await;
    println!("Test 3 Output:\n{}", output);

    assert_eq!(code, 0);
    assert!(output.contains("[PASS:MALFORMED_HELLO_REJECTED]"));

    let status = server.get_status();
    assert!(!status.runtime_connected, "runtime_connected must remain FALSE on malformed hello");
    server.stop();
}

// =========================================================================
// 4. fake PID spoofing reject (claimed PID != actual peer PID)
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_04_fake_pid_spoofing_rejected() {
    let server = BridgeServer::new_test();
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let (code, output) = run_csharp_runner("4", &endpoint, &challenge).await;
    println!("Test 4 Output:\n{}", output);

    assert_eq!(code, 0);
    assert!(output.contains("[PASS:PID_SPOOFING_REJECTED]"));

    let status = server.get_status();
    assert!(!status.runtime_connected, "runtime_connected must remain FALSE on PID spoofing");
    server.stop();
}

// =========================================================================
// 5. wrong session challenge reject
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_05_wrong_session_challenge_rejected() {
    let server = BridgeServer::new_test();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let (code, output) = run_csharp_runner("5", &endpoint, "").await;
    println!("Test 5 Output:\n{}", output);

    assert_eq!(code, 0);
    assert!(output.contains("[PASS:WRONG_CHALLENGE_REJECTED]"));

    let status = server.get_status();
    assert!(!status.runtime_connected, "runtime_connected must remain FALSE on wrong challenge");
    server.stop();
}

// =========================================================================
// 6. missing required capabilities reject
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_06_missing_required_capabilities_rejected() {
    let server = BridgeServer::new_test();
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let (code, output) = run_csharp_runner("6", &endpoint, &challenge).await;
    println!("Test 6 Output:\n{}", output);

    assert_eq!(code, 0);
    assert!(output.contains("[PASS:MISSING_CAPABILITIES_REJECTED]"));

    let status = server.get_status();
    assert!(!status.runtime_connected, "runtime_connected must remain FALSE on missing capabilities");
    server.stop();
}

// =========================================================================
// 7. heartbeat ping-pong
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_07_heartbeat_ping_pong() {
    let server = BridgeServer::new_test();
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let (code, output) = run_csharp_runner("7", &endpoint, &challenge).await;
    println!("Test 7 Output:\n{}", output);

    assert_eq!(code, 0);
    assert!(output.contains("[PASS:HEARTBEAT_PING_PONG]"));

    let status = server.get_status();
    assert!(status.total_messages_received >= 2, "Expected at least 1 HELLO + 1 heartbeat");
    server.stop();
}

// =========================================================================
// 8. client disconnect & clean server state transition
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_08_client_disconnect_clean_server_state() {
    let server = BridgeServer::new_test();
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let ch = challenge.clone();
    let ep = endpoint.clone();
    let runner_handle = tokio::spawn(async move { run_csharp_runner("8", &ep, &ch).await });

    // Wait until connected
    for _ in 0..50 {
        if server.get_status().runtime_connected {
            break;
        }
        tokio::time::sleep(Duration::from_millis(50)).await;
    }
    assert!(server.get_status().runtime_connected, "Client must connect");

    let (code, output) = runner_handle.await.expect("Runner failed");
    println!("Test 8 Output:\n{}", output);
    assert_eq!(code, 0);
    assert!(output.contains("[PASS:CLIENT_DISCONNECT_CLEAN_STATE]"));

    // Server should transition to Disconnected
    for _ in 0..60 {
        if !server.get_status().runtime_connected {
            break;
        }
        tokio::time::sleep(Duration::from_millis(50)).await;
    }
    assert!(!server.get_status().runtime_connected, "Hub must observe client disconnect");
    assert_eq!(server.active_generation(), 0, "active_generation must reset to 0");
    server.stop();
}

// =========================================================================
// 9. client reconnect & new session establishment
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_09_client_reconnect_new_session() {
    let server = BridgeServer::new_test();
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let (code, output) = run_csharp_runner("9", &endpoint, &challenge).await;
    println!("Test 9 Output:\n{}", output);

    assert_eq!(code, 0);
    assert!(output.contains("[PASS:CLIENT_RECONNECT_NEW_SESSION]"));
    assert_eq!(server.get_next_generation(), 2, "Hub should have advanced generation twice");
    server.stop();
}

// =========================================================================
// 10. Hub restart recovery (Hub down -> client backoff retry -> Hub up -> successful reconnect)
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_10_hub_restart_recovery() {
    let server1 = BridgeServer::new_test();
    let challenge = server1.session_challenge();
    let local_addr = server1.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let ch = challenge.clone();
    let ep = endpoint.clone();
    let runner_handle = tokio::spawn(async move { run_csharp_runner("10", &ep, &ch).await });

    // Wait until connected to server 1
    for _ in 0..60 {
        if server1.get_status().runtime_connected {
            break;
        }
        tokio::time::sleep(Duration::from_millis(50)).await;
    }
    assert!(server1.get_status().runtime_connected, "Client must connect to server 1");
    tokio::time::sleep(Duration::from_millis(200)).await;

    // Abruptly stop server 1
    server1.stop();
    drop(server1);
    tokio::time::sleep(Duration::from_millis(500)).await;

    // Start server 2 on same address with same challenge
    let server2 = BridgeServer::new_test();
    server2.set_session_challenge(&challenge);
    let mut rebind_res = Err(String::new());
    for _ in 0..15 {
        rebind_res = server2.start_tcp(&endpoint).await;
        if rebind_res.is_ok() {
            break;
        }
        tokio::time::sleep(Duration::from_millis(100)).await;
    }
    assert!(rebind_res.is_ok(), "Server 2 must be able to bind to same port: {:?}", rebind_res);

    // Wait for client to reconnect to server 2
    let mut reconnected = false;
    for _ in 0..120 {
        let status = server2.get_status();
        if status.runtime_connected || status.total_messages_received > 0 {
            reconnected = true;
            break;
        }
        tokio::time::sleep(Duration::from_millis(50)).await;
    }
    assert!(reconnected, "Client must reconnect to server 2");

    let (code, output) = runner_handle.await.expect("Runner failed");
    println!("Test 10 Output:\n{}", output);
    assert_eq!(code, 0);
    assert!(output.contains("[PASS:HUB_RESTART_RECOVERY]"));
    server2.stop();
}

// =========================================================================
// 11. Runtime process restart simulation (Process 1 -> Process 2)
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_11_runtime_process_restart_simulation() {
    let server = Arc::new(BridgeServer::new_test());
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let mut rx = server.subscribe_spectator_snapshots();

    // Process 1 (Gen 1)
    let ch1 = challenge.clone();
    let ep1 = endpoint.clone();
    let runner1 = tokio::spawn(async move { run_csharp_runner("11_proc1", &ep1, &ch1).await });

    let snap_p1 = tokio::time::timeout(Duration::from_secs(6), rx.recv())
        .await
        .expect("Timeout waiting for Process 1 snapshot")
        .expect("Broadcast channel closed");
    assert_eq!(snap_p1.spectator_session_epoch, 3);
    assert_eq!(snap_p1.sequence_number, 20);

    let (code1, output1) = runner1.await.expect("runner1 failed");
    assert_eq!(code1, 0);
    assert!(output1.contains("[PASS:PROC1_COMPLETED]"));

    // Wait for disconnect
    for _ in 0..60 {
        if !server.get_status().runtime_connected {
            break;
        }
        tokio::time::sleep(Duration::from_millis(50)).await;
    }
    assert!(!server.get_status().runtime_connected);
    assert_eq!(server.active_generation(), 0);

    // Process 2 (Gen 2)
    let ch2 = challenge.clone();
    let ep2 = endpoint.clone();
    let runner2 = tokio::spawn(async move { run_csharp_runner("11_proc2", &ep2, &ch2).await });

    let snap_p2_1 = tokio::time::timeout(Duration::from_secs(6), rx.recv())
        .await
        .expect("Timeout waiting for Process 2 epoch 1 seq 1")
        .expect("Broadcast channel closed");
    assert_eq!(snap_p2_1.spectator_session_epoch, 1);
    assert_eq!(snap_p2_1.sequence_number, 1);

    let snap_p2_2 = tokio::time::timeout(Duration::from_secs(6), rx.recv())
        .await
        .expect("Timeout waiting for Process 2 epoch 1 seq 2")
        .expect("Broadcast channel closed");
    assert_eq!(snap_p2_2.spectator_session_epoch, 1);
    assert_eq!(snap_p2_2.sequence_number, 2);

    let (code2, output2) = runner2.await.expect("runner2 failed");
    println!("Test 11 Output:\n{}", output2);
    assert_eq!(code2, 0);
    assert!(output2.contains("[PASS:RUNTIME_PROCESS_RESTART_SIMULATION]"));
    server.stop();
}

// =========================================================================
// 12. Gen 1 epoch 3 -> disconnect -> Gen 2 epoch 1 (monotonic generation reset)
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_12_gen1_epoch3_disconnect_gen2_epoch1_monotonic_generation_reset() {
    let server = Arc::new(BridgeServer::new_test());
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let mut rx = server.subscribe_spectator_snapshots();
    let ch = challenge.clone();
    let ep = endpoint.clone();
    let runner_handle = tokio::spawn(async move { run_csharp_runner("12", &ep, &ch).await });

    // Receive Gen 1 epoch 3 seq 5
    let s1 = tokio::time::timeout(Duration::from_secs(6), rx.recv()).await.unwrap().unwrap();
    assert_eq!(s1.spectator_session_epoch, 3);
    assert_eq!(s1.sequence_number, 5);

    // Receive Gen 2 epoch 1 seq 1
    let s2 = tokio::time::timeout(Duration::from_secs(8), rx.recv()).await.unwrap().unwrap();
    assert_eq!(s2.spectator_session_epoch, 1);
    assert_eq!(s2.sequence_number, 1);

    let (code, output) = runner_handle.await.expect("runner failed");
    println!("Test 12 Output:\n{}", output);
    assert_eq!(code, 0);
    assert!(output.contains("[PASS:GENERATION_RESET_VERIFIED]"));
    server.stop();
}

// =========================================================================
// 13. stale generation snapshot reject
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_13_stale_generation_snapshot_rejected() {
    let server = Arc::new(BridgeServer::new_test());
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let mut rx = server.subscribe_spectator_snapshots();
    let ch = challenge.clone();
    let ep = endpoint.clone();
    let runner_handle = tokio::spawn(async move { run_csharp_runner("13", &ep, &ch).await });

    // Receive valid epoch 2 seq 5
    let s1 = tokio::time::timeout(Duration::from_secs(6), rx.recv()).await.unwrap().unwrap();
    assert_eq!(s1.spectator_session_epoch, 2);
    assert_eq!(s1.sequence_number, 5);

    // Stale snapshot epoch 1 seq 99 must be dropped!
    tokio::time::sleep(Duration::from_millis(500)).await;
    assert!(rx.try_recv().is_err(), "Stale epoch 1 snapshot must be dropped by Hub");

    let (code, output) = runner_handle.await.expect("runner failed");
    println!("Test 13 Output:\n{}", output);
    assert_eq!(code, 0);
    assert!(output.contains("[PASS:STALE_GENERATION_SNAPSHOT_REJECTED]"));
    server.stop();
}

// =========================================================================
// 14. SPECTATOR_HAND_SNAPSHOT transmission & receipt
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_14_spectator_hand_snapshot_transmission_receipt() {
    let server = Arc::new(BridgeServer::new_test());
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let mut rx = server.subscribe_spectator_snapshots();
    let ch = challenge.clone();
    let ep = endpoint.clone();
    let runner_handle = tokio::spawn(async move { run_csharp_runner("14_snap", &ep, &ch).await });

    let snap = tokio::time::timeout(Duration::from_secs(6), rx.recv()).await.unwrap().unwrap();
    assert_eq!(snap.schema_version, "1.1.0");
    assert_eq!(snap.spectator_session_epoch, 1);
    assert_eq!(snap.sequence_number, 1);
    assert_eq!(snap.room_players.len(), 4);
    assert!(snap.room_players.contains_key("1668707"));
    assert!(snap.room_players.contains_key("2081758"));
    assert!(snap.room_players.contains_key("1681134"));
    assert!(snap.room_players.contains_key("2156674"));

    let (code, output) = runner_handle.await.expect("runner failed");
    println!("Test 14 Output:\n{}", output);
    assert_eq!(code, 0);
    assert!(output.contains("[PASS:SPECTATOR_HAND_SNAPSHOT_TRANSMITTED]"));
    server.stop();
}

// =========================================================================
// 15. invalid schema version reject
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_15_invalid_schema_version_rejected() {
    let server = Arc::new(BridgeServer::new_test());
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let mut rx = server.subscribe_spectator_snapshots();
    let (code, output) = run_csharp_runner("15", &endpoint, &challenge).await;
    println!("Test 15 Output:\n{}", output);
    assert_eq!(code, 0);
    assert!(output.contains("[PASS:INVALID_SCHEMA_VERSION_REJECTED]"));

    tokio::time::sleep(Duration::from_millis(300)).await;
    assert!(rx.try_recv().is_err(), "Invalid schemaVersion must be dropped");
    assert!(server.get_latest_spectator_snapshot().is_none());
    server.stop();
}

// =========================================================================
// 16. sequence number monotonicity enforcement
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_16_sequence_number_monotonicity_enforcement() {
    let server = Arc::new(BridgeServer::new_test());
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let mut rx = server.subscribe_spectator_snapshots();
    let ch = challenge.clone();
    let ep = endpoint.clone();
    let runner_handle = tokio::spawn(async move { run_csharp_runner("16", &ep, &ch).await });

    let s1 = tokio::time::timeout(Duration::from_secs(6), rx.recv()).await.unwrap().unwrap();
    assert_eq!(s1.sequence_number, 1);

    let s2 = tokio::time::timeout(Duration::from_secs(6), rx.recv()).await.unwrap().unwrap();
    assert_eq!(s2.sequence_number, 2);

    let s3 = tokio::time::timeout(Duration::from_secs(6), rx.recv()).await.unwrap().unwrap();
    assert_eq!(s3.sequence_number, 3);

    tokio::time::sleep(Duration::from_millis(300)).await;
    assert!(rx.try_recv().is_err(), "Duplicate and stale seq must be dropped");

    let (code, output) = runner_handle.await.expect("runner failed");
    println!("Test 16 Output:\n{}", output);
    assert_eq!(code, 0);
    assert!(output.contains("[PASS:SEQUENCE_MONOTONICITY_ENFORCED]"));
    server.stop();
}

// =========================================================================
// 17. oversized frame (> 64 KiB) rejection & connection abort
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_17_oversized_frame_rejection_connection_abort() {
    // 17a: Client sends oversized frame (65537 bytes) -> Server aborts immediately without unbounded allocation
    let server_a = BridgeServer::new_test();
    let local_addr_a = server_a.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let ep_a = local_addr_a.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let (code_a, output_a) = run_csharp_runner("17_client_oversized", &ep_a, "").await;
    println!("Test 17a Output:\n{}", output_a);
    assert_eq!(code_a, 0);
    assert!(output_a.contains("[PASS:OVERSIZED_FRAME_REJECTED]"));
    assert!(!server_a.get_status().runtime_connected);
    server_a.stop();

    // 17b: Mock server sends oversized frame (65537 bytes) -> Client LoopbackTcpTransport aborts
    use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
    use tokio::net::TcpListener;

    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let local_addr_b = listener.local_addr().unwrap();
    let ep_b = local_addr_b.to_string();

    tokio::spawn(async move {
        let (socket, _) = listener.accept().await.unwrap();
        let (r, mut w) = socket.into_split();
        let mut buf_r = BufReader::new(r);
        let mut hello = String::new();
        buf_r.read_line(&mut hello).await.unwrap();

        let ack = "{\"protocolVersion\":\"0.1\",\"messageType\":\"HELLO_ACK\",\"requestId\":\"ack-1\",\"timestamp\":100,\"payload\":{\"status\":\"ACCEPTED\",\"hubVersion\":\"0.1.0\",\"protocolVersion\":\"0.1\",\"heartbeatIntervalMs\":3000}}\n";
        w.write_all(ack.as_bytes()).await.unwrap();
        w.flush().await.unwrap();

        tokio::time::sleep(Duration::from_millis(100)).await;
        let oversized = vec![b'Z'; 65537];
        let _ = w.write_all(&oversized).await;
        let _ = w.flush().await;
    });

    let (code_b, output_b) = run_csharp_runner("17_hub_oversized", &ep_b, "").await;
    println!("Test 17b Output:\n{}", output_b);
    assert_eq!(code_b, 0);
    assert!(output_b.contains("[PASS:HUB_OVERSIZED_FRAME_REJECTED]"));
}

// =========================================================================
// 18. non-loopback bind address rejection (0.0.0.0 fails closed)
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_18_non_loopback_bind_address_rejection() {
    let server = BridgeServer::new_test();

    // 1. Attempt to bind 0.0.0.0:25890 -> MUST fail closed
    let res_all = server.start_tcp("0.0.0.0:25890").await;
    assert!(res_all.is_err(), "Binding to 0.0.0.0 must be rejected (FAIL CLOSED)");
    let err_msg = res_all.err().unwrap();
    assert!(err_msg.contains("Prohibited bind address") || err_msg.contains("127.0.0.1"));

    // 2. Attempt to bind external IP 192.168.1.1:25890 -> MUST fail closed
    let res_ext = server.start_tcp("192.168.1.1:25890").await;
    assert!(res_ext.is_err(), "Binding to 192.168.1.1 must be rejected (FAIL CLOSED)");

    // 3. validate_tcp_bind_addr unit check
    assert!(astralparty_hub_lib::xing_shu::bridge::server::validate_tcp_bind_addr("0.0.0.0:25890").is_err());
    assert!(astralparty_hub_lib::xing_shu::bridge::server::validate_tcp_bind_addr("192.168.1.100:25890").is_err());
    assert!(astralparty_hub_lib::xing_shu::bridge::server::validate_tcp_bind_addr("127.0.0.1:25890").is_ok());

    println!("[PASS:NON_LOOPBACK_BIND_REJECTED] 0.0.0.0 and external IP binding strictly rejected");
}

// =========================================================================
// 19. Spectator Offline Replay Pipeline (Evidence JSONL replay)
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_19_spectator_offline_replay_pipeline() {
    let _ = tracing_subscriber::fmt().with_test_writer().try_init();
    let server = Arc::new(BridgeServer::new_test());
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    // Subscribe to spectator snapshots on the Hub side
    let mut rx = server.subscribe_spectator_snapshots();

    // Spawn C# runner for test 13
    let ch = challenge.clone();
    let ep = endpoint.clone();
    let runner_handle = tokio::spawn(async move {
        run_csharp_runner("spectator_offline_replay", &ep, &ch).await
    });

    // Locate and load expected evidence records from JSONL to verify raw cards at each step
    let manifest_dir = std::env::var("CARGO_MANIFEST_DIR").unwrap_or_else(|_| ".".into());
    let candidate1 = Path::new(&manifest_dir).join("../../APX-Dev/tools/spectator_telemetry/evidence_live_hot_spectator_session_proven_20261016.jsonl");
    let candidate2 = PathBuf::from(r"E:\工作流\星引擎插件\APX-Dev\tools\spectator_telemetry\evidence_live_hot_spectator_session_proven_20261016.jsonl");
    let evidence_path = if candidate1.exists() { candidate1 } else { candidate2 };
    let evidence_content = std::fs::read_to_string(&evidence_path).expect("Failed to read evidence JSONL");

    let mut expected_snapshots = Vec::new();
    for line in evidence_content.lines() {
        let clean = line.trim();
        if clean.is_empty() { continue; }
        if let Ok(v) = serde_json::from_str::<serde_json::Value>(clean) {
            let evt_type = v.get("eventType").or_else(|| v.get("event")).and_then(|t| t.as_str()).unwrap_or("");
            if evt_type == "WatchRefreshRoomStateS2C" || evt_type == "RoomHeroCardChangeS2C" {
                let pid = v.get("playerId").and_then(|p| p.as_u64()).unwrap_or(0);
                let mut card_ids = Vec::new();
                if let Some(arr) = v.get("cardIds").and_then(|c| c.as_array()) {
                    for c in arr {
                        if let Some(num) = c.as_i64() {
                            card_ids.push(num as i32);
                        }
                    }
                }
                expected_snapshots.push((pid, card_ids));
            }
        }
    }
    assert_eq!(expected_snapshots.len(), 56, "Evidence file must contain exactly 56 snapshot events");

    // Phase 1: Verify all 56 offline replay snapshots
    let mut received_players = std::collections::HashSet::new();
    let mut last_seq = 0u64;

    for i in 0..56 {
        let snap = tokio::time::timeout(Duration::from_secs(6), rx.recv())
            .await
            .expect("Timeout waiting for spectator snapshot from C# runner")
            .expect("Broadcast channel closed");

        // 1. Verify monotonic sequence number (1, 2, 3...)
        assert_eq!(snap.sequence_number, (i + 1) as u64, "Sequence number must be strictly monotonic (1, 2, 3...)");
        assert!(snap.sequence_number > last_seq, "Sequence must be strictly increasing");
        last_seq = snap.sequence_number;

        // 2. Verify schema version and epoch
        assert_eq!(snap.schema_version, "1.1.0", "Schema version must be 1.1.0");
        assert_eq!(snap.spectator_session_epoch, 1, "Initial session epoch must be 1");
        assert_eq!(snap.room_id, 470179, "Room ID must match evidence");

        // 3. Verify cardIds match raw evidence at each step
        let (expected_pid, ref expected_cards) = expected_snapshots[i];
        assert_eq!(snap.player_id, expected_pid, "Step {}: Player ID mismatch", i + 1);
        assert_eq!(&snap.card_ids, expected_cards, "Step {}: Reconstructed cardIds mismatch", i + 1);
        assert_eq!(snap.hand_size, expected_cards.len(), "Step {}: handSize mismatch", i + 1);

        // Verify multiset counts invariant
        let sum_multiset: u32 = snap.cards.iter().map(|c| c.count).sum();
        assert_eq!(sum_multiset as usize, snap.hand_size, "Step {}: multiset sum must equal handSize", i + 1);

        received_players.insert(snap.player_id);
    }

    // Verify all 4 watched players received at Hub
    let required_players: [u64; 4] = [1668707, 2081758, 1681134, 2156674];
    for p in &required_players {
        assert!(received_players.contains(p), "Watched player {} must be received at Hub", p);
    }

    // Verify server has latest snapshot
    let latest_in_service = server.get_latest_spectator_snapshot().expect("Server must hold latest payload");
    assert_eq!(latest_in_service.sequence_number, 56);
    assert_eq!(latest_in_service.spectator_session_epoch, 1);

    // Phase 2: Verify stale/duplicate packet drop (seq <= highest or epoch < active)
    let ep2_snap1 = tokio::time::timeout(Duration::from_secs(6), rx.recv())
        .await
        .expect("Timeout waiting for epoch 2 seq 1 snapshot")
        .expect("Broadcast channel closed");
    assert_eq!(ep2_snap1.spectator_session_epoch, 2, "Expected epoch 2 advance");
    assert_eq!(ep2_snap1.sequence_number, 1, "Expected seq 1 for new epoch");

    let ep2_snap2 = tokio::time::timeout(Duration::from_secs(6), rx.recv())
        .await
        .expect("Timeout waiting for epoch 2 seq 2 snapshot")
        .expect("Broadcast channel closed");
    assert_eq!(ep2_snap2.spectator_session_epoch, 2);
    assert_eq!(ep2_snap2.sequence_number, 2);

    assert_eq!(server.get_spectator_epoch_and_sequence(), (2, 2));

    // Phase 3: Bridge disconnect & reconnect test (new session does not contaminate old epoch)
    let ep3_snap1 = tokio::time::timeout(Duration::from_secs(8), rx.recv())
        .await
        .expect("Timeout waiting for epoch 3 seq 1 snapshot after reconnect")
        .expect("Broadcast channel closed");
    assert_eq!(ep3_snap1.spectator_session_epoch, 3, "New session must be epoch 3");
    assert_eq!(ep3_snap1.sequence_number, 1);

    let ep3_snap2 = tokio::time::timeout(Duration::from_secs(6), rx.recv())
        .await
        .expect("Timeout waiting for epoch 3 seq 2 snapshot")
        .expect("Broadcast channel closed");
    assert_eq!(ep3_snap2.spectator_session_epoch, 3);
    assert_eq!(ep3_snap2.sequence_number, 2);

    let final_payload = server.get_latest_spectator_snapshot().expect("Final payload must exist");
    assert_eq!(final_payload.spectator_session_epoch, 3);
    assert_eq!(final_payload.sequence_number, 2);
    assert_eq!(server.get_spectator_epoch_and_sequence(), (3, 2));

    let (code, output) = runner_handle.await.expect("C# runner task failed");
    println!("Test 19 C# Output:\n{}", output);
    assert_eq!(code, 0, "C# runner failed with exit code {}", code);
    assert!(output.contains("[PASS:SPECTATOR_OFFLINE_REPLAY_SUCCESS]"), "Missing PASS token in C# runner output");

    server.stop();
}

// =========================================================================
// 20. Test runner rejected under default production policy (allow_test_runners = false)
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_20_test_runner_production_policy_rejected() {
    let _ = tracing_subscriber::fmt().with_test_writer().try_init();
    // Default BridgeServer::new() enforces production policy (allow_test_runners: false)
    let server = BridgeServer::new();
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("Failed to start TCP server");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let ch = challenge.clone();
    let ep = endpoint.clone();
    let runner_handle = tokio::spawn(async move { run_csharp_runner("1", &ep, &ch).await });

    // Wait briefly
    tokio::time::sleep(Duration::from_millis(500)).await;

    // Server must NOT accept BridgeTestRunner.exe under production policy
    let status = server.get_status();
    assert!(!status.runtime_authenticated, "Production policy must NOT authenticate BridgeTestRunner.exe");

    let (code, output) = runner_handle.await.expect("Runner task failed");
    println!("Test 20 Output (Expected rejection):\n{}", output);
    assert_ne!(code, 0, "C# runner must exit with failure code under production policy");
    assert!(!output.contains("[PASS:HELLO_ACK_RECEIVED]"), "Production policy must not return HELLO_ACK to test runner");

    server.stop();
}

// =========================================================================
// 21. Test runner accepted under explicit test policy (allow_test_runners = true)
// =========================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_case_21_test_runner_explicit_test_policy_accepted() {
    let _ = tracing_subscriber::fmt().with_test_writer().try_init();
    // Explicitly configure test policy (allow_test_runners: true)
    let server = BridgeServer::new_test();
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("Failed to start TCP server");
    let endpoint = local_addr.to_string();
    tokio::time::sleep(Duration::from_millis(50)).await;

    let ch = challenge.clone();
    let ep = endpoint.clone();
    let runner_handle = tokio::spawn(async move { run_csharp_runner("1", &ep, &ch).await });

    for _ in 0..60 {
        if server.get_status().runtime_connected {
            break;
        }
        tokio::time::sleep(Duration::from_millis(50)).await;
    }

    let status = server.get_status();
    assert!(status.runtime_connected, "Test policy must authenticate BridgeTestRunner.exe");

    let (code, output) = runner_handle.await.expect("Runner task failed");
    println!("Test 21 Output:\n{}", output);
    assert_eq!(code, 0, "C# runner must succeed under explicit test policy");
    assert!(output.contains("[PASS:HELLO_ACK_RECEIVED]"), "Missing PASS token in output");

    server.stop();
}

