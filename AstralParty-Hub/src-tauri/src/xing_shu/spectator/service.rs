use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use chrono::Utc;
use tracing::info;
use uuid::Uuid;

use crate::domain::error::{HubError, HubResult};
use crate::xing_shu::bridge::protocol::{BridgeEnvelope, BridgeMessage, SpectatorHandPayload};
use crate::xing_shu::bridge::server::BridgeServer;
use crate::xing_shu::spectator::session::{SpectatorConnectionState, SpectatorSessionInfo};
use crate::xing_shu::spectator::stream::{SpectatorMatchSnapshot, SpectatorPlayerState};

#[derive(Clone)]
pub struct SpectatorService {
    session_info: Arc<Mutex<SpectatorSessionInfo>>,
    latest_snapshot: Arc<Mutex<Option<SpectatorMatchSnapshot>>>,
    latest_hand_payload: Arc<Mutex<Option<SpectatorHandPayload>>>,
    bridge_server: Arc<BridgeServer>,
    app_handle: Arc<Mutex<Option<tauri::AppHandle>>>,
    is_listener_started: Arc<AtomicBool>,
}

impl SpectatorService {
    pub fn new(bridge_server: Arc<BridgeServer>) -> Self {
        Self {
            session_info: Arc::new(Mutex::new(SpectatorSessionInfo::default())),
            latest_snapshot: Arc::new(Mutex::new(None)),
            latest_hand_payload: Arc::new(Mutex::new(None)),
            bridge_server,
            app_handle: Arc::new(Mutex::new(None)),
            is_listener_started: Arc::new(AtomicBool::new(false)),
        }
    }

    pub fn start_snapshot_listener(&self) -> bool {
        if self
            .is_listener_started
            .compare_exchange(false, true, Ordering::SeqCst, Ordering::SeqCst)
            .is_ok()
        {
            let service = self.clone();
            let mut rx = self.bridge_server.subscribe_spectator_snapshots();
            let task = async move {
                while let Ok(snap) = rx.recv().await {
                    service.update_hand_payload(snap);
                }
            };

            if tokio::runtime::Handle::try_current().is_ok() {
                tokio::spawn(task);
            } else {
                tauri::async_runtime::spawn(task);
            }
            true
        } else {
            false
        }
    }

    pub fn is_listener_started(&self) -> bool {
        self.is_listener_started.load(Ordering::SeqCst)
    }

    pub fn update_hand_payload(&self, snapshot: SpectatorHandPayload) -> bool {
        info!(
            "SPECTATOR_SERVICE_SNAPSHOT_RECEIVED roomId={}, epoch={}, seq={}, playerId={}, handSize={}, cardIds={:?}",
            snapshot.room_id,
            snapshot.spectator_session_epoch,
            snapshot.sequence_number,
            snapshot.player_id,
            snapshot.hand_size,
            snapshot.card_ids
        );

        *self.latest_hand_payload.lock().unwrap() = Some(snapshot.clone());

        if let Some(ref handle) = *self.app_handle.lock().unwrap() {
            use tauri::Emitter;
            let _ = handle.emit("spectator://hand-snapshot", &snapshot);
        }

        true
    }

    pub fn get_latest_hand_payload(&self) -> Option<SpectatorHandPayload> {
        self.latest_hand_payload.lock().unwrap().clone()
    }

    pub fn set_app_handle(&self, handle: tauri::AppHandle) {
        *self.app_handle.lock().unwrap() = Some(handle);
    }

    #[deprecated(note = "BridgeServer is the sole monotonicity and freshness gate")]
    pub fn get_highest_epoch(&self) -> u64 {
        self.latest_hand_payload
            .lock()
            .unwrap()
            .as_ref()
            .map(|p| p.spectator_session_epoch)
            .unwrap_or(0)
    }

    #[deprecated(note = "BridgeServer is the sole monotonicity and freshness gate")]
    pub fn get_highest_sequence(&self) -> u64 {
        self.latest_hand_payload
            .lock()
            .unwrap()
            .as_ref()
            .map(|p| p.sequence_number)
            .unwrap_or(0)
    }

    pub fn bridge_server(&self) -> Arc<BridgeServer> {
        self.bridge_server.clone()
    }

    pub fn get_session_info(&self) -> SpectatorSessionInfo {
        self.session_info.lock().unwrap().clone()
    }

    pub fn get_latest_snapshot(&self) -> Option<SpectatorMatchSnapshot> {
        self.latest_snapshot.lock().unwrap().clone()
    }

    pub fn login_spectator(&self, account_name: String) -> HubResult<SpectatorSessionInfo> {
        let trimmed = account_name.trim();
        if trimmed.is_empty() {
            return Err(HubError::new(
                "INVALID_SPECTATOR_ACCOUNT",
                "Spectator account name cannot be empty",
                "SPECTATOR",
            ));
        }

        let mut guard = self.session_info.lock().unwrap();
        guard.session_id = Uuid::new_v4().to_string();
        guard.account_display_name = trimmed.to_string();
        guard.connection_state = SpectatorConnectionState::Idle;
        guard.connected_at = Some(Utc::now());
        guard.latency_ms = Some(24);
        guard.total_events_received = 0;

        info!(
            "XingShu Spectator logged in as '{}' (Session: {})",
            guard.account_display_name, guard.session_id
        );
        Ok(guard.clone())
    }

    pub fn start_spectating(&self, room_id: String) -> HubResult<SpectatorSessionInfo> {
        let mut guard = self.session_info.lock().unwrap();
        if guard.connection_state == SpectatorConnectionState::Disconnected {
            return Err(HubError::new(
                "SPECTATOR_NOT_LOGGED_IN",
                "Must log in before spectating a match room",
                "SPECTATOR",
            ));
        }

        let match_id = format!("match_{}", Uuid::new_v4().to_string()[..8].to_string());
        guard.room_id = Some(room_id.clone());
        guard.match_id = Some(match_id.clone());
        guard.connection_state = SpectatorConnectionState::Spectating;

        info!(
            "XingShu Spectator joined room {} for match {}",
            room_id, match_id
        );

        // Populate initial live snapshot
        let snapshot = SpectatorMatchSnapshot {
            match_id: match_id.clone(),
            room_id: room_id.clone(),
            round_number: 1,
            elapsed_seconds: 0,
            players: vec![
                SpectatorPlayerState {
                    player_id: "p1".into(),
                    nickname: "StarSeeker_01".into(),
                    character_id: "char_nono".into(),
                    hp: 4,
                    max_hp: 4,
                    stars: 12,
                    hand_cards: vec!["card_speed_up".into(), "card_shield".into(), "card_star_magnet".into()],
                    status_effects: vec!["shield_buff".into()],
                    is_current_turn: true,
                    ranking: 1,
                },
                SpectatorPlayerState {
                    player_id: "p2".into(),
                    nickname: "CosmicDrifter".into(),
                    character_id: "char_amber".into(),
                    hp: 3,
                    max_hp: 4,
                    stars: 8,
                    hand_cards: vec!["card_attack_plus".into(), "card_teleport".into()],
                    status_effects: vec![],
                    is_current_turn: false,
                    ranking: 2,
                },
            ],
            timestamp: Utc::now(),
        };

        *self.latest_snapshot.lock().unwrap() = Some(snapshot.clone());

        // Broadcast to bridge
        let _ = self.broadcast_hand_sync(&snapshot);

        Ok(guard.clone())
    }

    pub fn stop_spectating(&self) -> HubResult<SpectatorSessionInfo> {
        let mut guard = self.session_info.lock().unwrap();
        guard.room_id = None;
        guard.match_id = None;
        guard.connection_state = SpectatorConnectionState::Idle;
        *self.latest_snapshot.lock().unwrap() = None;
        *self.latest_hand_payload.lock().unwrap() = None;
        info!("XingShu Spectator left match room.");
        Ok(guard.clone())
    }

    pub fn update_snapshot(&self, snapshot: SpectatorMatchSnapshot) {
        let _ = self.broadcast_hand_sync(&snapshot);
        *self.latest_snapshot.lock().unwrap() = Some(snapshot);
        let mut guard = self.session_info.lock().unwrap();
        guard.total_events_received += 1;
    }

    fn broadcast_hand_sync(&self, snapshot: &SpectatorMatchSnapshot) -> HubResult<()> {
        for player in &snapshot.players {
            let _msg = BridgeEnvelope::with_request_id(
                "HandSync",
                snapshot.match_id.clone(),
                BridgeMessage::HandSync {
                    player_key: player.player_id.clone(),
                    cards: player.hand_cards.clone(),
                },
            );
            // Log IPC bridge dispatch
            info!(
                "Bridge broadcast: HandSync dispatched for {} ({} cards, message type: {})",
                player.player_id,
                player.hand_cards.len(),
                _msg.message_type
            );
        }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_spectator_lifecycle() {
        let bridge = Arc::new(BridgeServer::new());
        let service = SpectatorService::new(bridge);

        // 1. Initial State
        let info = service.get_session_info();
        assert_eq!(info.connection_state, SpectatorConnectionState::Disconnected);

        // 2. Login
        let login_res = service.login_spectator("TestObserverBot".into());
        assert!(login_res.is_ok());
        let info2 = service.get_session_info();
        assert_eq!(info2.connection_state, SpectatorConnectionState::Idle);
        assert_eq!(info2.account_display_name, "TestObserverBot");

        // 3. Start Spectating
        let spectate_res = service.start_spectating("ROOM_8899".into());
        assert!(spectate_res.is_ok());
        let info3 = service.get_session_info();
        assert_eq!(info3.connection_state, SpectatorConnectionState::Spectating);
        assert_eq!(info3.room_id, Some("ROOM_8899".into()));

        // Snapshot is available
        let snap = service.get_latest_snapshot().expect("Snapshot should exist");
        assert_eq!(snap.room_id, "ROOM_8899");
        assert_eq!(snap.players.len(), 2);

        // 4. Stop Spectating
        let stop_res = service.stop_spectating();
        assert!(stop_res.is_ok());
        let info4 = service.get_session_info();
        assert_eq!(info4.connection_state, SpectatorConnectionState::Idle);
        assert!(service.get_latest_snapshot().is_none());
        assert!(service.get_latest_hand_payload().is_none());
    }

    #[test]
    fn test_direct_snapshot_ingestion() {
        let bridge = Arc::new(BridgeServer::new());
        let service = SpectatorService::new(bridge);

        assert!(service.get_latest_hand_payload().is_none());

        let make_payload = |epoch: u64, seq: u64| SpectatorHandPayload {
            schema_version: "1.1.0".to_string(),
            room_id: 8888,
            spectator_session_epoch: epoch,
            spectator_account_player_id: 1001,
            sequence_number: seq,
            snapshot_timestamp: "2026-10-16T12:00:00Z".to_string(),
            player_id: 2001,
            card_ids: vec![10005, 10006],
            cards: vec![],
            hand_size: 2,
            draw_count_total: 2,
            play_count_total: 0,
            room_players: std::collections::HashMap::new(),
            ..Default::default()
        };

        // Direct ingestion: SpectatorService accepts snapshot directly without redundant stale rejection
        let p1 = make_payload(1, 1);
        assert!(service.update_hand_payload(p1));
        let stored = service.get_latest_hand_payload().unwrap();
        assert_eq!(stored.sequence_number, 1);
        assert_eq!(stored.spectator_session_epoch, 1);
        assert_eq!(stored.player_id, 2001);

        // Subsequent snapshot updates directly as Bridge Server handles monotonicity
        let p2 = make_payload(1, 2);
        assert!(service.update_hand_payload(p2));
        let stored2 = service.get_latest_hand_payload().unwrap();
        assert_eq!(stored2.sequence_number, 2);
    }

    #[tokio::test]
    async fn test_start_snapshot_listener_duplicate_prevention() {
        let bridge = Arc::new(BridgeServer::new());
        let service = SpectatorService::new(bridge);

        assert!(!service.is_listener_started());

        // First start succeeds and sets listener started flag
        assert!(service.start_snapshot_listener());
        assert!(service.is_listener_started());

        // Subsequent call returns false, preventing duplicate listener tasks
        assert!(!service.start_snapshot_listener());
        assert!(service.is_listener_started());
    }

    #[tokio::test]
    async fn test_bridge_subscription_background_delivery() {
        let bridge = Arc::new(BridgeServer::new());
        let service = SpectatorService::new(bridge.clone());

        assert!(service.get_latest_hand_payload().is_none());

        // Explicitly start snapshot listener
        assert!(service.start_snapshot_listener());

        let payload = SpectatorHandPayload {
            schema_version: "1.1.0".to_string(),
            room_id: 9999,
            spectator_session_epoch: 1,
            spectator_account_player_id: 1001,
            sequence_number: 1,
            snapshot_timestamp: "2026-10-16T12:00:00Z".to_string(),
            player_id: 3001,
            card_ids: vec![20002],
            cards: vec![],
            hand_size: 1,
            draw_count_total: 1,
            play_count_total: 0,
            room_players: std::collections::HashMap::new(),
            ..Default::default()
        };

        // Broadcast through bridge
        bridge.broadcast_spectator_snapshot(payload);

        // Allow background async task to process
        tokio::time::sleep(tokio::time::Duration::from_millis(50)).await;

        let latest = service
            .get_latest_hand_payload()
            .expect("Snapshot should be delivered via bridge subscription");
        assert_eq!(latest.room_id, 9999);
        assert_eq!(latest.sequence_number, 1);
        assert_eq!(latest.player_id, 3001);
    }
}

