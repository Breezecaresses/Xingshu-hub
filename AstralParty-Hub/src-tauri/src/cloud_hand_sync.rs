use super::{data_root, error, request, token, AccountLogin};
use crate::domain::error::HubResult;
use crate::xing_shu::bridge::protocol::{PlayerHandSummary, SpectatorHandPayload};
use crate::xing_shu::bridge::server::BridgeServer;
use crate::xing_shu::bridge::session::RuntimeSession;
use chrono::{DateTime, Utc};
use serde::Deserialize;
use serde_json::{json, Value};
use std::{collections::HashMap, io::ErrorKind, path::Path, sync::Arc, time::Duration};
use tauri::{AppHandle, Manager, State};

const OPT_OUT_FILE: &str = "cloud-hands-disabled";

#[derive(Default)]
pub(super) struct CloudHandSync {
    task: Option<tokio::task::JoinHandle<()>>,
    generation: u64,
    status: &'static str,
}

impl CloudHandSync {
    pub(super) fn restore(&mut self, app: AppHandle, root: &Path, session: Option<String>) -> HubResult<Value> {
        self.stop();
        let disabled = root.join(OPT_OUT_FILE).try_exists()
            .map_err(|_| error("无法读取云端手牌设置。"))?;
        if !disabled {
            if let Some(session) = session {
                self.status = "waiting_match";
                self.task = Some(tokio::spawn(poll(app, session, self.generation)));
            } else {
                self.status = "signed_out";
            }
        }
        Ok(self.status())
    }

    pub(super) fn stop(&mut self) {
        self.generation += 1;
        if let Some(task) = self.task.take() {
            task.abort();
        }
        self.status = "disabled";
    }

    fn status(&self) -> Value {
        json!({"enabled": self.task.as_ref().is_some_and(|t| !t.is_finished()),
               "status": if self.status.is_empty() { "disabled" } else { self.status }})
    }
}

pub(crate) async fn restore_on_startup(app: AppHandle) -> HubResult<()> {
    let state = app.state::<AccountLogin>();
    let _gate = state.gate.lock().await;
    let root = data_root(&app)?;
    let status = state.hand_sync.lock().unwrap().restore(app.clone(), &root, token(&root)?)?;
    tracing::info!(status = status["status"].as_str().unwrap_or("unavailable"), "Restored cloud hands on startup");
    Ok(())
}

fn save_preference(root: &Path, enabled: bool) -> HubResult<()> {
    let file = root.join(OPT_OUT_FILE);
    let result = if enabled {
        match std::fs::remove_file(file) {
            Err(err) if err.kind() == ErrorKind::NotFound => Ok(()),
            result => result,
        }
    } else {
        std::fs::write(file, b"disabled")
    };
    result.map_err(|_| error("无法保存云端手牌设置。"))
}

#[tauri::command]
pub fn cloud_hand_sync_status(state: State<'_, AccountLogin>) -> Value {
    state.hand_sync.lock().unwrap().status()
}

#[tauri::command]
pub async fn set_cloud_hand_sync(app: AppHandle, state: State<'_, AccountLogin>, enabled: bool) -> HubResult<Value> {
    let _gate = state.gate.lock().await;
    let root = data_root(&app)?;
    let session = if enabled { token(&root)? } else { None };
    save_preference(&root, enabled)?;
    state.hand_sync.lock().unwrap().restore(app, &root, session)
}

async fn poll(app: AppHandle, token: String, generation: u64) {
    let bridge = app.state::<Arc<BridgeServer>>();
    let account = app.state::<AccountLogin>();
    let mut logged_in_uid: Option<i64> = None;
    loop {
        if logged_in_uid.is_none() {
            match request("/auth/me", None, Some(&token)).await {
                Ok(value) => {
                    logged_in_uid = value["account"]["uid"].as_str()
                        .and_then(|raw| raw.parse::<i64>().ok())
                        .filter(|&id| id > 0);
                }
                Err(err) if err.code == "ACCOUNT_LOGIN_EXPIRED" => {
                    let mut sync = account.hand_sync.lock().unwrap();
                    if sync.generation == generation {
                        sync.status = "signed_out";
                    }
                    return;
                }
                Err(_) => {}
            }
        }
        let target = bridge.cloud_hand_target_for(logged_in_uid);
        let result = if let Some(target) = &target {
            request("/account/data/live-hands", Some(json!({
                "roomId": target.room_id.unwrap_or_default().to_string(),
                "playerId": target.live_player_id.unwrap_or_default().to_string()
            })), Some(&token)).await.map(Some)
        } else {
            Ok(None)
        };
        {
            let mut sync = account.hand_sync.lock().unwrap();
            if sync.generation != generation { return; }
            sync.status = match (target, result) {
                (_, Err(err)) if err.code == "ACCOUNT_LOGIN_EXPIRED" => {
                    sync.status = "signed_out";
                    return;
                }
                (_, Err(_)) => "unavailable",
                (Some(target), Ok(Some(value))) => match value["status"].as_str() {
                    Some("ready") => match mirror(&target, value) {
                        Some(payload) if bridge.send_cloud_hand_mirror(&target, &payload) => "forwarded",
                        _ => "waiting_snapshot",
                    },
                    Some("busy") => "busy",
                    Some("identity_mismatch") => "identity_mismatch",
                    Some("unsupported") => "waiting_match",
                    _ => "waiting_snapshot",
                },
                _ => "waiting_match",
            };
        }
        tokio::time::sleep(Duration::from_secs(2)).await;
    }
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct LiveHands {
    room_id: String,
    player_id: String,
    observer_id: String,
    observed_at: DateTime<Utc>,
    hands: HashMap<String, Vec<i32>>,
}

fn mirror(target: &RuntimeSession, value: Value) -> Option<SpectatorHandPayload> {
    let live: LiveHands = serde_json::from_value(value).ok()?;
    let room_id = live.room_id.parse().ok()?;
    let player_id: i64 = live.player_id.parse().ok()?;
    let observer_id: u64 = live.observer_id.parse().ok()?;
    let age = (Utc::now() - live.observed_at).num_milliseconds();
    let context = target.match_context.as_ref()?;
    if target.room_id != Some(room_id) || target.live_player_id != Some(player_id)
        || observer_id == 0 || observer_id > i64::MAX as u64 || observer_id == player_id as u64
        || !(-300_000..120_000).contains(&age) || live.hands.len() > 16
    {
        return None;
    }

    let mut room_players = HashMap::new();
    for (key, card_ids) in live.hands {
        let id: i64 = key.parse().ok()?;
        if id <= 0 || id == player_id || !context.roster_player_ids.contains(&id)
            || card_ids.len() > 128 || card_ids.iter().any(|&id| id <= 0)
        {
            return None;
        }
        room_players.insert(id.to_string(), PlayerHandSummary {
            player_id: id as u64, hand_size: card_ids.len(), card_ids, ..Default::default()
        });
    }
    if room_players.is_empty() { return None; }
    Some(SpectatorHandPayload {
        room_id, player_id: player_id as u64, spectator_account_player_id: observer_id,
        spectator_session_epoch: context.sequence,
        sequence_number: live.observed_at.timestamp_micros().try_into().ok()?,
        snapshot_timestamp: live.observed_at.to_rfc3339(), room_players,
        platform_id: Some("CLOUD_OFFICIAL".to_owned()), ..Default::default()
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::xing_shu::bridge::protocol::RuntimeMatchContext;

    #[test]
    fn cloud_hands_save_and_clear_explicit_opt_out() {
        let root = tempfile::tempdir().unwrap();
        let flag = root.path().join(OPT_OUT_FILE);
        assert!(!flag.exists());
        save_preference(root.path(), false).unwrap();
        assert!(flag.is_file());
        save_preference(root.path(), true).unwrap();
        save_preference(root.path(), true).unwrap();
        assert!(!flag.exists());
    }

    #[test]
    fn cloud_hands_are_scoped_to_fresh_same_match_teammates() {
        let mut target = RuntimeSession::new("steam", "STEAM_CN", 2, "", Some(101), "", 1, "MAIN");
        target.room_id = Some(50);
        target.match_context = Some(RuntimeMatchContext {
            sequence: 7, roster_player_ids: vec![101, 102, 103], ..Default::default()
        });
        let response = json!({"roomId":"50", "playerId":"101", "observerId":"301",
            "observedAt": Utc::now(), "hands":{"102":[10001,10001],"103":[]}});
        let payload = mirror(&target, response.clone()).unwrap();
        assert_eq!(payload.room_players["102"].card_ids, vec![10001, 10001]);
        assert!(payload.room_players["103"].card_ids.is_empty());
        assert_eq!(payload.spectator_session_epoch, 7);
        for skew in [chrono::Duration::seconds(-30), chrono::Duration::seconds(60)] {
            let mut skewed = response.clone();
            skewed["observedAt"] = json!(Utc::now() + skew);
            assert!(mirror(&target, skewed).is_some(), "rejected tolerated skew/idle {skew}");
        }
        for (key, value) in [
            ("roomId", json!("51")), ("playerId", json!("102")), ("observerId", json!("101")),
            ("observedAt", json!("2020-01-01T00:00:00Z")),
            ("observedAt", json!(Utc::now() - chrono::Duration::seconds(150))),
            ("hands", json!({"104":[10001]})),
            ("hands", json!({"102":[0]})), ("hands", json!({"101":[10001]})),
        ] {
            let mut invalid = response.clone();
            invalid[key] = value;
            assert!(mirror(&target, invalid).is_none(), "accepted invalid {key}");
        }
    }
}
