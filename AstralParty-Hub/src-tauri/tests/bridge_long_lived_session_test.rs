// -----------------------------------------------------------------------
// PROJECT XINGSHU — HS3.3.1 Long-Lived Session & Idle Stability Tests
// -----------------------------------------------------------------------

use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::Duration;
use tokio::io::AsyncWriteExt;
use tokio::net::TcpListener;
use astralparty_hub_lib::xing_shu::bridge::server::BridgeServer;

fn get_csharp_runner_path() -> PathBuf {
    let manifest_dir = std::env::var("CARGO_MANIFEST_DIR").unwrap_or_else(|_| ".".into());
    let candidate = Path::new(&manifest_dir).join("../../XingShu-Dev/build/bin/Release/BridgeTestRunner.exe");
    if candidate.exists() {
        return candidate.canonicalize().unwrap_or(candidate);
    }
    PathBuf::from(r"E:\工作流\星引擎插件\XingShu-Dev\build\bin\Release\BridgeTestRunner.exe")
}

async fn run_csharp_runner(test_id: &str, endpoint: &str, challenge: &str, extra_args: &[&str]) -> (i32, String) {
    let test_id = test_id.to_string();
    let endpoint = endpoint.to_string();
    let challenge = challenge.to_string();
    let extra = extra_args.iter().map(|s| s.to_string()).collect::<Vec<_>>();

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
        for a in &extra {
            cmd.arg(a);
        }
        let output = cmd.output().expect("Failed to execute BridgeTestRunner.exe");
        let exit_code = output.status.code().unwrap_or(-1);
        let stdout = String::from_utf8_lossy(&output.stdout).to_string();
        let stderr = String::from_utf8_lossy(&output.stderr).to_string();
        let combined = format!("STDOUT:\n{}\nSTDERR:\n{}", stdout, stderr);
        (exit_code, combined)
    })
    .await
    .expect("spawn_blocking failed")
}

// -----------------------------------------------------------------------
// Test 1: Blocked Reader Thread exits in <= 500ms upon Disconnect()
// -----------------------------------------------------------------------
#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn test_blocked_reader_shutdown_under_500ms() {
    let listener = TcpListener::bind("127.0.0.1:0").await.expect("bind failed");
    let local_addr = listener.local_addr().unwrap();

    let server_task = tokio::spawn(async move {
        let (mut socket, _) = listener.accept().await.expect("accept failed");
        // Keep socket open but send nothing; client thread is blocked on ReadLine()
        tokio::time::sleep(Duration::from_millis(1500)).await;
        let _ = socket.shutdown().await;
    });

    let (code, output) = run_csharp_runner("blocked_reader_shutdown", &local_addr.to_string(), "", &[]).await;
    println!("Blocked Reader Shutdown Output:\n{}", output);

    assert_eq!(code, 0, "Blocked reader test failed with non-zero exit code");
    assert!(output.contains("[PASS:BLOCKED_READER_SHUTDOWN_LE_500MS]"), "Missing PASS token");

    let _ = server_task.await;
}

// -----------------------------------------------------------------------
// Test 2: Framing Edge Cases (Partial frame idle, multi-frames, oversized)
// -----------------------------------------------------------------------
#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn test_framing_edge_cases_and_partial_frame_idle() {
    let listener = TcpListener::bind("127.0.0.1:0").await.expect("bind failed");
    let local_addr = listener.local_addr().unwrap();

    let server_task = tokio::spawn(async move {
        let (mut socket, _) = listener.accept().await.expect("accept failed");

        // 1. Send partial frame, idle for 500ms, then send rest of frame + \n
        socket.write_all(b"PART_A_").await.unwrap();
        socket.flush().await.unwrap();
        tokio::time::sleep(Duration::from_millis(500)).await;
        socket.write_all(b"PART_B\n").await.unwrap();
        socket.flush().await.unwrap();

        tokio::time::sleep(Duration::from_millis(50)).await;

        // 2. Send 3 multiple frames in single socket write chunk
        socket.write_all(b"MULTI_1\nMULTI_2\nMULTI_3\n").await.unwrap();
        socket.flush().await.unwrap();

        tokio::time::sleep(Duration::from_millis(50)).await;

        // 3. Send oversized frame (> 64 KiB)
        let oversized = vec![b'X'; 65540];
        socket.write_all(&oversized).await.unwrap();
        socket.write_all(b"\n").await.unwrap();
        socket.flush().await.unwrap();

        tokio::time::sleep(Duration::from_millis(500)).await;
        let _ = socket.shutdown().await;
    });

    let (code, output) = run_csharp_runner("framing_edge_cases", &local_addr.to_string(), "", &[]).await;
    println!("Framing Edge Cases Output:\n{}", output);

    assert_eq!(code, 0, "Framing edge cases failed with non-zero exit code");
    assert!(output.contains("[PASS:PARTIAL_FRAME_IDLE_PRESERVED]"), "Missing partial frame pass token");
    assert!(output.contains("[PASS:MULTIPLE_FRAMES_SINGLE_CHUNK]"), "Missing multi-frame pass token");
    assert!(output.contains("[PASS:OVERSIZED_FRAME_REJECTED]"), "Missing oversized frame rejection token");

    let _ = server_task.await;
}

// -----------------------------------------------------------------------
// Test 3: Idle 10s Long-Lived Session (No disconnect, generation unchanged)
// -----------------------------------------------------------------------
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_idle_10s_session_persists_without_disconnect() {
    let server = BridgeServer::new_test();
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();

    let ch = challenge.clone();
    let ep = endpoint.clone();
    let runner_handle = tokio::spawn(async move {
        run_csharp_runner("hold", &ep, &ch, &["--duration", "10"]).await
    });

    // Wait until C# client has connected and completed handshake
    for _ in 0..60 {
        if server.get_status().runtime_connected {
            break;
        }
        tokio::time::sleep(Duration::from_millis(50)).await;
    }

    assert!(server.get_status().runtime_connected, "Client should be connected");
    let initial_gen = server.active_generation();
    assert_eq!(initial_gen, 1, "Initial generation must be 1");

    // Sleep 10 seconds while client is connected
    println!("Verifying 10 seconds idle persistence...");
    tokio::time::sleep(Duration::from_secs(10)).await;

    // Verify still connected and generation unchanged
    assert!(server.get_status().runtime_connected, "Client MUST still be connected after 10s idle");
    assert_eq!(server.active_generation(), initial_gen, "Generation MUST remain unchanged during idle");

    let (code, output) = runner_handle.await.expect("Runner failed");
    println!("10s Hold Output:\n{}", output);
    assert_eq!(code, 0, "Runner hold exited with failure");
    assert!(output.contains("[PASS:HOLD_FINISHED]"), "Missing HOLD_FINISHED pass token");

    server.stop();
}

// -----------------------------------------------------------------------
// Test 4: Idle 30s Long-Lived Session (Heartbeats every 3s, Gen=1 throughout)
// -----------------------------------------------------------------------
#[tokio::test(flavor = "multi_thread", worker_threads = 4)]
async fn test_idle_30s_generation_unchanged_and_heartbeats() {
    let server = BridgeServer::new_test();
    let challenge = server.session_challenge();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp failed");
    let endpoint = local_addr.to_string();

    let ch = challenge.clone();
    let ep = endpoint.clone();
    let runner_handle = tokio::spawn(async move {
        run_csharp_runner("hold", &ep, &ch, &["--duration", "35"]).await
    });

    for _ in 0..60 {
        if server.get_status().runtime_connected {
            break;
        }
        tokio::time::sleep(Duration::from_millis(50)).await;
    }

    assert!(server.get_status().runtime_connected, "Client should be connected");
    let initial_gen = server.active_generation();
    assert_eq!(initial_gen, 1, "Initial generation must be 1");

    println!("Monitoring 30 seconds idle session...");
    let mut check_count = 0;
    while check_count < 30 {
        tokio::time::sleep(Duration::from_secs(1)).await;
        check_count += 1;
        assert_eq!(server.active_generation(), initial_gen, "Generation changed at second {}", check_count);
        assert!(server.get_status().runtime_connected, "Connection dropped at second {}", check_count);
    }

    // Verify heartbeats were received
    let total_msgs = server.get_status().total_messages_received;
    println!("Total messages received across 30s idle session: {}", total_msgs);
    assert!(total_msgs >= 10, "Expected at least 10 messages (HELLO + heartbeats), got {}", total_msgs);

    let (code, output) = runner_handle.await.expect("Runner failed");
    assert_eq!(code, 0, "Runner hold exited with failure");
    assert!(output.contains("[PASS:HOLD_FINISHED]"), "Missing HOLD_FINISHED pass token");

    println!("[PASS:IDLE_30S_GENERATION_STABLE] 30s session completely stable without generation advance or reconnects");
    server.stop();
}
