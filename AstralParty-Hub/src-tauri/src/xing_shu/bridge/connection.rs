use std::collections::HashMap;
use std::sync::{Arc, RwLock};
use tokio::sync::mpsc::Sender;

/// Holds runtime state and outbound channel for an active connection.
#[derive(Clone)]
pub struct ActiveConnection {
    pub connection_id: String,
    pub platform_id: String,
    pub game_pid: u32,
    pub generation: u64,
    pub sender: Sender<String>,
    pub spectator_active_epoch: u64,
    pub spectator_highest_sequence: u64,
    pub spectator_retired_epoch: Option<u64>,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn retired_watch_epoch_rejects_late_frames_without_affecting_peer_or_reconnect() {
        let manager = ConnectionManager::new();
        let (first, _first_rx) = tokio::sync::mpsc::channel(4);
        let (peer, _peer_rx) = tokio::sync::mpsc::channel(4);
        manager.insert(ActiveConnection::new("first", "TAPTAP", 1, 1, first));
        manager.insert(ActiveConnection::new("peer", "STEAM_CN", 2, 1, peer));
        assert_eq!(manager.check_and_update_spectator_monotonicity("first", 10, 1), Ok(()));
        assert_eq!(manager.check_and_update_spectator_monotonicity("peer", 10, 1), Ok(()));
        manager.retire_spectator_epoch("first");
        assert_eq!(manager.check_and_update_spectator_monotonicity("first", 10, 500), Err("ENDED_EPOCH"));
        assert_eq!(manager.check_and_update_spectator_monotonicity("first", 9, 500), Err("ENDED_EPOCH"));
        assert_eq!(manager.check_and_update_spectator_monotonicity("peer", 10, 2), Ok(()));
        assert_eq!(manager.check_and_update_spectator_monotonicity("first", 11, 1), Ok(()));
        manager.retire_spectator_epoch("first");
        assert_eq!(manager.check_and_update_spectator_monotonicity("first", 11, 2), Err("ENDED_EPOCH"));
        manager.remove("first");
        let (reconnected, _rx) = tokio::sync::mpsc::channel(4);
        manager.insert(ActiveConnection::new("first", "TAPTAP", 1, 2, reconnected));
        assert_eq!(manager.check_and_update_spectator_monotonicity("first", 1, 1), Ok(()));
    }
}

impl ActiveConnection {
    pub fn new(
        connection_id: impl Into<String>,
        platform_id: impl Into<String>,
        game_pid: u32,
        generation: u64,
        sender: Sender<String>,
    ) -> Self {
        Self {
            connection_id: connection_id.into(),
            platform_id: platform_id.into(),
            game_pid,
            generation,
            sender,
            spectator_active_epoch: 0,
            spectator_highest_sequence: 0,
            spectator_retired_epoch: None,
        }
    }
}

/// Concurrent manager for active connection handlers.
#[derive(Clone, Default)]
pub struct ConnectionManager {
    connections: Arc<RwLock<HashMap<String, ActiveConnection>>>,
}

impl ConnectionManager {
    pub fn new() -> Self {
        Self {
            connections: Arc::new(RwLock::new(HashMap::new())),
        }
    }

    pub fn insert(&self, connection: ActiveConnection) {
        let mut map = self.connections.write().unwrap();
        map.insert(connection.connection_id.clone(), connection);
    }

    pub fn remove(&self, connection_id: &str) -> Option<ActiveConnection> {
        let mut map = self.connections.write().unwrap();
        map.remove(connection_id)
    }

    pub fn get(&self, connection_id: &str) -> Option<ActiveConnection> {
        let map = self.connections.read().unwrap();
        map.get(connection_id).cloned()
    }

    pub fn get_sender(&self, connection_id: &str) -> Option<Sender<String>> {
        let map = self.connections.read().unwrap();
        map.get(connection_id).map(|c| c.sender.clone())
    }

    pub fn get_generation(&self, connection_id: &str) -> Option<u64> {
        let map = self.connections.read().unwrap();
        map.get(connection_id).map(|c| c.generation)
    }

    pub fn increment_generation(&self, connection_id: &str) -> Option<u64> {
        let mut map = self.connections.write().unwrap();
        if let Some(conn) = map.get_mut(connection_id) {
            conn.generation += 1;
            Some(conn.generation)
        } else {
            None
        }
    }

    pub fn send_to(&self, connection_id: &str, line: &str) -> bool {
        let sender = {
            let map = self.connections.read().unwrap();
            map.get(connection_id).map(|c| c.sender.clone())
        };
        if let Some(tx) = sender {
            let formatted = if line.ends_with('\n') {
                line.to_string()
            } else {
                format!("{}\n", line)
            };
            tx.try_send(formatted).is_ok()
        } else {
            false
        }
    }

    pub fn broadcast(&self, line: &str) {
        let senders: Vec<Sender<String>> = {
            let map = self.connections.read().unwrap();
            map.values().map(|c| c.sender.clone()).collect()
        };
        let formatted = if line.ends_with('\n') {
            line.to_string()
        } else {
            format!("{}\n", line)
        };
        for tx in senders {
            let _ = tx.try_send(formatted.clone());
        }
    }

    pub fn broadcast_except(&self, exclude_connection_id: &str, line: &str) {
        let senders: Vec<Sender<String>> = {
            let map = self.connections.read().unwrap();
            map.iter()
                .filter(|(id, _)| id.as_str() != exclude_connection_id)
                .map(|(_, c)| c.sender.clone())
                .collect()
        };
        let formatted = if line.ends_with('\n') {
            line.to_string()
        } else {
            format!("{}\n", line)
        };
        for tx in senders {
            let _ = tx.try_send(formatted.clone());
        }
    }

    /// Ending a match also retires its stream. A higher sequence within the old
    /// epoch is still an old-match message, even if it arrives after cache clear.
    pub fn retire_spectator_epoch(&self, connection_id: &str) {
        let mut map = self.connections.write().unwrap();
        if let Some(conn) = map.get_mut(connection_id) {
            if conn.spectator_active_epoch > 0 {
                conn.spectator_retired_epoch = Some(conn.spectator_active_epoch);
            }
        }
    }

    pub fn check_and_update_spectator_monotonicity(
        &self,
        connection_id: &str,
        epoch: u64,
        seq: u64,
    ) -> Result<(), &'static str> {
        let mut map = self.connections.write().unwrap();
        if let Some(conn) = map.get_mut(connection_id) {
            if conn.spectator_retired_epoch.is_some_and(|retired| epoch <= retired) {
                return Err("ENDED_EPOCH");
            }
            if epoch < conn.spectator_active_epoch {
                return Err("STALE_EPOCH");
            }
            if epoch == conn.spectator_active_epoch && seq <= conn.spectator_highest_sequence {
                return Err("STALE_SEQUENCE");
            }

            if epoch > conn.spectator_active_epoch {
                conn.spectator_active_epoch = epoch;
                conn.spectator_highest_sequence = seq;
            } else {
                conn.spectator_highest_sequence = seq;
            }
            Ok(())
        } else {
            Err("CONNECTION_NOT_FOUND")
        }
    }

    pub fn count(&self) -> usize {
        let map = self.connections.read().unwrap();
        map.len()
    }

    pub fn is_empty(&self) -> bool {
        let map = self.connections.read().unwrap();
        map.is_empty()
    }

    pub fn clear(&self) {
        let mut map = self.connections.write().unwrap();
        map.clear();
    }
}
