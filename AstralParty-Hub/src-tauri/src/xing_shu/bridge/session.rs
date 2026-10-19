use std::collections::HashMap;
use std::sync::{Arc, RwLock};
use chrono::Utc;
use serde::{Deserialize, Serialize};
use crate::xing_shu::bridge::protocol::RuntimeMatchContext;

pub const BOUND_STEAM_MAIN_PLATFORM: &str = "STEAM_CN";
pub const BOUND_STEAM_MAIN_PLAYER_ID: i64 = 1556514;
pub const BOUND_STEAM_MAIN_ROLE: &str = "MAIN";

pub const BOUND_TAPTAP_SPECTATOR_PLATFORM: &str = "TAPTAP";
pub const BOUND_TAPTAP_SPECTATOR_PLAYER_ID: i64 = 3045259;
pub const BOUND_TAPTAP_SPECTATOR_ROLE: &str = "USER_SPECTATOR";

/// Represents an active runtime connection session in the Multi-Runtime Bridge architecture.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RuntimeSession {
    #[serde(alias = "connection_id")]
    pub connection_id: String,
    #[serde(alias = "platform_id")]
    pub platform_id: String, // e.g. "STEAM_CN", "TAPTAP", "STEAM_GLOBAL"
    #[serde(alias = "game_pid", alias = "gamePid", alias = "process_id", alias = "processId")]
    pub pid: u32,
    #[serde(default, alias = "executable_path")]
    pub executable_path: String,
    #[serde(default, alias = "player_id", alias = "playerId", alias = "local_player_id", alias = "localPlayerId")]
    pub live_player_id: Option<i64>,
    #[serde(default, alias = "runtime_version")]
    pub runtime_version: String,
    pub generation: u64,
    #[serde(alias = "connected_at")]
    pub connected_at: String, // ISO-8601
    #[serde(alias = "last_seen_at")]
    pub last_seen_at: String, // ISO-8601
    #[serde(alias = "role")]
    pub bound_role: String, // "MAIN", "USER_SPECTATOR", "UNBOUND"
    #[serde(default, alias = "expected_bound_player_id", alias = "expectedBoundPlayerId", skip_serializing_if = "Option::is_none")]
    pub expected_bound_player_id: Option<i64>,
    #[serde(default = "default_connection_state", alias = "status")]
    pub connection_state: String,
    #[serde(default, alias = "match_context", alias = "matchContext", skip_serializing_if = "Option::is_none")]
    pub match_context: Option<RuntimeMatchContext>,

    // Compatibility fields
    #[serde(default, skip_deserializing)]
    pub game_pid: u32,
    #[serde(default, skip_deserializing)]
    pub player_id: Option<i64>,
    #[serde(default, skip_deserializing)]
    pub role: String,
    #[serde(default = "default_connection_state", skip_deserializing)]
    pub status: String,
    #[serde(default, alias = "internal_room_id", skip_serializing_if = "Option::is_none")]
    pub internal_room_id: Option<i64>,
    #[serde(default, alias = "spectator_code", skip_serializing_if = "Option::is_none")]
    pub spectator_code: Option<String>,
    #[serde(default, alias = "room_id", skip_serializing_if = "Option::is_none")]
    pub room_id: Option<u64>,
    #[serde(default, alias = "in_match")]
    pub in_match: bool,
    #[serde(default, alias = "is_pve", skip_serializing_if = "Option::is_none")]
    pub is_pve: Option<bool>,
    #[serde(default, alias = "match_context_updated_at", skip_serializing_if = "Option::is_none")]
    pub match_context_updated_at: Option<i64>,
}

fn default_connection_state() -> String {
    "CONNECTED".to_string()
}

impl RuntimeSession {
    pub fn new(
        connection_id: impl Into<String>,
        platform_id: impl Into<String>,
        game_pid: u32,
        executable_path: impl Into<String>,
        player_id: Option<i64>,
        runtime_version: impl Into<String>,
        generation: u64,
        role: impl Into<String>,
    ) -> Self {
        let now = Utc::now().to_rfc3339();
        let plat_str = platform_id.into();
        let role_str = role.into();
        let expected_bound = if plat_str == BOUND_STEAM_MAIN_PLATFORM {
            Some(BOUND_STEAM_MAIN_PLAYER_ID)
        } else if plat_str == BOUND_TAPTAP_SPECTATOR_PLATFORM {
            Some(BOUND_TAPTAP_SPECTATOR_PLAYER_ID)
        } else {
            None
        };
        let effective_role = role_str;

        Self {
            connection_id: connection_id.into(),
            platform_id: plat_str,
            pid: game_pid,
            game_pid,
            executable_path: executable_path.into(),
            live_player_id: player_id,
            player_id,
            runtime_version: runtime_version.into(),
            generation,
            connected_at: now.clone(),
            last_seen_at: now,
            bound_role: effective_role.clone(),
            role: effective_role,
            expected_bound_player_id: expected_bound,
            connection_state: "CONNECTED".to_string(),
            status: "CONNECTED".to_string(),
            match_context: None,
            internal_room_id: None,
            spectator_code: None,
            room_id: None,
            in_match: false,
            is_pve: None,
            match_context_updated_at: None,
        }
    }

    pub fn touch(&mut self) {
        self.last_seen_at = Utc::now().to_rfc3339();
    }
}

/// Concurrent manager for active runtime sessions.
#[derive(Clone)]
pub struct SessionManager {
    sessions: Arc<RwLock<HashMap<String, RuntimeSession>>>,
    bindings: Arc<RwLock<Option<Vec<RuntimeBinding>>>>,
}

impl Default for SessionManager {
    fn default() -> Self { Self::new() }
}

#[derive(Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RuntimeBinding {
    pub platform_id: String,
    pub player_id: i64,
    pub role: String,
}

fn apply_binding(session: &mut RuntimeSession, bindings: &[RuntimeBinding]) {
    let exact = bindings.iter().find(|b| b.platform_id == session.platform_id && Some(b.player_id) == session.live_player_id);
    let pending = bindings.iter().filter(|b| b.platform_id == session.platform_id).collect::<Vec<_>>();
    session.expected_bound_player_id = exact.map(|b| b.player_id).or_else(|| if pending.len() == 1 { Some(pending[0].player_id) } else { None });
    session.bound_role = exact.map(|b| b.role.clone()).unwrap_or_else(|| "UNBOUND".into());
    session.role = session.bound_role.clone();
}

impl SessionManager {
    pub fn configure_bindings(&self, bindings: Vec<RuntimeBinding>) -> Result<(), String> {
        let mut roles = std::collections::HashSet::new();
        let mut accounts = std::collections::HashSet::new();
        for b in &bindings {
            if b.player_id <= 0 || !matches!(b.platform_id.as_str(), "STEAM_CN" | "STEAM_GLOBAL" | "TAPTAP" | "BILIBILI" | "WEGAME") ||
                !matches!(b.role.as_str(), "MAIN" | "USER_SPECTATOR") || !roles.insert(&b.role) || !accounts.insert((&b.platform_id, b.player_id)) {
                return Err("Invalid or conflicting account bindings".into());
            }
        }
        *self.bindings.write().unwrap() = Some(bindings.clone());
        for session in self.sessions.write().unwrap().values_mut() { apply_binding(session, &bindings); }
        Ok(())
    }
    pub fn new() -> Self {
        Self {
            sessions: Arc::new(RwLock::new(HashMap::new())),
            bindings: Arc::new(RwLock::new(if cfg!(test) { None } else { Some(vec![]) })),
        }
    }

    pub fn insert(&self, mut session: RuntimeSession) {
        if let Some(bindings) = self.bindings.read().unwrap().as_ref() { apply_binding(&mut session, bindings); }
        let mut map = self.sessions.write().unwrap();
        map.insert(session.connection_id.clone(), session);
    }

    pub fn get(&self, connection_id: &str) -> Option<RuntimeSession> {
        let map = self.sessions.read().unwrap();
        map.get(connection_id).cloned()
    }

    pub fn remove(&self, connection_id: &str) -> Option<RuntimeSession> {
        let mut map = self.sessions.write().unwrap();
        map.remove(connection_id)
    }

    pub fn list(&self) -> Vec<RuntimeSession> {
        let map = self.sessions.read().unwrap();
        map.values().cloned().collect()
    }

    pub fn is_empty(&self) -> bool {
        let map = self.sessions.read().unwrap();
        map.is_empty()
    }

    pub fn count(&self) -> usize {
        let map = self.sessions.read().unwrap();
        map.len()
    }

    pub fn update_player_id(&self, connection_id: &str, player_id: i64) -> Option<RuntimeSession> {
        let configured = self.bindings.read().unwrap().clone();
        let mut map = self.sessions.write().unwrap();
        if let Some(session) = map.get_mut(connection_id) {
            session.player_id = Some(player_id);
            session.live_player_id = Some(player_id);
            // STRICT PLATFORM-ROLE ISOLATION:
            // Steam CN with 1556514 -> MAIN
            // TapTap with 3045259 -> USER_SPECTATOR
            if let Some(bindings) = configured.as_ref() {
                apply_binding(session, bindings);
            } else if session.platform_id == BOUND_STEAM_MAIN_PLATFORM && player_id == BOUND_STEAM_MAIN_PLAYER_ID {
                session.role = BOUND_STEAM_MAIN_ROLE.to_string();
                session.bound_role = BOUND_STEAM_MAIN_ROLE.to_string();
            } else if session.platform_id == BOUND_TAPTAP_SPECTATOR_PLATFORM && player_id == BOUND_TAPTAP_SPECTATOR_PLAYER_ID {
                session.role = BOUND_TAPTAP_SPECTATOR_ROLE.to_string();
                session.bound_role = BOUND_TAPTAP_SPECTATOR_ROLE.to_string();
            } else {
                session.role = "UNBOUND".to_string();
                session.bound_role = "UNBOUND".to_string();
            }
            if let Some(ref mut ctx) = session.match_context {
                ctx.local_player_id = Some(player_id);
            }
            session.touch();
            Some(session.clone())
        } else {
            None
        }
    }

    pub fn update_match_context_struct(
        &self,
        connection_id: &str,
        mut ctx: RuntimeMatchContext,
    ) -> Option<RuntimeSession> {
        let mut map = self.sessions.write().unwrap();
        if let Some(session) = map.get_mut(connection_id) {
            if (!ctx.connection_id.is_empty() && ctx.connection_id != session.connection_id)
                || (!ctx.platform_id.is_empty() && ctx.platform_id != session.platform_id)
                || (ctx.process_id != 0 && ctx.process_id != session.pid)
                || matches!((ctx.local_player_id, session.live_player_id), (Some(a), Some(b)) if a != b)
                || ctx.generation.is_some_and(|g| g != session.generation)
                || session.match_context.as_ref().is_some_and(|old| ctx.sequence > 0 && old.sequence >= ctx.sequence) {
                return None;
            }
            ctx.normalize();
            if ctx.connection_id.is_empty() {
                ctx.connection_id = session.connection_id.clone();
            }
            if ctx.platform_id.is_empty() {
                ctx.platform_id = session.platform_id.clone();
            }
            if ctx.process_id == 0 {
                ctx.process_id = session.pid;
            }
            if ctx.local_player_id.is_none() && session.live_player_id.is_some() {
                ctx.local_player_id = session.live_player_id;
            }
            let is_in_match = ctx.is_in_match();
            session.in_match = is_in_match;
            let room_val = ctx.get_internal_room_id();
            session.internal_room_id = room_val;
            session.room_id = room_val.map(|r| r as u64);
            session.spectator_code = ctx.spectator_code.clone();
            session.is_pve = match ctx.match_kind.as_deref() {
                Some("PVE") => Some(true),
                Some("PVP") => Some(false),
                _ => None,
            };
            session.match_context_updated_at = Some(ctx.timestamp as i64);
            session.match_context = Some(ctx);
            session.touch();
            Some(session.clone())
        } else {
            None
        }
    }

    pub fn update_match_context(
        &self,
        connection_id: &str,
        room_id: u64,
        in_match: bool,
        is_pve: Option<bool>,
    ) -> Option<RuntimeSession> {
        let mut ctx = RuntimeMatchContext {
            connection_id: connection_id.to_string(),
            internal_room_id: if room_id > 0 { Some(room_id as i64) } else { None },
            room_id,
            in_match,
            match_state: if in_match { "MAIN_IN_MATCH".to_string() } else { "MAIN_MENU".to_string() },
            is_pve,
            match_kind: is_pve.map(|p| if p { "PVE".to_string() } else { "PVP".to_string() }),
            timestamp: Utc::now().timestamp_millis() as u64,
            ..Default::default()
        };
        ctx.normalize();
        self.update_match_context_struct(connection_id, ctx)
    }

    pub fn find_bound_steam_main(&self) -> Option<RuntimeSession> {
        let map = self.sessions.read().unwrap();
        map.values().find(|s| {
            s.live_player_id.is_some_and(|id| id > 0 && Some(id) == s.expected_bound_player_id)
                && s.bound_role == "MAIN" && s.connection_state == "CONNECTED"
        }).cloned()
    }

    pub fn is_bound_taptap_spectator(&self, connection_id: &str) -> bool {
        let map = self.sessions.read().unwrap();
        if let Some(s) = map.get(connection_id) {
            s.live_player_id.is_some_and(|id| id > 0 && Some(id) == s.expected_bound_player_id)
                && s.bound_role == "USER_SPECTATOR" && s.connection_state == "CONNECTED"
        } else {
            false
        }
    }

    pub fn touch_session(&self, connection_id: &str) -> Option<RuntimeSession> {
        let mut map = self.sessions.write().unwrap();
        if let Some(session) = map.get_mut(connection_id) {
            session.touch();
            Some(session.clone())
        } else {
            None
        }
    }

    pub fn clear(&self) {
        let mut map = self.sessions.write().unwrap();
        map.clear();
    }
}

#[cfg(test)]
mod runtime_truth_closure_tests {
    use super::*;

    #[test]
    fn fresh_values_same_count_roster_and_context_order_are_preserved() {
        let sm = SessionManager::new();
        let id = (uuid::Uuid::new_v4().as_u128() % 8_000_000) as i64 + 100;
        let room = (uuid::Uuid::new_v4().as_u128() % 900_000) as i64 + 1;
        let code = uuid::Uuid::new_v4().simple().to_string();
        sm.insert(RuntimeSession::new("main", "STEAM_CN", 321, "", Some(id), "test", 7, "UNBOUND"));
        sm.insert(RuntimeSession::new("tap", "TAPTAP", 654, "", None, "test", 8, "UNBOUND"));
        let mut ctx = RuntimeMatchContext { connection_id: "main".into(), platform_id: "STEAM_CN".into(),
            process_id: 321, local_player_id: Some(id), internal_room_id: Some(room), spectator_code: Some(code.clone()),
            match_state: "MAIN_IN_MATCH".into(), match_kind: Some("PVE".into()), player_count: 4,
            roster_player_ids: vec![id, 10, 20, 30], sequence: 1, generation: Some(7), ..Default::default() };
        let first = sm.update_match_context_struct("main", ctx.clone()).unwrap();
        assert_eq!(first.internal_room_id, Some(room));
        assert_eq!(first.spectator_code.as_deref(), Some(code.as_str()));
        ctx.roster_player_ids = vec![id, 40, 50, 60];
        ctx.sequence = 2;
        let replaced = sm.update_match_context_struct("main", ctx.clone()).unwrap();
        assert_eq!(replaced.match_context.unwrap().roster_player_ids, vec![id, 40, 50, 60]);
        ctx.sequence = 1;
        assert!(sm.update_match_context_struct("main", ctx.clone()).is_none());
        ctx.sequence = 3;
        ctx.platform_id = "TAPTAP".into();
        assert!(sm.update_match_context_struct("main", ctx).is_none());
        let tap = sm.get("tap").unwrap();
        assert_eq!(tap.live_player_id, None);
        assert_eq!(tap.match_context, None);
    }

    #[test]
    fn unknown_kind_and_account_change_never_retain_a_verified_role() {
        let sm = SessionManager::new();
        sm.insert(RuntimeSession::new("main", "STEAM_CN", 321, "", Some(BOUND_STEAM_MAIN_PLAYER_ID), "test", 1, "MAIN"));
        let unknown = sm.update_match_context_struct("main", RuntimeMatchContext {
            match_state: "LOBBY".into(), match_kind: Some("UNKNOWN".into()), ..Default::default()
        }).unwrap();
        assert_eq!(unknown.is_pve, None);
        assert!(!unknown.in_match);
        let other = sm.update_player_id("main", BOUND_STEAM_MAIN_PLAYER_ID + 1).unwrap();
        assert_eq!(other.bound_role, "UNBOUND");
        assert_eq!(other.expected_bound_player_id, Some(BOUND_STEAM_MAIN_PLAYER_ID));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_session_manager_match_context_and_bound_resolvers() {
        let sm = SessionManager::new();

        let s_steam = RuntimeSession::new("c1", "STEAM_CN", 1000, "", None, "1.0", 1, "UNBOUND");
        let s_tap = RuntimeSession::new("c2", "TAPTAP", 2000, "", None, "1.0", 1, "UNBOUND");
        sm.insert(s_steam.clone());
        sm.insert(s_tap.clone());

        assert!(sm.find_bound_steam_main().is_none());
        assert!(!sm.is_bound_taptap_spectator("c2"));

        // Promote Steam to MAIN
        let updated_steam = sm.update_player_id("c1", 1556514).expect("Update c1");
        assert_eq!(updated_steam.role, "MAIN");
        assert_eq!(updated_steam.bound_role, "MAIN");
        assert_eq!(updated_steam.live_player_id, Some(1556514));
        assert_eq!(sm.find_bound_steam_main().unwrap().connection_id, "c1");

        // Promote TapTap to USER_SPECTATOR
        let updated_tap = sm.update_player_id("c2", 3045259).expect("Update c2");
        assert_eq!(updated_tap.role, "USER_SPECTATOR");
        assert_eq!(updated_tap.bound_role, "USER_SPECTATOR");
        assert_eq!(updated_tap.live_player_id, Some(3045259));
        assert!(sm.is_bound_taptap_spectator("c2"));

        // Update match context for Steam
        let with_ctx = sm.update_match_context("c1", 88888, true, Some(false)).expect("Update ctx");
        assert_eq!(with_ctx.room_id, Some(88888));
        assert_eq!(with_ctx.internal_room_id, Some(88888));
        assert!(with_ctx.in_match);
        assert_eq!(with_ctx.is_pve, Some(false));
        assert!(with_ctx.match_context_updated_at.is_some());
        assert!(with_ctx.match_context.is_some());
    }

    #[test]
    fn test_multi_runtime_connection_scoped_state_independence() {
        // Requirement 3:
        // Two live clients remain completely independent.
        // Session A: STEAM_CN, PID=Steam PID, livePlayerId=1556514, role=MAIN, matchContext=current Steam PVE
        // Session B: TAPTAP, PID=TapTap PID, livePlayerId=3045259, role=USER_SPECTATOR, matchContext=spectator client state
        let sm = SessionManager::new();

        // 1. Establish Session A (Steam CN MAIN)
        let s_steam = RuntimeSession::new("conn_steam_a", "STEAM_CN", 15200, "AstralParty_CN.exe", Some(1556514), "1.3.4", 1, "MAIN");
        sm.insert(s_steam);

        // 2. Establish Session B (TapTap USER_SPECTATOR)
        let s_tap = RuntimeSession::new("conn_taptap_b", "TAPTAP", 28310, "AstralParty_Tap.exe", Some(3045259), "1.3.4", 1, "USER_SPECTATOR");
        sm.insert(s_tap);

        assert_eq!(sm.count(), 2);

        // 3. Update Steam Session with PVE MatchContext
        let steam_ctx = RuntimeMatchContext {
            connection_id: "conn_steam_a".into(),
            platform_id: "STEAM_CN".into(),
            process_id: 15200,
            local_player_id: Some(1556514),
            match_state: "MAIN_IN_MATCH".into(),
            match_kind: Some("PVE".into()),
            internal_room_id: Some(351045),
            spectator_code: Some("C05FE7B".into()),
            player_count: 4,
            roster_player_ids: vec![1556514, 212667, 345678, 456789],
            scene: Some("BattleScene".into()),
            timestamp: 1729300000000,
            sequence: 5,
            ..Default::default()
        };
        let updated_steam = sm.update_match_context_struct("conn_steam_a", steam_ctx).expect("Update Steam Match Context");

        // Verify Steam Session A is strictly configured
        assert_eq!(updated_steam.platform_id, "STEAM_CN");
        assert_eq!(updated_steam.pid, 15200);
        assert_eq!(updated_steam.live_player_id, Some(1556514));
        assert_eq!(updated_steam.bound_role, "MAIN");
        assert_eq!(updated_steam.internal_room_id, Some(351045));
        assert_eq!(updated_steam.spectator_code.as_deref(), Some("C05FE7B"));
        assert!(updated_steam.in_match);
        assert_eq!(updated_steam.is_pve, Some(true));

        // 4. Verify TapTap Session B was NOT MUTATED by Steam match update
        let tap_current = sm.get("conn_taptap_b").expect("TapTap session exists");
        assert_eq!(tap_current.platform_id, "TAPTAP");
        assert_eq!(tap_current.pid, 28310);
        assert_eq!(tap_current.live_player_id, Some(3045259));
        assert_eq!(tap_current.bound_role, "USER_SPECTATOR");
        assert_eq!(tap_current.internal_room_id, None);
        assert_eq!(tap_current.spectator_code, None);
        assert!(!tap_current.in_match);
        assert_eq!(tap_current.match_context, None);

        // 5. Update TapTap Session B as spectator entering room
        let tap_ctx = RuntimeMatchContext {
            connection_id: "conn_taptap_b".into(),
            platform_id: "TAPTAP".into(),
            process_id: 28310,
            local_player_id: Some(3045259),
            match_state: "SPECTATING".into(),
            match_kind: Some("PVE".into()),
            internal_room_id: Some(351045),
            spectator_code: Some("C05FE7B".into()),
            player_count: 4,
            roster_player_ids: vec![1556514, 212667, 345678, 456789],
            scene: Some("SpectatorView".into()),
            timestamp: 1729300001000,
            sequence: 1,
            ..Default::default()
        };
        let updated_tap = sm.update_match_context_struct("conn_taptap_b", tap_ctx).expect("Update TapTap");
        assert_eq!(updated_tap.platform_id, "TAPTAP");
        assert_eq!(updated_tap.live_player_id, Some(3045259));
        assert_eq!(updated_tap.bound_role, "USER_SPECTATOR");

        // Verify Steam Session A was NOT MUTATED by TapTap update
        let steam_final = sm.get("conn_steam_a").expect("Steam session exists");
        assert_eq!(steam_final.platform_id, "STEAM_CN");
        assert_eq!(steam_final.live_player_id, Some(1556514));
        assert_eq!(steam_final.bound_role, "MAIN");
        assert_eq!(steam_final.match_context.as_ref().unwrap().match_state, "MAIN_IN_MATCH");
    }
}
