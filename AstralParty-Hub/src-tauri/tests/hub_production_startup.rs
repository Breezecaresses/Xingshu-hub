// -----------------------------------------------------------------------
// PROJECT XINGSHU — HS3.2.2b Production Hub Startup Integration Tests
// Verifies automatic TCP binding, fail-closed behavior on port conflict,
// lifecycle ownership, idempotency, and port release upon stop/restart.
// -----------------------------------------------------------------------

use std::net::SocketAddr;
use std::time::Duration;
use tokio::net::TcpStream;
use astralparty_hub_lib::xing_shu::bridge::protocol::{BridgeServerState, XINGSHU_BRIDGE_TCP_ADDR};
use astralparty_hub_lib::xing_shu::bridge::server::BridgeServer;

// Mutex to serialize tests binding to the fixed production port 127.0.0.1:25890
static STARTUP_TEST_MUTEX: tokio::sync::Mutex<()> = tokio::sync::Mutex::const_new(());

#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn test_production_startup_automatically_listens_tcp_25890() {
    let _guard = STARTUP_TEST_MUTEX.lock().await;
    let server = BridgeServer::new_test();

    // Verify initial state: not started
    assert!(!server.is_tcp_started(), "TCP server must not be started initially");
    assert_eq!(server.tcp_local_addr(), None);

    // Call production start() — MUST automatically start TCP on 127.0.0.1:25890 and return Ok(())
    let start_res = server.start();
    assert!(start_res.is_ok(), "server.start() must return Ok(()): {:?}", start_res.err());

    // Verify TCP listener is started
    assert!(server.is_tcp_started(), "server.start() must automatically start TCP listener");
    let expected_addr: SocketAddr = XINGSHU_BRIDGE_TCP_ADDR.parse().unwrap();
    assert_eq!(server.tcp_local_addr(), Some(expected_addr), "TCP listener must bind to 127.0.0.1:25890");

    // Verify a TCP connection can be established to 127.0.0.1:25890
    let conn = TcpStream::connect(expected_addr).await;
    assert!(conn.is_ok(), "Must successfully connect to production TCP listener on 127.0.0.1:25890: {:?}", conn.err());

    // Verify shutdown releases listener and port
    server.stop();
    assert!(!server.is_tcp_started(), "server.stop() must mark TCP server as not started");
    assert_eq!(server.tcp_local_addr(), None);
}

#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn test_production_startup_idempotent() {
    let _guard = STARTUP_TEST_MUTEX.lock().await;
    let server = BridgeServer::new_test();

    // Calling start() multiple times must return Ok(()) without error or port collision
    let res1 = server.start();
    assert!(res1.is_ok(), "First start() must succeed");
    assert!(server.is_tcp_started());

    // Second start call must return Ok(()) idempotently (not fail on second bind)
    let res2 = server.start();
    assert!(res2.is_ok(), "Second redundant start() call must return Ok(()) idempotently");
    assert!(server.is_tcp_started());

    let expected_addr: SocketAddr = XINGSHU_BRIDGE_TCP_ADDR.parse().unwrap();
    let conn = TcpStream::connect(expected_addr).await;
    assert!(conn.is_ok(), "Connection must succeed after redundant start() calls");

    server.stop();
}

#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn test_production_restart_releases_and_rebinds_port() {
    let _guard = STARTUP_TEST_MUTEX.lock().await;
    let server = BridgeServer::new_test();

    // First cycle: start and stop
    let res1 = server.start();
    assert!(res1.is_ok(), "Initial start must succeed");
    assert!(server.is_tcp_started());
    let expected_addr: SocketAddr = XINGSHU_BRIDGE_TCP_ADDR.parse().unwrap();
    let conn1 = TcpStream::connect(expected_addr).await;
    assert!(conn1.is_ok());
    server.stop();
    tokio::time::sleep(Duration::from_millis(50)).await;

    // Second cycle: re-start server on the same port
    let res2 = server.start();
    assert!(res2.is_ok(), "Server must successfully re-bind port 25890 after stop(): {:?}", res2.err());
    assert!(server.is_tcp_started());
    assert_eq!(server.tcp_local_addr(), Some(expected_addr));
    let conn2 = TcpStream::connect(expected_addr).await;
    assert!(conn2.is_ok(), "Connection must succeed after restart on port 25890");

    server.stop();
}

#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn test_production_startup_fails_closed_on_tcp_port_conflict() {
    let _guard = STARTUP_TEST_MUTEX.lock().await;

    // Step 1: Pre-occupy 127.0.0.1:25890 with external listener
    let squat_addr: SocketAddr = XINGSHU_BRIDGE_TCP_ADDR.parse().unwrap();
    let squatter = std::net::TcpListener::bind(squat_addr).expect("Pre-occupy 127.0.0.1:25890 must succeed");

    // Step 2: Call real production startup
    let server = BridgeServer::new();
    let start_res = server.start();

    // Step 3: Must return failure (Err)
    assert!(
        start_res.is_err(),
        "Production startup MUST return Err when port 25890 is pre-occupied, got Ok"
    );
    let err_str = start_res.err().unwrap();
    assert!(
        err_str.contains("Port squatting detected") || err_str.contains("Failed to start production TCP listener"),
        "Error message must indicate TCP binding failure: {}",
        err_str
    );

    // Step 4: Bridge must NOT enter Ready
    assert_eq!(
        server.connection_state(),
        BridgeServerState::Disconnected,
        "Bridge connection state must remain Disconnected upon startup failure"
    );
    assert!(!server.is_tcp_started(), "is_tcp_started() must remain false");

    // Step 5: Must NOT leave orphaned Named Pipe or TCP listener running
    assert_eq!(
        server.tcp_local_addr(),
        None,
        "tcp_local_addr() must be None after startup failure"
    );

    // Step 6: Release port and restart — MUST succeed
    drop(squatter);
    tokio::time::sleep(Duration::from_millis(50)).await;

    let restart_res = server.start();
    assert!(
        restart_res.is_ok(),
        "Production startup must succeed after conflicting port is released: {:?}",
        restart_res.err()
    );
    assert!(server.is_tcp_started(), "Server must be running after clean restart");
    assert_eq!(server.tcp_local_addr(), Some(squat_addr));

    let conn = TcpStream::connect(squat_addr).await;
    assert!(conn.is_ok(), "Connection to restarted server must succeed");

    server.stop();
}
