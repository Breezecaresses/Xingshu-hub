//! Local proof of the managed-worker lifecycle using an authenticated official client.
//! No account creation, credentials, remote listener or synthetic hand data.
use super::bridge::{
    protocol::SpectatorHandPayload, server::BridgeServer, session::RuntimeSession,
};
use chrono::{DateTime, Utc};
use serde::Serialize;
use serde_json::json;
use std::sync::Mutex;
use uuid::Uuid;

const LEASE_MS: i64 = 90_000;
const JOIN_TIMEOUT_MS: i64 = 30_000;

#[derive(Clone, Debug)]
struct Lease {
    id: String,
    worker: RuntimeSession,
    main_connection: String,
    main_generation: u64,
    room: i64,
    code: String,
    request: String,
    allocated: i64,
    expires: i64,
    baseline: (u64, u64),
    confirmed: bool,
}

#[derive(Default)]
pub struct LocalWatcher {
    lease: Mutex<Option<Lease>>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LocalWatcherInfo {
    pub status: String,
    pub execution_mode: &'static str,
    pub message: String,
    pub session_id: Option<String>,
    pub allocated_player_id: Option<i64>,
    pub allocated_at: Option<String>,
    pub expires_at: Option<String>,
    pub current_room_id: Option<i64>,
    pub connection_id: Option<String>,
    pub request_id: Option<String>,
}

fn info(status: &str, message: &str, lease: Option<&Lease>) -> LocalWatcherInfo {
    let iso = |t| DateTime::from_timestamp_millis(t).map(|t| t.to_rfc3339());
    LocalWatcherInfo {
        status: status.into(),
        execution_mode: "LOCAL_VALIDATION",
        message: message.into(),
        session_id: lease.map(|l| l.id.clone()),
        allocated_player_id: lease.and_then(|l| l.worker.live_player_id),
        allocated_at: lease.and_then(|l| iso(l.allocated)),
        expires_at: lease.and_then(|l| iso(l.expires)),
        current_room_id: lease.map(|l| l.room),
        connection_id: lease.map(|l| l.worker.connection_id.clone()),
        request_id: lease.map(|l| l.request.clone()),
    }
}

fn worker(sessions: &[RuntimeSession]) -> Option<&RuntimeSession> {
    let mut workers = sessions.iter().filter(|s| {
        s.connection_state == "CONNECTED"
            && s.bound_role == "USER_SPECTATOR"
            && s.live_player_id
                .is_some_and(|id| id > 0 && Some(id) == s.expected_bound_player_id)
    });
    let first = workers.next()?;
    if workers.next().is_some() {
        None
    } else {
        Some(first)
    }
}

fn target(sessions: &[RuntimeSession]) -> Result<(&RuntimeSession, i64, String), String> {
    let mut mains = sessions.iter().filter(|s| {
        s.connection_state == "CONNECTED"
            && s.bound_role == "MAIN"
            && s.live_player_id
                .is_some_and(|id| id > 0 && Some(id) == s.expected_bound_player_id)
    });
    let main = mains.next().ok_or("等待已验证的 Steam 主账号 Runtime")?;
    if mains.next().is_some() {
        return Err("主账号 Runtime 不唯一".into());
    }
    let ctx = main
        .match_context
        .as_ref()
        .ok_or("等待主账号真实对局上下文")?;
    if ctx.match_state != "MAIN_IN_MATCH" {
        return Err("等待主账号进入对局".into());
    }
    // First local managed validation retains the already-proven PVE scope.
    if !ctx.is_pve_match() {
        return Err("本机托管验证当前仅开放 PVE 对局".into());
    }
    let room = ctx
        .get_internal_room_id()
        .filter(|id| *id > 0)
        .ok_or("等待真实房间号")?;
    let code = ctx
        .spectator_code
        .as_ref()
        .map(|s| s.trim().to_string())
        .filter(|s| !s.is_empty() && s.len() <= 64 && s.bytes().all(|b| b.is_ascii_alphanumeric()))
        .ok_or("等待真实观战码")?;
    Ok((main, room, code))
}

fn valid(lease: &Lease, sessions: &[RuntimeSession], now: i64) -> bool {
    if now >= lease.expires {
        return false;
    }
    let Some(w) = worker(sessions) else {
        return false;
    };
    let Ok((main, room, code)) = target(sessions) else {
        return false;
    };
    w.connection_id == lease.worker.connection_id
        && w.generation == lease.worker.generation
        && w.live_player_id == lease.worker.live_player_id
        && main.connection_id == lease.main_connection
        && main.generation == lease.main_generation
        && room == lease.room
        && code == lease.code
}

fn confirms(lease: &Lease, p: &SpectatorHandPayload) -> bool {
    p.room_id == lease.room as u64
        && p.connection_id.as_deref() == Some(&lease.worker.connection_id)
        && p.platform_id.as_deref() == Some(lease.worker.platform_id.as_str())
        && Some(p.spectator_account_player_id as i64) == lease.worker.live_player_id
        && (p.spectator_session_epoch, p.sequence_number) > lease.baseline
        && DateTime::parse_from_rfc3339(&p.snapshot_timestamp)
            .is_ok_and(|t| t.timestamp_millis() >= lease.allocated)
        && p.room_players.len() == 4
}

impl LocalWatcher {
    pub fn status(&self, bridge: &BridgeServer, session_id: Option<&str>) -> LocalWatcherInfo {
        let sessions = bridge.get_runtime_sessions();
        let now = Utc::now().timestamp_millis();
        let mut guard = self.lease.lock().unwrap();
        if let Some(lease) = guard.as_mut() {
            if !valid(lease, &sessions, now) {
                tracing::info!(room_id = lease.room, "[LocalManagedWatcher] lease expired");
                *guard = None;
                return info(
                    "EXPIRED",
                    "托管租约已失效：对局已结束、账号已断线或运行时已更换",
                    None,
                );
            }
            // Only the holder renews the lease. Viewing pool status does not keep it occupied.
            if session_id == Some(&lease.id) {
                lease.expires = now + LEASE_MS;
            }
            if let Some(snapshot) = bridge.get_latest_spectator_snapshot() {
                if !lease.confirmed && confirms(lease, &snapshot) {
                    lease.confirmed = true;
                    tracing::info!(
                        room_id = lease.room,
                        player_id = lease.worker.live_player_id,
                        epoch = snapshot.spectator_session_epoch,
                        sequence = snapshot.sequence_number,
                        "[LocalManagedWatcher] current lease confirmed by real four-player snapshot"
                    );
                }
            }
            if lease.confirmed {
                return info(
                    "IN_USE",
                    "本机托管验证中：已收到当前租约对应的四人真实手牌；仍使用本机 TapTap 官方客户端",
                    Some(lease),
                );
            }
            if now - lease.allocated > JOIN_TIMEOUT_MS {
                return info(
                    "ERROR",
                    "观战加入超时，尚未收到四人真实手牌；请检查客户端并重试",
                    Some(lease),
                );
            }
            return info(
                "ALLOCATING",
                "已向官方客户端发送观战请求，等待当前房间的真实四人手牌",
                Some(lease),
            );
        }
        if worker(&sessions).is_some() {
            info(
                "AVAILABLE",
                "本机验证 Worker 已连接；进入 PVE 对局后可租用此 TapTap 观战账号",
                None,
            )
        } else if sessions
            .iter()
            .any(|s| s.platform_id == "TAPTAP" && s.connection_state == "CONNECTED")
        {
            info(
                "IDENTITY_PENDING",
                "TapTap 已连接，等待绑定账号的真实身份验证",
                None,
            )
        } else {
            info(
                "OFFLINE",
                "本机验证 Worker 离线；请启动已绑定账号的 TapTap 官方客户端并进入大厅",
                None,
            )
        }
    }

    pub fn allocate(
        &self,
        bridge: &BridgeServer,
        room: Option<i64>,
        code: Option<&str>,
        owner: Option<&str>,
    ) -> Result<LocalWatcherInfo, String> {
        self.allocate_with(bridge, room, code, owner, |worker, target, id| {
            bridge.send_watch_target(worker, target, id)
        })
    }

    fn allocate_with(
        &self,
        bridge: &BridgeServer,
        room: Option<i64>,
        code: Option<&str>,
        owner: Option<&str>,
        send: impl FnOnce(&str, serde_json::Value, u64) -> Result<String, String>,
    ) -> Result<LocalWatcherInfo, String> {
        let sessions = bridge.get_runtime_sessions();
        let (main, actual_room, actual_code) = target(&sessions)?;
        let w = worker(&sessions).ok_or("TapTap 观战 Worker 尚未完成身份验证")?;
        if room.is_some_and(|r| r != actual_room) || code.is_some_and(|c| c.trim() != actual_code) {
            return Err("观战目标与主账号当前真实对局不一致".into());
        }
        let now = Utc::now().timestamp_millis();
        let mut guard = self.lease.lock().unwrap();
        if let Some(l) = guard.as_ref().filter(|l| valid(l, &sessions, now)) {
            if owner != Some(&l.id) {
                return Err("本机观战账号已被其他会话租用".into());
            }
            return Ok(info(
                if l.confirmed { "IN_USE" } else { "ALLOCATING" },
                "复用当前托管租约",
                Some(l),
            ));
        }
        *guard = None;
        let baseline = bridge
            .get_latest_spectator_snapshot()
            .map(|p| (p.spectator_session_epoch, p.sequence_number))
            .unwrap_or((0, 0));
        let request = send(
            &w.connection_id,
            json!({"roomId": actual_room, "internalRoomId": actual_room,
            "watchCode": actual_code, "spectatorCode": actual_code, "matchKind": "PVE"}),
            w.live_player_id.unwrap() as u64,
        )?;
        *guard = Some(Lease {
            id: Uuid::new_v4().to_string(),
            worker: w.clone(),
            main_connection: main.connection_id.clone(),
            main_generation: main.generation,
            room: actual_room,
            code: actual_code,
            request,
            allocated: now,
            expires: now + LEASE_MS,
            baseline,
            confirmed: false,
        });
        tracing::info!(
            room_id = actual_room,
            player_id = w.live_player_id,
            "[LocalManagedWatcher] lease allocated; official watch command dispatched"
        );
        Ok(info(
            "ALLOCATING",
            "托管租约已分配，等待官方观战回包及四人真实手牌",
            guard.as_ref(),
        ))
    }

    pub fn release(&self, session_id: &str) -> Result<(), String> {
        let mut guard = self.lease.lock().unwrap();
        if let Some(lease) = guard.as_ref() {
            if lease.id != session_id {
                return Err("托管租约不属于当前会话".into());
            }
            tracing::info!(room_id = lease.room, "[LocalManagedWatcher] lease released");
        }
        *guard = None;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::super::bridge::protocol::RuntimeMatchContext;
    use super::*;
    fn bridge() -> BridgeServer {
        let b = BridgeServer::new_test();
        let mut m = RuntimeSession::new("main", "STEAM_CN", 100, "", Some(1556514), "", 1, "MAIN");
        m.match_context = Some(RuntimeMatchContext {
            match_state: "MAIN_IN_MATCH".into(),
            match_kind: Some("PVE".into()),
            internal_room_id: Some(639893),
            spectator_code: Some("C140A15".into()),
            ..Default::default()
        });
        b.session_manager().insert(m);
        b.session_manager().insert(RuntimeSession::new(
            "worker",
            "TAPTAP",
            200,
            "",
            Some(3045259),
            "",
            2,
            "USER_SPECTATOR",
        ));
        b
    }
    fn allocate(
        w: &LocalWatcher,
        b: &BridgeServer,
        owner: Option<&str>,
    ) -> Result<LocalWatcherInfo, String> {
        w.allocate_with(b, Some(639893), Some("C140A15"), owner, |c, _, id| {
            assert_eq!(c, "worker");
            assert_eq!(id, 3045259);
            Ok("request".into())
        })
    }
    #[test]
    fn requires_real_worker_and_current_match() {
        let w = LocalWatcher::default();
        let b = bridge();
        b.session_manager().remove("worker");
        assert_eq!(w.status(&b, None).status, "OFFLINE");
        assert!(allocate(&w, &b, None).is_err());
    }
    #[test]
    fn lease_idempotency_ownership_and_release() {
        let w = LocalWatcher::default();
        let b = bridge();
        let a = allocate(&w, &b, None).unwrap();
        assert_eq!(a.status, "ALLOCATING");
        assert!(allocate(&w, &b, None).is_err());
        assert_eq!(
            allocate(&w, &b, a.session_id.as_deref())
                .unwrap()
                .session_id,
            a.session_id
        );
        assert!(w.release("other").is_err());
        w.release(a.session_id.as_ref().unwrap()).unwrap();
        assert_eq!(w.status(&b, None).status, "AVAILABLE");
    }
    #[test]
    fn restart_or_room_change_invalidates_lease() {
        let w = LocalWatcher::default();
        let b = bridge();
        allocate(&w, &b, None).unwrap();
        let mut s = b.session_manager().get("worker").unwrap();
        s.generation += 1;
        b.session_manager().insert(s);
        assert_eq!(w.status(&b, None).status, "EXPIRED");
        allocate(&w, &b, None).unwrap();
        let mut m = b.session_manager().get("main").unwrap();
        m.match_context.as_mut().unwrap().internal_room_id = Some(1);
        b.session_manager().insert(m);
        assert_eq!(w.status(&b, None).status, "EXPIRED");
    }
    #[test]
    fn stale_or_other_room_snapshot_never_confirms() {
        let w = LocalWatcher::default();
        let b = bridge();
        allocate(&w, &b, None).unwrap();
        let lease = w.lease.lock().unwrap().clone().unwrap();
        let mut p = SpectatorHandPayload {
            room_id: lease.room as u64,
            connection_id: Some("worker".into()),
            platform_id: Some("TAPTAP".into()),
            spectator_account_player_id: 3045259,
            spectator_session_epoch: 7,
            sequence_number: 1,
            snapshot_timestamp: Utc::now().to_rfc3339(),
            ..Default::default()
        };
        for id in [1556514, 99990, 99991, 99992] {
            p.room_players.insert(id.to_string(), Default::default());
        }
        assert!(confirms(&lease, &p));
        p.room_id += 1;
        assert!(!confirms(&lease, &p));
        p.room_id -= 1;
        p.snapshot_timestamp = "2000-01-01T00:00:00Z".into();
        assert!(!confirms(&lease, &p));
    }
    #[test]
    fn expiry_and_untrusted_identity_cannot_renew() {
        let w = LocalWatcher::default();
        let b = bridge();
        let a = allocate(&w, &b, None).unwrap();
        w.lease.lock().unwrap().as_mut().unwrap().expires = 1;
        assert_eq!(w.status(&b, a.session_id.as_deref()).status, "EXPIRED");
        let mut s = b.session_manager().get("worker").unwrap();
        s.live_player_id = Some(1556514);
        b.session_manager().insert(s);
        assert!(allocate(&w, &b, None).is_err());
    }
    #[test]
    fn rejects_wrong_code_pvp_and_failed_dispatch() {
        let w = LocalWatcher::default();
        let b = bridge();
        assert!(w.allocate(&b, Some(1), None, None).is_err());
        assert!(w.allocate(&b, None, Some("ABCDEF"), None).is_err());
        assert!(
            w.allocate_with(&b, None, None, None, |_, _, _| Err("offline".into()))
                .is_err()
        );
        assert!(w.lease.lock().unwrap().is_none());
        let mut s = b.session_manager().get("main").unwrap();
        s.match_context.as_mut().unwrap().match_kind = Some("PVP".into());
        b.session_manager().insert(s);
        assert!(allocate(&w, &b, None).is_err());
    }
}
