use serde::{Deserialize, Serialize};
use std::sync::{Arc, Mutex};
use tracing::info;
use crate::domain::error::{HubError, HubResult};
use crate::xing_shu::bridge::protocol::{
    BridgeEnvelope, BridgeHandshake, BridgeHeartbeat, BridgeMessage, ComponentIdentity,
};

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub enum BridgeConnectionState {
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed(String),
}

#[derive(Clone)]
pub struct BridgeClient {
    pub identity: ComponentIdentity,
    pub state: Arc<Mutex<BridgeConnectionState>>,
    pub session_id: Arc<Mutex<Option<String>>>,
    pub heartbeat_sequence: Arc<Mutex<u64>>,
}

impl BridgeClient {
    pub fn new(identity: ComponentIdentity) -> Self {
        Self {
            identity,
            state: Arc::new(Mutex::new(BridgeConnectionState::Disconnected)),
            session_id: Arc::new(Mutex::new(None)),
            heartbeat_sequence: Arc::new(Mutex::new(0)),
        }
    }

    pub fn prepare_handshake(&self, capabilities: Vec<String>) -> BridgeHandshake {
        *self.state.lock().unwrap() = BridgeConnectionState::Connecting;
        BridgeHandshake {
            protocol_version: 1,
            component: self.identity.clone(),
            capabilities,
        }
    }

    pub fn on_handshake_ack(&self, session_id: String) {
        *self.session_id.lock().unwrap() = Some(session_id.clone());
        *self.state.lock().unwrap() = BridgeConnectionState::Connected;
        info!(
            "BridgeClient ({:?}) connected successfully. Session ID: {}",
            self.identity, session_id
        );
    }

    pub fn next_heartbeat(&self) -> HubResult<BridgeEnvelope<BridgeMessage>> {
        let sid = self.session_id.lock().unwrap().clone().ok_or_else(|| {
            HubError::new("BRIDGE_NOT_CONNECTED", "Client has no active session", "BRIDGE")
        })?;

        let mut seq = self.heartbeat_sequence.lock().unwrap();
        *seq += 1;
        let current_seq = *seq;

        let hb = BridgeHeartbeat {
            sequence: current_seq,
            timestamp: chrono::Utc::now().timestamp_millis(),
        };

        Ok(BridgeEnvelope::with_request_id(
            "Heartbeat",
            sid,
            BridgeMessage::Heartbeat(hb),
        ))
    }

    pub fn get_state(&self) -> BridgeConnectionState {
        self.state.lock().unwrap().clone()
    }

    pub fn disconnect(&self) {
        *self.state.lock().unwrap() = BridgeConnectionState::Disconnected;
        *self.session_id.lock().unwrap() = None;
        info!("BridgeClient ({:?}) disconnected.", self.identity);
    }
}
