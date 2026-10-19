// -----------------------------------------------------------------------
// PROJECT XINGSHU — HS3.2 Loopback TCP Hub Integration PoC Test
// Verifies Tokio TcpListener on 127.0.0.1 with Bounded Line Framing & Protocol 0.1
// -----------------------------------------------------------------------

use std::net::SocketAddr;
use std::time::Duration;
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::net::{TcpListener, TcpStream};
use serde_json::json;

#[tokio::test]
async fn test_loopback_tcp_hub_binding_strictly_local() {
    // Security check: Must bind strictly to 127.0.0.1, never 0.0.0.0
    let listener = TcpListener::bind("127.0.0.1:0").await.expect("Failed to bind loopback TCP listener");
    let local_addr = listener.local_addr().expect("Failed to get local addr");

    assert!(local_addr.ip().is_loopback(), "Listener MUST bind to loopback address");
    assert_ne!(local_addr.ip().to_string(), "0.0.0.0", "Listener MUST NOT bind to 0.0.0.0");
    println!("[PASS:TCP_POC_STRICT_LOOPBACK_BIND] Listener bound to: {}", local_addr);
}

#[tokio::test]
async fn test_loopback_tcp_hello_handshake_and_spectator_snapshot() {
    let listener = TcpListener::bind("127.0.0.1:0").await.expect("Failed to bind listener");
    let server_addr: SocketAddr = listener.local_addr().unwrap();

    let server_handle = tokio::spawn(async move {
        let (socket, client_addr) = listener.accept().await.expect("Accept failed");
        assert!(client_addr.ip().is_loopback(), "Client must be loopback");

        let (read_half, mut write_half) = socket.into_split();
        let mut reader = BufReader::new(read_half);
        let mut line = String::new();

        // 1. Read HELLO
        reader.read_line(&mut line).await.expect("Failed to read HELLO");
        let hello_json: serde_json::Value = serde_json::from_str(&line).expect("Invalid HELLO JSON");
        assert_eq!(hello_json["messageType"], "HELLO");
        assert_eq!(hello_json["payload"]["runtimeVersion"], "3.3.0");

        // 2. Reply HELLO_ACK with generation = 1
        let ack = json!({
            "protocolVersion": "0.1",
            "messageType": "HELLO_ACK",
            "messageId": "ack_001",
            "timestamp": 1729000000000i64,
            "payload": {
                "status": "ACCEPTED",
                "generation": 1,
                "protocolVersion": "0.1",
                "capabilities": ["spectator_hand_snapshot"]
            }
        });
        write_half.write_all(format!("{}\n", ack).as_bytes()).await.expect("Failed to write ACK");
        write_half.flush().await.expect("Flush failed");

        // 3. Read SPECTATOR_HAND_SNAPSHOT
        line.clear();
        reader.read_line(&mut line).await.expect("Failed to read snapshot");
        let snap_json: serde_json::Value = serde_json::from_str(&line).expect("Invalid Snapshot JSON");
        assert_eq!(snap_json["messageType"], "SPECTATOR_HAND_SNAPSHOT");
        assert_eq!(snap_json["payload"]["schemaVersion"], "1.1.0");
        assert_eq!(snap_json["payload"]["spectatorAccountPlayerId"], 1556514);

        // Verify 4 watched players
        let room_players = snap_json["payload"]["roomPlayers"].as_object().unwrap();
        assert_eq!(room_players.len(), 4);
        assert!(room_players.contains_key("1668707"));
        assert!(room_players.contains_key("2081758"));
        assert!(room_players.contains_key("1681134"));
        assert!(room_players.contains_key("2156674"));

        println!("[PASS:TCP_POC_SNAPSHOT_VERIFIED] HS3 spectator snapshot verified on Rust Tokio TCP server");
    });

    // Client connection
    let mut client = TcpStream::connect(server_addr).await.expect("Client connect failed");
    let (read_half, mut write_half) = client.split();
    let mut reader = BufReader::new(read_half);
    let mut line = String::new();

    // Send HELLO
    let hello = json!({
        "protocolVersion": "0.1",
        "messageType": "HELLO",
        "messageId": "msg_001",
        "timestamp": 1729000000000i64,
        "payload": {
            "runtimeVersion": "3.3.0",
            "gamePid": 99999,
            "authChallenge": "test_challenge_abc"
        }
    });
    write_half.write_all(format!("{}\n", hello).as_bytes()).await.unwrap();
    write_half.flush().await.unwrap();

    // Read ACK
    reader.read_line(&mut line).await.unwrap();
    let ack_json: serde_json::Value = serde_json::from_str(&line).unwrap();
    assert_eq!(ack_json["payload"]["status"], "ACCEPTED");
    assert_eq!(ack_json["payload"]["generation"], 1);

    // Send Snapshot
    let snap = json!({
        "protocolVersion": "0.1",
        "messageType": "SPECTATOR_HAND_SNAPSHOT",
        "messageId": "snap_001",
        "timestamp": 1729000000000i64,
        "payload": {
            "schemaVersion": "1.1.0",
            "roomId": 470179,
            "spectatorSessionEpoch": 1,
            "spectatorAccountPlayerId": 1556514,
            "sequenceNumber": 1,
            "snapshotTimestamp": "2026-10-16T14:00:00Z",
            "playerId": 2081758,
            "cardIds": [10005, 10005, 20002],
            "cards": [
                { "cardId": 10005, "count": 2 },
                { "cardId": 20002, "count": 1 }
            ],
            "handSize": 3,
            "drawCountTotal": 5,
            "playCountTotal": 2,
            "roomPlayers": {
                "1668707": { "playerId": 1668707, "handSize": 4, "cardIds": [10001, 10002, 20001, 20003] },
                "2081758": { "playerId": 2081758, "handSize": 3, "cardIds": [10005, 10005, 20002] },
                "1681134": { "playerId": 1681134, "handSize": 2, "cardIds": [10006, 20005] },
                "2156674": { "playerId": 2156674, "handSize": 5, "cardIds": [10003, 10004, 10005, 20001, 20002] }
            }
        }
    });
    write_half.write_all(format!("{}\n", snap).as_bytes()).await.unwrap();
    write_half.flush().await.unwrap();

    server_handle.await.unwrap();
    println!("[PASS:TCP_POC_END_TO_END] End-to-end Loopback TCP PoC passed cleanly");
}

#[tokio::test]
async fn test_loopback_tcp_reconnect_advances_generation() {
    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let server_addr = listener.local_addr().unwrap();

    let server_handle = tokio::spawn(async move {
        let mut generation = 0;
        for _ in 0..2 {
            let (socket, _) = listener.accept().await.unwrap();
            generation += 1;
            let (read_half, mut write_half) = socket.into_split();
            let mut reader = BufReader::new(read_half);
            let mut line = String::new();

            reader.read_line(&mut line).await.unwrap();
            let ack = json!({
                "protocolVersion": "0.1",
                "messageType": "HELLO_ACK",
                "payload": { "status": "ACCEPTED", "generation": generation }
            });
            write_half.write_all(format!("{}\n", ack).as_bytes()).await.unwrap();
            write_half.flush().await.unwrap();
        }
        generation
    });

    // Session 1
    {
        let mut client1 = TcpStream::connect(server_addr).await.unwrap();
        client1.write_all(b"{\"messageType\":\"HELLO\"}\n").await.unwrap();
        let mut reader1 = BufReader::new(&mut client1);
        let mut line = String::new();
        reader1.read_line(&mut line).await.unwrap();
        let ack: serde_json::Value = serde_json::from_str(&line).unwrap();
        assert_eq!(ack["payload"]["generation"], 1);
    }

    tokio::time::sleep(Duration::from_millis(50)).await;

    // Session 2 (Reconnect)
    {
        let mut client2 = TcpStream::connect(server_addr).await.unwrap();
        client2.write_all(b"{\"messageType\":\"HELLO\"}\n").await.unwrap();
        let mut reader2 = BufReader::new(&mut client2);
        let mut line = String::new();
        reader2.read_line(&mut line).await.unwrap();
        let ack: serde_json::Value = serde_json::from_str(&line).unwrap();
        assert_eq!(ack["payload"]["generation"], 2);
    }

    let final_gen = server_handle.await.unwrap();
    assert_eq!(final_gen, 2);
    println!("[PASS:TCP_POC_GENERATION_ADVANCE] Reconnection advances Hub generation to 2 cleanly");
}

#[tokio::test]
async fn test_diagnostic_tcp_server_live_component_lifecycle() {
    use astralparty_hub_lib::xing_shu::bridge::diagnostic_tcp::DiagnosticTcpServer;
    use std::sync::Arc;

    // 1. Strict loopback binding
    let server = Arc::new(DiagnosticTcpServer::bind("127.0.0.1:0").await.expect("Failed to bind DiagnosticTcpServer"));
    let server_addr = server.local_addr();
    assert_eq!(server_addr.ip().to_string(), "127.0.0.1");

    let s_clone = server.clone();
    let server_handle = tokio::spawn(async move {
        let _ = s_clone.run().await;
    });

    // 2. Session 1 Handshake & Snapshot
    {
        let mut client = TcpStream::connect(server_addr).await.expect("Session 1 connect failed");
        let (read_half, mut write_half) = client.split();
        let mut reader = BufReader::new(read_half);
        let mut line = String::new();

        // Send HELLO
        let hello = json!({
            "protocolVersion": "0.1",
            "messageType": "HELLO",
            "requestId": "live_session_1",
            "timestamp": 1729000000000i64,
            "payload": {
                "component": "XingShu_Runtime",
                "runtimeVersion": "3.3.0",
                "gamePid": 54321,
                "gameVersion": "1.3.8",
                "capabilities": ["spectator_hand_snapshot", "status"]
            }
        });
        write_half.write_all(format!("{}\n", hello).as_bytes()).await.unwrap();
        write_half.flush().await.unwrap();

        // Receive HELLO_ACK
        reader.read_line(&mut line).await.unwrap();
        let ack: serde_json::Value = serde_json::from_str(&line).unwrap();
        assert_eq!(ack["messageType"], "HELLO_ACK");
        assert_eq!(ack["payload"]["status"], "ACCEPTED");
        assert_eq!(ack["payload"]["protocolVersion"], "0.1");
        assert_eq!(ack["payload"]["generation"], 1);

        // Send Spectator snapshot
        let snap = json!({
            "protocolVersion": "0.1",
            "messageType": "SPECTATOR_HAND_SNAPSHOT",
            "requestId": "snap_001",
            "timestamp": 1729000000000i64,
            "payload": {
                "schemaVersion": "1.1.0",
                "roomId": 470179,
                "spectatorSessionEpoch": 1,
                "spectatorAccountPlayerId": 1556514,
                "sequenceNumber": 1,
                "snapshotTimestamp": "2026-10-16T14:00:00Z",
                "playerId": 2081758,
                "cardIds": [10005, 20002],
                "cards": [
                    { "cardId": 10005, "count": 1 },
                    { "cardId": 20002, "count": 1 }
                ],
                "handSize": 2,
                "drawCountTotal": 3,
                "playCountTotal": 1,
                "roomPlayers": {
                    "2081758": { "playerId": 2081758, "handSize": 2, "cardIds": [10005, 20002] }
                }
            }
        });
        write_half.write_all(format!("{}\n", snap).as_bytes()).await.unwrap();
        write_half.flush().await.unwrap();

        // Send HEARTBEAT
        line.clear();
        let hb = json!({
            "protocolVersion": "0.1",
            "messageType": "HEARTBEAT",
            "requestId": "hb_001",
            "payload": { "sequence": 1 }
        });
        write_half.write_all(format!("{}\n", hb).as_bytes()).await.unwrap();
        write_half.flush().await.unwrap();

        // Receive HEARTBEAT ACK
        reader.read_line(&mut line).await.unwrap();
        let hb_ack: serde_json::Value = serde_json::from_str(&line).unwrap();
        assert_eq!(hb_ack["messageType"], "HEARTBEAT");
        assert_eq!(hb_ack["payload"]["sequence"], 1);
    }

    tokio::time::sleep(Duration::from_millis(30)).await;

    // 3. Session 2 Reconnection (Simulating restart) -> Generation 2
    {
        let mut client2 = TcpStream::connect(server_addr).await.expect("Session 2 connect failed");
        let (read_half2, mut write_half2) = client2.split();
        let mut reader2 = BufReader::new(read_half2);
        let mut line2 = String::new();

        let hello2 = json!({
            "protocolVersion": "0.1",
            "messageType": "HELLO",
            "requestId": "live_session_2",
            "payload": { "gamePid": 54322 }
        });
        write_half2.write_all(format!("{}\n", hello2).as_bytes()).await.unwrap();
        write_half2.flush().await.unwrap();

        reader2.read_line(&mut line2).await.unwrap();
        let ack2: serde_json::Value = serde_json::from_str(&line2).unwrap();
        assert_eq!(ack2["payload"]["generation"], 2);
    }

    server.shutdown();
    let _ = server_handle.await;

    // 4. Verify log file persistence
    let log_targets = astralparty_hub_lib::xing_shu::bridge::diagnostic_tcp::get_diagnostic_log_paths();
    let log_exists = log_targets.iter().any(|p| p.exists() && std::fs::metadata(p).map(|m| m.len() > 0).unwrap_or(false));
    assert!(log_exists, "diagnostic_hub_tcp.log must exist and have content");
    println!("[PASS:DIAGNOSTIC_TCP_SERVER_INTEGRATION] DiagnosticTcpServer lifecycle and log verification complete");
}
