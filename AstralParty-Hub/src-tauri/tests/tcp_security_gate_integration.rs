// -----------------------------------------------------------------------
// PROJECT XINGSHU — HS3.2.2a 5-Layer Identity Gate Integration Tests
// Verifies all 6 security rejection/acceptance scenarios against real
// TCP connections to BridgeServer:
// 1. Same-user wrong executable reject
// 2. Correct PID but wrong executable reject
// 3. Correct executable name but wrong path reject
// 4. Fake HELLO PID reject
// 5. Test runner production policy reject
// 6. Test runner explicit test policy accept
// -----------------------------------------------------------------------

use std::time::Duration;
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::net::TcpStream;
use astralparty_hub_lib::xing_shu::bridge::protocol::{
    error_codes, message_types, BridgeEnvelope, BridgeServerState, HelloPayload,
};
use astralparty_hub_lib::xing_shu::bridge::server::BridgeServer;
use astralparty_hub_lib::xing_shu::bridge::tcp_security::SecurityVerificationPolicy;

/// Helper to construct a standard valid HELLO envelope
fn build_hello_envelope(
    game_pid: u32,
    challenge: String,
) -> BridgeEnvelope<serde_json::Value> {
    let hello = HelloPayload {
        component: Some("XingShu_Runtime".into()),
        runtime_version: Some("1.0.0".into()),
        game_pid: Some(game_pid),
        game_version: Some("1.3.8".into()),
        capabilities: Some(vec![
            "status".into(),
            "events".into(),
            "spectator_hand_snapshot".into(),
        ]),
        session_challenge: Some(challenge),
        ..Default::default()
    };
    BridgeEnvelope::new(message_types::HELLO, serde_json::to_value(&hello).unwrap())
}

// =======================================================================
// Scenario 1 & 2: Same-user / Correct PID but wrong executable reject
// =======================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn test_scenario_01_02_correct_pid_wrong_executable_rejected() {
    let server = BridgeServer::new(); // Production policy: only AstralParty_CN.exe, allow_test_runners: false
    let mut custom_policy = SecurityVerificationPolicy::production();
    custom_policy.allowed_executables = vec!["AstralParty_CN.exe".into()];
    server.set_security_policy(custom_policy);

    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
    tokio::time::sleep(Duration::from_millis(50)).await;

    let client = TcpStream::connect(local_addr).await.expect("Connect to TCP");
    let (r, mut w) = client.into_split();
    let mut buf_r = BufReader::new(r);

    // Client sends its own real PID (so PID matches, user matches, session matches)
    let my_pid = std::process::id();
    let env = build_hello_envelope(my_pid, server.session_challenge());
    w.write_all(format!("{}\n", serde_json::to_string(&env).unwrap()).as_bytes()).await.unwrap();
    w.flush().await.unwrap();

    let mut line = String::new();
    buf_r.read_line(&mut line).await.unwrap();

    // Must be rejected with SECURITY_TRUST_VIOLATION because current executable is not AstralParty_CN.exe
    assert!(
        line.contains(error_codes::SECURITY_TRUST_VIOLATION),
        "Expected SECURITY_TRUST_VIOLATION for wrong executable, got: {}",
        line
    );
    assert!(line.contains("Disallowed executable image name"));

    tokio::time::sleep(Duration::from_millis(250)).await;
    assert_eq!(server.connection_state(), BridgeServerState::Disconnected);
    assert_eq!(server.active_generation(), 0);

    server.stop();
}

// =======================================================================
// Scenario 3: Correct executable name but wrong path reject
// =======================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn test_scenario_03_correct_executable_name_wrong_path_rejected() {
    let server = BridgeServer::new();

    // Configure policy: allow current executable name as game exe, but require a strict non-existent Steam folder
    let cur_name = std::env::current_exe()
        .unwrap()
        .file_name()
        .unwrap()
        .to_string_lossy()
        .to_string();

    let mut path_policy = SecurityVerificationPolicy::production();
    path_policy.allowed_executables = vec![cur_name];
    path_policy.allow_test_runners = false; // Must strictly match canonical Steam path
    path_policy.expected_steam_cn_path = Some(r"C:\NonExistentSteamFolder\8vJXn6CN\AstralParty_CN.exe".into());
    server.set_security_policy(path_policy);

    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
    tokio::time::sleep(Duration::from_millis(50)).await;

    let client = TcpStream::connect(local_addr).await.expect("Connect to TCP");
    let (r, mut w) = client.into_split();
    let mut buf_r = BufReader::new(r);

    let my_pid = std::process::id();
    let env = build_hello_envelope(my_pid, server.session_challenge());
    w.write_all(format!("{}\n", serde_json::to_string(&env).unwrap()).as_bytes()).await.unwrap();
    w.flush().await.unwrap();

    let mut line = String::new();
    buf_r.read_line(&mut line).await.unwrap();

    // Must be rejected with SECURITY_TRUST_VIOLATION due to path not matching approved Steam CN directory
    assert!(
        line.contains(error_codes::SECURITY_TRUST_VIOLATION),
        "Expected SECURITY_TRUST_VIOLATION for wrong path, got: {}",
        line
    );
    assert!(line.contains("does not match canonical Steam CN PlatformProfile path"));

    tokio::time::sleep(Duration::from_millis(250)).await;
    assert_eq!(server.connection_state(), BridgeServerState::Disconnected);
    server.stop();
}

// =======================================================================
// Scenario 4: Fake HELLO PID reject (claimed PID != actual peer PID)
// =======================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn test_scenario_04_fake_hello_pid_rejected() {
    let server = BridgeServer::new_test(); // Allow test runner so failure is specifically PID spoofing
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
    tokio::time::sleep(Duration::from_millis(50)).await;

    let client = TcpStream::connect(local_addr).await.expect("Connect to TCP");
    let (r, mut w) = client.into_split();
    let mut buf_r = BufReader::new(r);

    // Client sends a forged/fake PID (actual PID + 9999)
    let my_pid = std::process::id();
    let forged_pid = my_pid + 9999;
    let env = build_hello_envelope(forged_pid, server.session_challenge());
    w.write_all(format!("{}\n", serde_json::to_string(&env).unwrap()).as_bytes()).await.unwrap();
    w.flush().await.unwrap();

    let mut line = String::new();
    buf_r.read_line(&mut line).await.unwrap();

    // Must be rejected with PID_MISMATCH
    assert!(
        line.contains(error_codes::PID_MISMATCH),
        "Expected PID_MISMATCH for forged PID, got: {}",
        line
    );
    assert!(line.contains(&format!("does not match client process id {}", my_pid)));

    tokio::time::sleep(Duration::from_millis(250)).await;
    assert_eq!(server.connection_state(), BridgeServerState::Disconnected);
    server.stop();
}

// =======================================================================
// Scenario 5: Test runner production policy reject
// =======================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn test_scenario_05_test_runner_production_policy_rejected() {
    // Default BridgeServer::new() uses SecurityVerificationPolicy::production() (allow_test_runners = false)
    let server = BridgeServer::new();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
    tokio::time::sleep(Duration::from_millis(50)).await;

    let client = TcpStream::connect(local_addr).await.expect("Connect to TCP");
    let (r, mut w) = client.into_split();
    let mut buf_r = BufReader::new(r);

    let my_pid = std::process::id();
    let env = build_hello_envelope(my_pid, server.session_challenge());
    w.write_all(format!("{}\n", serde_json::to_string(&env).unwrap()).as_bytes()).await.unwrap();
    w.flush().await.unwrap();

    let mut line = String::new();
    buf_r.read_line(&mut line).await.unwrap();

    // Test runner must be rejected under production policy
    assert!(
        line.contains(error_codes::SECURITY_TRUST_VIOLATION),
        "Expected SECURITY_TRUST_VIOLATION under production policy, got: {}",
        line
    );
    assert_eq!(server.active_generation(), 0);

    tokio::time::sleep(Duration::from_millis(250)).await;
    assert_eq!(server.connection_state(), BridgeServerState::Disconnected);
    server.stop();
}

// =======================================================================
// Scenario 6: Test runner explicit test policy accept
// =======================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn test_scenario_06_test_runner_explicit_test_policy_accepted() {
    // Explicitly configure test policy (allow_test_runners = true)
    let server = BridgeServer::new_test();
    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
    tokio::time::sleep(Duration::from_millis(50)).await;

    let client = TcpStream::connect(local_addr).await.expect("Connect to TCP");
    let (r, mut w) = client.into_split();
    let mut buf_r = BufReader::new(r);

    let my_pid = std::process::id();
    let env = build_hello_envelope(my_pid, server.session_challenge());
    w.write_all(format!("{}\n", serde_json::to_string(&env).unwrap()).as_bytes()).await.unwrap();
    w.flush().await.unwrap();

    let mut line = String::new();
    buf_r.read_line(&mut line).await.unwrap();

    // Must be accepted with HELLO_ACK
    assert!(
        line.contains(message_types::HELLO_ACK) && line.contains("ACCEPTED"),
        "Expected HELLO_ACK ACCEPTED under test policy, got: {}",
        line
    );

    tokio::time::sleep(Duration::from_millis(50)).await;
    assert_eq!(server.active_generation(), 1);
    assert_eq!(server.connection_state(), BridgeServerState::RuntimeAuthenticated);

    server.stop();
}

// =======================================================================
// Scenario 7: Exact canonical path accept
// =======================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn test_scenario_07_exact_canonical_path_accepted() {
    let server = BridgeServer::new();
    let cur_exe = std::env::current_exe().unwrap();
    let cur_name = cur_exe.file_name().unwrap().to_string_lossy().to_string();

    let mut policy = SecurityVerificationPolicy::production();
    policy.allowed_executables = vec![cur_name];
    policy.allow_test_runners = false; // Strictly requires exact game path match
    policy.expected_steam_cn_path = Some(cur_exe);
    server.set_security_policy(policy);

    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
    tokio::time::sleep(Duration::from_millis(50)).await;

    let client = TcpStream::connect(local_addr).await.expect("Connect to TCP");
    let (r, mut w) = client.into_split();
    let mut buf_r = BufReader::new(r);

    let my_pid = std::process::id();
    let env = build_hello_envelope(my_pid, server.session_challenge());
    w.write_all(format!("{}\n", serde_json::to_string(&env).unwrap()).as_bytes()).await.unwrap();
    w.flush().await.unwrap();

    let mut line = String::new();
    buf_r.read_line(&mut line).await.unwrap();

    assert!(
        line.contains(message_types::HELLO_ACK) && line.contains("ACCEPTED"),
        "Expected HELLO_ACK ACCEPTED for exact canonical path match, got: {}",
        line
    );

    tokio::time::sleep(Duration::from_millis(50)).await;
    assert_eq!(server.active_generation(), 1);
    assert_eq!(server.connection_state(), BridgeServerState::RuntimeAuthenticated);

    server.stop();
}

// =======================================================================
// Scenario 8: Fake C:\Temp directory path rejected
// =======================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn test_scenario_08_fake_temp_directory_path_rejected() {
    let server = BridgeServer::new();
    let cur_exe = std::env::current_exe().unwrap();
    let cur_name = cur_exe.file_name().unwrap().to_string_lossy().to_string();

    let mut policy = SecurityVerificationPolicy::production();
    policy.allowed_executables = vec![cur_name];
    policy.allow_test_runners = false;
    policy.expected_steam_cn_path = Some(r"C:\Temp\Astral Party\8vJXn6CN\AstralParty_CN.exe".into());
    server.set_security_policy(policy);

    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
    tokio::time::sleep(Duration::from_millis(50)).await;

    let client = TcpStream::connect(local_addr).await.expect("Connect to TCP");
    let (r, mut w) = client.into_split();
    let mut buf_r = BufReader::new(r);

    let my_pid = std::process::id();
    let env = build_hello_envelope(my_pid, server.session_challenge());
    w.write_all(format!("{}\n", serde_json::to_string(&env).unwrap()).as_bytes()).await.unwrap();
    w.flush().await.unwrap();

    let mut line = String::new();
    buf_r.read_line(&mut line).await.unwrap();

    assert!(
        line.contains(error_codes::SECURITY_TRUST_VIOLATION),
        "Expected SECURITY_TRUST_VIOLATION for fake temp directory path, got: {}",
        line
    );
    assert!(line.contains("does not match canonical Steam CN PlatformProfile path"));

    tokio::time::sleep(Duration::from_millis(250)).await;
    assert_eq!(server.connection_state(), BridgeServerState::Disconnected);
    server.stop();
}

// =======================================================================
// Scenario 9: Production policy missing PlatformProfile path rejected (FAIL CLOSED)
// =======================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn test_scenario_09_production_policy_missing_platform_profile_path_rejected() {
    let server = BridgeServer::new();
    let cur_exe = std::env::current_exe().unwrap();
    let cur_name = cur_exe.file_name().unwrap().to_string_lossy().to_string();

    let mut policy = SecurityVerificationPolicy::production();
    policy.allowed_executables = vec![cur_name];
    policy.allow_test_runners = false;
    policy.expected_steam_cn_path = None; // Missing / unresolvable PlatformProfile path
    server.set_security_policy(policy);

    let local_addr = server.start_tcp("127.0.0.1:0").await.expect("start_tcp succeeds");
    tokio::time::sleep(Duration::from_millis(50)).await;

    let client = TcpStream::connect(local_addr).await.expect("Connect to TCP");
    let (r, mut w) = client.into_split();
    let mut buf_r = BufReader::new(r);

    let my_pid = std::process::id();
    let env = build_hello_envelope(my_pid, server.session_challenge());
    w.write_all(format!("{}\n", serde_json::to_string(&env).unwrap()).as_bytes()).await.unwrap();
    w.flush().await.unwrap();

    let mut line = String::new();
    buf_r.read_line(&mut line).await.unwrap();

    assert!(
        line.contains(error_codes::SECURITY_TRUST_VIOLATION),
        "Expected SECURITY_TRUST_VIOLATION when PlatformProfile path is None, got: {}",
        line
    );
    assert!(line.contains("PlatformProfile Steam CN executable path cannot be resolved (FAIL CLOSED)"));

    tokio::time::sleep(Duration::from_millis(250)).await;
    assert_eq!(server.connection_state(), BridgeServerState::Disconnected);
    server.stop();
}

// =======================================================================
// Scenario 10: Path traversal and slash normalization
// =======================================================================
#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn test_scenario_10_path_traversal_and_slash_normalization() {
    use astralparty_hub_lib::xing_shu::bridge::tcp_security::normalize_path_for_comparison;
    use std::path::PathBuf;

    let canonical = PathBuf::from(r"E:\Steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN.exe");
    let traversal = PathBuf::from(r"E:/Steam/steamapps/common/../common/Astral Party/8vJXn6CN/AstralParty_CN.exe");
    let fake_temp = PathBuf::from(r"C:\Temp\Astral Party\8vJXn6CN\AstralParty_CN.exe");

    let norm_canonical = normalize_path_for_comparison(&canonical);
    let norm_traversal = normalize_path_for_comparison(&traversal);
    let norm_fake = normalize_path_for_comparison(&fake_temp);

    assert_eq!(norm_canonical, norm_traversal, "Traversal and forward slashes must normalize to canonical");
    assert_ne!(norm_canonical, norm_fake, "Fake path in C:\\Temp must not match canonical path");
}

