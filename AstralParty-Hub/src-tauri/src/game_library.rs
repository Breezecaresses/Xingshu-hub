//! Account-scoped, durable snapshots of whitelisted game data. No credentials or demo records.
use crate::{domain::error::{HubError, HubResult}, storage::database::Database, xing_shu::bridge::{server::BridgeServer, session::RuntimeSession, protocol::BridgeEnvelope}};
use rusqlite::{params, OptionalExtension};
use serde::{Deserialize, Serialize};
use std::{sync::{Arc, Mutex}, collections::HashMap, time::{Duration, Instant}};

#[derive(Clone)]
pub struct GameLibraryStore(Database, Arc<Mutex<HashMap<String, PendingMatch>>>);

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all="camelCase", deny_unknown_fields)]
pub struct MatchParticipant {
    pub player_id: i64, pub name: String, pub hero_id: i32, pub rank: i32,
    pub level: i32, pub gold: i32, pub slot: i32, pub give_up: bool, pub player_level: i32,
    #[serde(default)] pub kills: Option<i64>,
    #[serde(default)] pub deaths: Option<i64>,
    #[serde(default)] pub damage: Option<i64>,
    #[serde(default)] pub taken: Option<i64>,
    #[serde(default)] pub healing: Option<i64>,
    #[serde(default)] pub coins: Option<i64>,
    #[serde(default)] pub transfer: Option<i64>,
    #[serde(default)] pub turns: Option<i64>,
    #[serde(default)] pub movement: Option<i64>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all="camelCase", deny_unknown_fields)]
pub struct MatchDetail {
    pub request_id: String, pub player_id: i64, pub index: i32, pub time: i64,
    pub hero_id: i32, pub map_id: i32, pub status: String,
    #[serde(default)] pub error: String,
    #[serde(default)] pub message: String,
    #[serde(default)] pub replay_id: String,
    #[serde(default)] pub version: String,
    #[serde(default)] pub participants: Vec<MatchParticipant>,
    #[serde(default)] pub observed_at: String,
    #[serde(default)] pub refresh_error: String,
    #[serde(default)] pub stats_source: String,
    #[serde(default)] pub stats_error: String,
    #[serde(default)] pub arena_id: Option<i32>,
    #[serde(default)] pub rounds: Option<i32>,
    #[serde(default)] pub duration_seconds: Option<i64>,
}

#[derive(Clone)]
struct PendingMatch { session: RuntimeSession, started: Instant, detail: MatchDetail }

fn record_key(time: i64, hero: i32, mode: i32) -> String { format!("{time}:{hero}:{mode}") }

fn supports_match_detail(version: &str) -> bool {
    // Release names changed after library-r2. Compare the numbered family,
    // rather than rejecting all subsequent game-ui builds.
    [("library-r", 2), ("game-ui-r", 7)].iter().any(|(family, minimum)| {
        version.rsplit_once(family).is_some_and(|(_, tail)| {
            let digits: String = tail.chars().take_while(|c| c.is_ascii_digit()).collect();
            digits.parse::<u32>().is_ok_and(|revision| revision >= *minimum)
        })
    })
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all="camelCase", deny_unknown_fields)]
pub struct HeroProgress {
    pub id: i32, pub level: i32, pub exp: i32, pub skin: i32, pub breakthrough: bool,
    #[serde(default)] pub pve_level: Option<i32>,
    #[serde(default)] pub pve_exp: Option<i32>,
    #[serde(default)] pub potential: Vec<i32>,
}
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all="camelCase", deny_unknown_fields)]
pub struct PlayedMatch {
    pub time: i64, pub rank: i32, pub hero_id: i32, pub map_id: i32,
    pub index: i32, pub replay_id: String, pub version: String,
    #[serde(default)] pub detail_available: bool,
}
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all="camelCase", deny_unknown_fields)]
pub struct AccountSnapshot {
    pub player_id: i64, pub name: String, pub level: i32, pub exp: i32,
    pub heroes: Vec<HeroProgress>, pub skins: Vec<i32>, pub alt_arts: Vec<i32>,
    /// None means the game has not loaded this account's career. Never erase previous records.
    pub matches: Option<Vec<PlayedMatch>>,
}
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all="camelCase")]
pub struct CachedAccount {
    pub platform: String, pub role: String, pub observed_at: String,
    pub snapshot: AccountSnapshot,
}

fn storage(e: impl std::fmt::Display) -> HubError { HubError::storage_error(e.to_string()) }
fn invalid() -> HubError { HubError::new("INVALID_GAME_DATA", "游戏数据身份或字段范围无效", "GAME_DATA") }

impl GameLibraryStore {
    pub fn new(db: Database) -> HubResult<Self> {
        db.with_conn(|c| c.execute_batch("CREATE TABLE IF NOT EXISTS game_library_accounts (
            platform TEXT NOT NULL, player_id INTEGER NOT NULL, role TEXT NOT NULL,
            observed_at TEXT NOT NULL, snapshot TEXT NOT NULL, PRIMARY KEY(platform, player_id));
            CREATE TABLE IF NOT EXISTS game_library_matches (
            platform TEXT NOT NULL, player_id INTEGER NOT NULL, record_key TEXT NOT NULL,
            played_at INTEGER NOT NULL, record TEXT NOT NULL, PRIMARY KEY(platform,player_id,record_key));
            CREATE TABLE IF NOT EXISTS game_library_match_details (
            platform TEXT NOT NULL, player_id INTEGER NOT NULL, record_key TEXT NOT NULL,
            detail TEXT NOT NULL, PRIMARY KEY(platform,player_id,record_key));")
            .map_err(storage))?;
        // Older builds included the server's moving detail index in the identity.
        // Canonicalize existing rows before another sync, preserving any newer canonical row.
        db.with_conn(|c| {
            let tx=c.transaction().map_err(storage)?;
            let old = {
                let mut stmt=tx.prepare("SELECT platform,player_id,record_key,record FROM game_library_matches").map_err(storage)?;
                let rows=stmt.query_map([],|r|Ok((r.get::<_,String>(0)?,r.get::<_,i64>(1)?,r.get::<_,String>(2)?,r.get::<_,String>(3)?))).map_err(storage)?;
                rows.collect::<Result<Vec<_>,_>>().map_err(storage)?
            };
            for (platform,pid,key,json) in old {
                let m:PlayedMatch=serde_json::from_str(&json).map_err(storage)?;
                let canonical=record_key(m.time,m.hero_id,m.map_id);
                if canonical==key {continue;}
                tx.execute("INSERT OR IGNORE INTO game_library_matches VALUES (?1,?2,?3,?4,?5)",params![platform,pid,canonical,m.time,json]).map_err(storage)?;
                tx.execute("DELETE FROM game_library_matches WHERE platform=?1 AND player_id=?2 AND record_key=?3",params![platform,pid,key]).map_err(storage)?;
            }
            tx.commit().map_err(storage)
        })?;
        Ok(Self(db, Arc::new(Mutex::new(HashMap::new()))))
    }

    pub fn save(&self, session: &RuntimeSession, mut data: AccountSnapshot) -> HubResult<()> {
        if session.live_player_id != Some(data.player_id) || data.player_id <= 0
            || session.connection_state != "CONNECTED" || session.platform_id == "UNKNOWN"
            || data.name.chars().count() > 100 || !(0..=10000).contains(&data.level)
            || data.exp < 0 || data.heroes.len() > 256 || data.skins.len() > 1024
            || data.alt_arts.len() > 1024 || data.matches.as_ref().is_some_and(|v| v.len() > 100)
            || data.heroes.iter().any(|h| h.id <= 0 || h.level < 0 || h.exp < 0 || h.skin < 0 || h.pve_level.is_some_and(|v|v<0 || v>100) || h.pve_exp.is_some_and(|v|v<0) || h.potential.len()>64)
            || data.matches.as_ref().is_some_and(|v| v.iter().any(|m| m.time <= 0 || !(1..=64).contains(&m.rank) || m.hero_id <= 0 || m.replay_id.len() > 128 || m.version.len() > 64)) {
            return Err(invalid());
        }
        self.0.with_conn(|c| {
            let tx = c.transaction().map_err(storage)?;
            let matches = data.matches.take();
            let previous: Option<String> = tx.query_row("SELECT snapshot FROM game_library_accounts WHERE platform=?1 AND player_id=?2",params![session.platform_id,data.player_id],|r|r.get(0)).optional().map_err(storage)?;
            let was_loaded = previous.as_deref().and_then(|s|serde_json::from_str::<AccountSnapshot>(s).ok()).is_some_and(|s|s.matches.is_some());
            data.matches = if matches.is_some() || was_loaded { Some(Vec::new()) } else { None };
            let payload = serde_json::to_string(&data).map_err(storage)?;
            tx.execute("INSERT INTO game_library_accounts VALUES (?1,?2,?3,?4,?5)
                ON CONFLICT(platform,player_id) DO UPDATE SET role=excluded.role,observed_at=excluded.observed_at,snapshot=excluded.snapshot",
                params![session.platform_id, data.player_id, session.bound_role, chrono::Utc::now().to_rfc3339(), payload]).map_err(storage)?;
            if let Some(records) = matches {
                for m in records {
                    // The server's detail index can change as its recent-history window advances.
                    let key = record_key(m.time,m.hero_id,m.map_id);
                    tx.execute("INSERT INTO game_library_matches VALUES (?1,?2,?3,?4,?5)
                        ON CONFLICT(platform,player_id,record_key) DO UPDATE SET record=excluded.record",
                        params![session.platform_id,data.player_id,key,m.time,serde_json::to_string(&m).map_err(storage)?]).map_err(storage)?;
                }
            }
            tx.commit().map_err(storage)
        })
    }

    pub fn accounts(&self) -> HubResult<Vec<CachedAccount>> {
        self.0.with_conn(|c| {
            let mut stmt = c.prepare("SELECT platform,role,observed_at,snapshot,player_id FROM game_library_accounts ORDER BY observed_at DESC").map_err(storage)?;
            let rows = stmt.query_map([], |r| Ok((r.get::<_,String>(0)?,r.get::<_,String>(1)?,r.get::<_,String>(2)?,r.get::<_,String>(3)?,r.get::<_,i64>(4)?))).map_err(storage)?;
            let mut out = Vec::new();
            for row in rows {
                let (platform,role,observed_at,json,pid)=row.map_err(storage)?;
                let mut snapshot:AccountSnapshot=serde_json::from_str(&json).map_err(storage)?;
                let mut ms=c.prepare("SELECT record FROM game_library_matches WHERE platform=?1 AND player_id=?2 ORDER BY played_at DESC LIMIT 1000").map_err(storage)?;
                let records=ms.query_map(params![platform,pid], |r| r.get::<_,String>(0)).map_err(storage)?;
                let mut records=records.map(|r| serde_json::from_str::<PlayedMatch>(&r.map_err(storage)?).map_err(storage)).collect::<HubResult<Vec<_>>>()?;
                for record in &mut records {
                    record.detail_available=c.query_row("SELECT 1 FROM game_library_match_details WHERE platform=?1 AND player_id=?2 AND record_key=?3",params![platform,pid,record_key(record.time,record.hero_id,record.map_id)],|r|r.get::<_,i32>(0)).optional().map_err(storage)?.is_some();
                }
                // Distinguish never loaded from loaded but empty, including subsequent partial updates.
                let loaded:Option<i32>=c.query_row("SELECT 1 FROM game_library_matches WHERE platform=?1 AND player_id=?2 LIMIT 1",params![platform,pid],|r|r.get(0)).optional().map_err(storage)?;
                snapshot.matches=if loaded.is_some() || snapshot.matches.is_some() {Some(records)} else {None};
                out.push(CachedAccount{platform,role,observed_at,snapshot});
            }
            Ok(out)
        })
    }

    fn begin_detail(&self, session: &RuntimeSession, time: i64, hero_id: i32, map_id: i32) -> HubResult<(MatchDetail,bool)> {
        let pid=session.live_player_id.ok_or_else(invalid)?;
        if session.connection_state!="CONNECTED" || session.platform_id=="UNKNOWN" {return Err(invalid());}
        let key=record_key(time,hero_id,map_id);
        let record=self.0.with_conn(|c| c.query_row("SELECT record FROM game_library_matches WHERE platform=?1 AND player_id=?2 AND record_key=?3",params![session.platform_id,pid,key],|r|r.get::<_,String>(0)).optional().map_err(storage))?.ok_or_else(invalid)?;
        let record:PlayedMatch=serde_json::from_str(&record).map_err(storage)?;
        let mut pending=self.1.lock().map_err(storage)?;
        pending.retain(|_,p|p.started.elapsed()<Duration::from_secs(120));
        if let Some(p)=pending.values().find(|p|p.session.connection_id==session.connection_id && p.session.generation==session.generation && p.session.platform_id==session.platform_id && p.detail.player_id==pid && p.detail.time==time && p.detail.hero_id==hero_id && p.detail.map_id==map_id && p.detail.status=="loading" && p.started.elapsed()<Duration::from_secs(40)) {return Ok((p.detail.clone(),false));}
        pending.retain(|_,p|!(p.session.platform_id==session.platform_id && p.detail.player_id==pid && p.detail.time==time && p.detail.hero_id==hero_id && p.detail.map_id==map_id));
        if pending.len()>=32 {return Err(HubError::new("GAME_DATA_BUSY","正在读取其他对局，请稍后重试","GAME_DATA"));}
        let detail=MatchDetail{request_id:uuid::Uuid::new_v4().to_string(),player_id:pid,index:record.index,time,hero_id,map_id,status:"loading".into(),error:String::new(),message:String::new(),replay_id:record.replay_id,version:record.version,participants:Vec::new(),observed_at:String::new(),refresh_error:String::new(),stats_source:String::new(),stats_error:String::new(),arena_id:None,rounds:None,duration_seconds:None};
        pending.insert(detail.request_id.clone(),PendingMatch{session:session.clone(),started:Instant::now(),detail:detail.clone()});
        Ok((detail,true))
    }

    pub fn save_live_settlement(&self, session: &RuntimeSession, mut detail: MatchDetail) -> HubResult<()> {
        // An unsolicited settlement must match a summary already returned by
        // this same authenticated account, never a spectator or arbitrary record.
        if session.bound_role == "USER_SPECTATOR" || session.live_player_id != Some(detail.player_id) ||
            detail.status != "ready" || detail.stats_source != "game_settlement" { return Err(invalid()); }
        let record: PlayedMatch = self.0.with_conn(|c| {
            let json: String = c.query_row("SELECT record FROM game_library_matches WHERE platform=?1 AND player_id=?2 AND record_key=?3",
                params![session.platform_id,detail.player_id,record_key(detail.time,detail.hero_id,detail.map_id)], |r| r.get(0)).map_err(storage)?;
            serde_json::from_str(&json).map_err(storage)
        })?;
        if record.replay_id.is_empty() || record.replay_id != detail.replay_id || record.version != detail.version ||
            !detail.participants.iter().any(|p|p.player_id==detail.player_id && p.hero_id==detail.hero_id && p.rank==record.rank) { return Err(invalid()); }
        let (pending, _) = self.begin_detail(session,detail.time,detail.hero_id,detail.map_id)?;
        detail.request_id=pending.request_id;
        detail.index=pending.index;
        self.save_detail(session,detail)
    }

    pub fn save_detail(&self, session: &RuntimeSession, mut detail: MatchDetail) -> HubResult<()> {
        let mut pending=self.1.lock().map_err(storage)?;
        let p=pending.get(&detail.request_id).ok_or_else(invalid)?;
        if p.started.elapsed()>Duration::from_secs(40) || session.connection_state!="CONNECTED"
            || p.session.connection_id!=session.connection_id || p.session.generation!=session.generation
            || p.session.platform_id!=session.platform_id || session.live_player_id!=Some(detail.player_id)
            || p.detail.player_id!=detail.player_id || p.detail.time!=detail.time
            || p.detail.hero_id!=detail.hero_id || p.detail.map_id!=detail.map_id
            || !matches!(detail.status.as_str(),"ready"|"error") || detail.error.len()>500 || detail.message.len()>1000 || detail.replay_id.len()>128 || detail.version.len()>64 || detail.stats_error.len()>200 || !matches!(detail.stats_source.as_str(),""|"profile_record"|"replay_settlement"|"game_settlement")
            || (detail.status=="ready" && (detail.replay_id!=p.detail.replay_id || detail.version!=p.detail.version))
            || detail.rounds.is_some_and(|v|v<0 || v>10000) || detail.duration_seconds.is_some_and(|v|v<0 || v>604800)
            || detail.participants.len()>16 || detail.participants.iter().any(|v| v.name.chars().count()>100 || v.hero_id<=0 || !(0..=64).contains(&v.rank) || v.level<0 || v.gold<0 || v.player_level<0 || [v.kills,v.deaths,v.damage,v.taken,v.healing,v.coins,v.transfer,v.turns,v.movement].iter().any(|n|n.is_some_and(|n| !(0..=1_000_000_000_000).contains(&n))))
            || (detail.status=="ready" && !detail.participants.iter().any(|v|v.player_id==detail.player_id && v.hero_id==detail.hero_id)) {
            return Err(invalid());
        }
        detail.observed_at=chrono::Utc::now().to_rfc3339();
        if detail.status=="ready" {
            // Replays may expire after their metrics have been saved. A later profile-only
            // response must not destroy an already verified settlement for this same match.
            if !matches!(detail.stats_source.as_str(),"replay_settlement"|"game_settlement") {
                let old:Option<MatchDetail>=self.0.with_conn(|c| {
                    let json:Option<String>=c.query_row("SELECT detail FROM game_library_match_details WHERE platform=?1 AND player_id=?2 AND record_key=?3",params![session.platform_id,detail.player_id,record_key(detail.time,detail.hero_id,detail.map_id)],|r|r.get(0)).optional().map_err(storage)?;
                    json.map(|s|serde_json::from_str(&s).map_err(storage)).transpose()
                })?;
                if let Some(old)=old.filter(|old|matches!(old.stats_source.as_str(),"replay_settlement"|"game_settlement") && old.replay_id==detail.replay_id && old.version==detail.version) {
                    for participant in &mut detail.participants {
                        if let Some(p)=old.participants.iter().find(|p|p.player_id==participant.player_id && p.hero_id==participant.hero_id && p.slot==participant.slot) {
                            participant.kills=p.kills;participant.deaths=p.deaths;participant.damage=p.damage;participant.taken=p.taken;
                            participant.healing=p.healing;participant.coins=p.coins;participant.transfer=p.transfer;participant.turns=p.turns;participant.movement=p.movement;
                        }
                    }
                    detail.stats_source=old.stats_source;detail.arena_id=old.arena_id;detail.rounds=old.rounds;detail.duration_seconds=old.duration_seconds;
                    detail.refresh_error="本次未重新取得回放统计，继续保留此前验证并保存的结算数据".into();
                }
            }
            let json=serde_json::to_string(&detail).map_err(storage)?;
            self.0.with_conn(|c|c.execute("INSERT INTO game_library_match_details VALUES (?1,?2,?3,?4) ON CONFLICT(platform,player_id,record_key) DO UPDATE SET detail=excluded.detail",params![session.platform_id,detail.player_id,record_key(detail.time,detail.hero_id,detail.map_id),json]).map(|_|()).map_err(storage))?;
            pending.remove(&detail.request_id);
        } else {
            let id=detail.request_id.clone();
            pending.get_mut(&id).unwrap().detail=detail;
        }
        Ok(())
    }

    pub fn match_detail(&self, platform: &str, pid: i64, time: i64, hero_id: i32, map_id: i32) -> HubResult<Option<MatchDetail>> {
        let pending=self.1.lock().map_err(storage)?;
        let current=if let Some(p)=pending.values().filter(|p|p.session.platform_id==platform && p.detail.player_id==pid && p.detail.time==time && p.detail.hero_id==hero_id && p.detail.map_id==map_id).max_by_key(|p|p.started) {
            let mut detail=p.detail.clone();
            if detail.status=="loading" && p.started.elapsed()>Duration::from_secs(40) {detail.status="error".into();detail.error="游戏读取超时，请确认已进入大厅后重试。".into();}
            Some(detail)
        } else {None};
        drop(pending);
        let cached:Option<MatchDetail>=self.0.with_conn(|c| {
            let json:Option<String>=c.query_row("SELECT detail FROM game_library_match_details WHERE platform=?1 AND player_id=?2 AND record_key=?3",params![platform,pid,record_key(time,hero_id,map_id)],|r|r.get(0)).optional().map_err(storage)?;
            json.map(|s|serde_json::from_str(&s).map_err(storage)).transpose()
        })?;
        match (current,cached) {
            (Some(p),Some(mut cached)) if p.status=="error" => {
                // Keep the saved metrics and timestamp, but correlate this failed refresh
                // with the request the UI is waiting for. Do not persist the view overlay.
                cached.request_id=p.request_id;
                cached.refresh_error=if p.message.is_empty(){p.error}else{p.message};Ok(Some(cached))
            },
            (Some(mut p),Some(cached)) => {
                p.participants=cached.participants;p.stats_source=cached.stats_source;p.stats_error=cached.stats_error;
                p.arena_id=cached.arena_id;p.rounds=cached.rounds;p.duration_seconds=cached.duration_seconds;p.observed_at=cached.observed_at;
                Ok(Some(p))
            },
            (Some(p),None)=>Ok(Some(p)),(None,cached)=>Ok(cached)
        }
    }
}

#[cfg_attr(feature = "web-ui", tauri::command)]
pub fn get_game_library(store: crate::desktop_host::State<GameLibraryStore>) -> HubResult<Vec<CachedAccount>> { store.accounts() }

#[cfg_attr(feature = "web-ui", tauri::command)]
pub fn sync_game_library(bridge: crate::desktop_host::State<Arc<BridgeServer>>, connection_id: String) -> HubResult<()> {
    let session=bridge.get_runtime_session(&connection_id).ok_or_else(invalid)?;
    if session.live_player_id.is_none() || session.connection_state != "CONNECTED" { return Err(invalid()); }
    let message=BridgeEnvelope::new("GAME_LIBRARY_REQUEST",serde_json::json!({}));
    if bridge.send_envelope_to(&connection_id,&message) {Ok(())}
    else {Err(HubError::new("GAME_DATA_OFFLINE","游戏连接已断开，请重新进入大厅后同步","GAME_DATA"))}
}

#[cfg_attr(feature = "web-ui", tauri::command)]
pub fn get_game_match_detail(store: crate::desktop_host::State<GameLibraryStore>, platform: String, player_id: i64, time: i64, hero_id: i32, map_id: i32) -> HubResult<Option<MatchDetail>> {
    store.match_detail(&platform,player_id,time,hero_id,map_id)
}

#[cfg_attr(feature = "web-ui", tauri::command)]
pub fn request_game_match_detail(store: crate::desktop_host::State<GameLibraryStore>, bridge: crate::desktop_host::State<Arc<BridgeServer>>, connection_id: String, player_id: i64, time: i64, hero_id: i32, map_id: i32) -> HubResult<String> {
    let session=bridge.get_runtime_session(&connection_id).ok_or_else(invalid)?;
    if session.live_player_id!=Some(player_id) {return Err(invalid());}
    if !supports_match_detail(&session.runtime_version) {
        return Err(HubError::new("GAME_RUNTIME_UPDATE","请安装支持历史对局详情的新版星枢运行时，再启动游戏。","GAME_DATA"));
    }
    let (detail,created)=store.begin_detail(&session,time,hero_id,map_id)?;
    if !created {return Ok(detail.request_id);}
    let message=BridgeEnvelope::new("GAME_LIBRARY_MATCH_REQUEST",serde_json::json!({"requestId":detail.request_id,"playerId":player_id,"index":detail.index,"time":time,"heroId":hero_id,"mapId":map_id}));
    if bridge.send_envelope_to(&connection_id,&message) {Ok(detail.request_id)}
    else {
        let mut failed=detail;failed.status="error".into();failed.error="游戏连接已断开，请重新进入大厅后重试".into();
        let _=store.save_detail(&session,failed);
        Err(HubError::new("GAME_DATA_OFFLINE","游戏连接已断开，请重新进入大厅后重试","GAME_DATA"))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn historical_details_accept_current_and_legacy_supported_releases() {
        for v in ["1.0.0-library-r2", "1.0.0-library-r10", "1.0.0-game-ui-r7", "1.0.0-game-ui-r8-fix"] { assert!(supports_match_detail(v), "{v}"); }
        for v in ["", "1.0.0-library-r1", "1.0.0-game-ui-r6", "1.0.0-game-ui-r", "unknown"] { assert!(!supports_match_detail(v), "{v}"); }
    }
    fn snapshot()->AccountSnapshot { serde_json::from_value(serde_json::json!({"playerId":42,"name":"测试","level":5,"exp":20,"heroes":[],"skins":[],"altArts":[],"matches":[{"time":1800000000,"rank":1,"heroId":101,"mapId":81003,"index":0,"replayId":"","version":"3.3.0"}]})).unwrap() }
    fn session(platform:&str)->RuntimeSession {RuntimeSession::new("c",platform,1,"game",Some(42),"1",1,"MAIN")}
    #[test] fn account_isolation_dedup_and_partial_updates() {
        let store=GameLibraryStore::new(Database::in_memory().unwrap()).unwrap();
        let mut data=snapshot();
        store.save(&session("STEAM_CN"),data.clone()).unwrap();
        store.save(&session("STEAM_CN"),data.clone()).unwrap();
        data.matches=None; data.level=6;
        store.save(&session("STEAM_CN"),data.clone()).unwrap();
        store.save(&session("TAPTAP"),data).unwrap();
        let accounts=store.accounts().unwrap();
        assert_eq!(accounts.len(),2);
        let steam=accounts.iter().find(|a|a.platform=="STEAM_CN").unwrap();
        assert_eq!(steam.snapshot.level,6);
        assert_eq!(steam.snapshot.matches.as_ref().unwrap().len(),1);
        assert!(accounts.iter().find(|a|a.platform=="TAPTAP").unwrap().snapshot.matches.is_none());
        let mut bad=snapshot(); bad.player_id=43;
        assert!(store.save(&session("STEAM_CN"),bad).is_err());
    }
    #[test] fn rejects_sensitive_or_unexpected_fields() {
        let mut value=serde_json::to_value(snapshot()).unwrap();
        value["token"]=serde_json::json!("secret");
        assert!(serde_json::from_value::<AccountSnapshot>(value).is_err());
    }
    #[test] fn empty_history_and_records_survive_reopen_and_reindexed_sync() {
        let dir=tempfile::tempdir().unwrap();
        let path=dir.path().join("library.db");
        let store=GameLibraryStore::new(Database::init(&path).unwrap()).unwrap();
        let mut data=snapshot(); data.matches=Some(Vec::new());
        store.save(&session("STEAM_CN"),data.clone()).unwrap();
        data.matches=None;
        store.save(&session("STEAM_CN"),data).unwrap();
        drop(store);
        let store=GameLibraryStore::new(Database::init(&path).unwrap()).unwrap();
        assert_eq!(store.accounts().unwrap()[0].snapshot.matches.as_ref().unwrap().len(),0);
        let mut data=snapshot();
        store.save(&session("STEAM_CN"),data.clone()).unwrap();
        data.matches.as_mut().unwrap()[0].index=19;
        store.save(&session("STEAM_CN"),data).unwrap();
        let accounts=store.accounts().unwrap();
        let records=accounts[0].snapshot.matches.as_ref().unwrap();
        assert_eq!(records.len(),1);
        assert_eq!(records[0].index,19);
    }
    fn ready(mut detail:MatchDetail)->MatchDetail {
        detail.status="ready".into();
        detail.participants=serde_json::from_value(serde_json::json!([{"playerId":42,"name":"测试","heroId":101,"rank":1,"level":5,"gold":21,"slot":0,"giveUp":false,"playerLevel":5,"kills":2,"deaths":0,"damage":35,"taken":10,"healing":20,"coins":50,"transfer":5,"turns":10,"movement":42}])).unwrap();
        detail.stats_source="replay_settlement".into();
        detail
    }
    #[test] fn detail_correlation_rejects_other_accounts_records_and_reconnected_sessions() {
        let store=GameLibraryStore::new(Database::in_memory().unwrap()).unwrap();
        let s=session("STEAM_CN");store.save(&s,snapshot()).unwrap();
        assert!(store.begin_detail(&session("TAPTAP"),1800000000,101,81003).is_err());
        assert!(store.begin_detail(&s,1800000001,101,81003).is_err());
        let request=store.begin_detail(&s,1800000000,101,81003).unwrap().0;
        let detail=ready(request);
        let mut reconnect=s.clone();reconnect.generation+=1;
        assert!(store.save_detail(&reconnect,detail.clone()).is_err());
        let mut wrong=detail.clone();wrong.hero_id=102;
        assert!(store.save_detail(&s,wrong).is_err());
        let mut wrong=detail.clone();wrong.participants[0].player_id=43;
        assert!(store.save_detail(&s,wrong).is_err());
        store.save_detail(&s,detail).unwrap();
        assert_eq!(store.match_detail("STEAM_CN",42,1800000000,101,81003).unwrap().unwrap().participants[0].damage,Some(35));
        assert!(store.match_detail("TAPTAP",42,1800000000,101,81003).unwrap().is_none());
        assert!(store.accounts().unwrap()[0].snapshot.matches.as_ref().unwrap()[0].detail_available);
    }
    #[test] fn detail_survives_reopen_and_legacy_record_keys_are_migrated_without_duplicates() {
        let dir=tempfile::tempdir().unwrap();let path=dir.path().join("library.db");
        let db=Database::init(&path).unwrap();
        let store=GameLibraryStore::new(db.clone()).unwrap();let s=session("STEAM_CN");
        store.save(&s,snapshot()).unwrap();
        let request=store.begin_detail(&s,1800000000,101,81003).unwrap().0;
        store.save_detail(&s,ready(request)).unwrap();
        db.with_conn(|c|c.execute("UPDATE game_library_matches SET record_key='1800000000:0:101:81003'",[]).map(|_|()).map_err(storage)).unwrap();
        drop(store);drop(db);
        let store=GameLibraryStore::new(Database::init(&path).unwrap()).unwrap();
        let mut data=snapshot();data.matches.as_mut().unwrap()[0].index=9;
        store.save(&s,data).unwrap();
        assert_eq!(store.accounts().unwrap()[0].snapshot.matches.as_ref().unwrap().len(),1);
        assert_eq!(store.match_detail("STEAM_CN",42,1800000000,101,81003).unwrap().unwrap().participants[0].kills,Some(2));
    }
    #[test] fn detail_requests_coalesce_only_within_same_live_generation_and_retry_clears_errors() {
        let store=GameLibraryStore::new(Database::in_memory().unwrap()).unwrap();let s=session("STEAM_CN");
        store.save(&s,snapshot()).unwrap();
        let (first,created)=store.begin_detail(&s,1800000000,101,81003).unwrap();assert!(created);
        let (same,created)=store.begin_detail(&s,1800000000,101,81003).unwrap();assert!(!created);assert_eq!(first.request_id,same.request_id);
        let mut reconnect=s.clone();reconnect.generation+=1;
        let (next,created)=store.begin_detail(&reconnect,1800000000,101,81003).unwrap();assert!(created);assert_ne!(first.request_id,next.request_id);
        assert!(store.save_detail(&s,ready(first)).is_err());
        let mut failed=next.clone();failed.status="error".into();failed.error="timeout".into();
        store.save_detail(&reconnect,failed).unwrap();
        let (retry,created)=store.begin_detail(&reconnect,1800000000,101,81003).unwrap();assert!(created);
        store.save_detail(&reconnect,ready(retry)).unwrap();
        assert_eq!(store.match_detail("STEAM_CN",42,1800000000,101,81003).unwrap().unwrap().status,"ready");
        let (refresh,_)=store.begin_detail(&reconnect,1800000000,101,81003).unwrap();
        let refresh_id=refresh.request_id.clone();
        let saved_time=store.match_detail("STEAM_CN",42,1800000000,101,81003).unwrap().unwrap().observed_at;
        let mut failed=refresh;failed.status="error".into();failed.message="服务器记录已过期".into();
        store.save_detail(&reconnect,failed).unwrap();
        let cached=store.match_detail("STEAM_CN",42,1800000000,101,81003).unwrap().unwrap();
        assert_eq!(cached.status,"ready");assert_eq!(cached.participants[0].damage,Some(35));
        assert_eq!(cached.refresh_error,"服务器记录已过期");
        assert_eq!(cached.request_id,refresh_id);
        assert_eq!(cached.observed_at,saved_time);
        let (request,_)=store.begin_detail(&reconnect,1800000000,101,81003).unwrap();
        let mut partial=ready(request);partial.stats_source="profile_record".into();partial.stats_error="REPLAY_UNAVAILABLE".into();
        partial.participants[0].damage=None;partial.participants[0].kills=None;
        store.save_detail(&reconnect,partial).unwrap();
        let cached=store.match_detail("STEAM_CN",42,1800000000,101,81003).unwrap().unwrap();
        assert_eq!(cached.stats_source,"replay_settlement");assert_eq!(cached.participants[0].damage,Some(35));
        assert_eq!(cached.participants[0].movement,Some(42));
    }
    #[test] fn movement_is_optional_in_old_details_and_validated_in_new_replies() {
        let store=GameLibraryStore::new(Database::in_memory().unwrap()).unwrap();let s=session("STEAM_CN");
        store.save(&s,snapshot()).unwrap();
        let request=store.begin_detail(&s,1800000000,101,81003).unwrap().0;
        let mut detail=ready(request);
        let mut legacy=serde_json::to_value(&detail.participants[0]).unwrap();
        legacy.as_object_mut().unwrap().remove("movement");
        assert_eq!(serde_json::from_value::<MatchParticipant>(legacy).unwrap().movement,None);
        detail.participants[0].movement=Some(-1);
        assert!(store.save_detail(&s,detail.clone()).is_err());
        detail.participants[0].movement=Some(42);
        store.save_detail(&s,detail).unwrap();
        assert_eq!(store.match_detail("STEAM_CN",42,1800000000,101,81003).unwrap().unwrap().participants[0].movement,Some(42));
    }
    #[test] fn live_settlement_is_bound_to_own_career_and_survives_partial_refresh() {
        let dir=tempfile::tempdir().unwrap();let path=dir.path().join("live.db");
        let store=GameLibraryStore::new(Database::init(&path).unwrap()).unwrap();
        let mut s=session("BILIBILI");s.bound_role="UNBOUND".into();
        let mut data=snapshot();data.matches.as_mut().unwrap()[0].replay_id="replay-42".into();
        store.save(&s,data.clone()).unwrap();
        let mut detail=ready(store.begin_detail(&s,1800000000,101,81003).unwrap().0);
        detail.stats_source="game_settlement".into();
        let mut other=s.clone();other.platform_id="TAPTAP".into();assert!(store.save_live_settlement(&other,detail.clone()).is_err());
        other=s.clone();other.live_player_id=Some(43);assert!(store.save_live_settlement(&other,detail.clone()).is_err());
        other=s.clone();other.bound_role="USER_SPECTATOR".into();assert!(store.save_live_settlement(&other,detail.clone()).is_err());
        for field in ["replay", "version", "rank", "time", "hero", "map"] {
            let mut wrong=detail.clone();
            match field { "replay"=>wrong.replay_id="other".into(),"version"=>wrong.version="other".into(),"rank"=>wrong.participants[0].rank=2,"time"=>wrong.time+=1,"hero"=>wrong.hero_id+=1,_=>wrong.map_id+=1 }
            assert!(store.save_live_settlement(&s,wrong).is_err(),"{field}");
        }
        store.save_live_settlement(&s,detail.clone()).unwrap();
        store.save_live_settlement(&s,detail).unwrap();
        let mut partial=ready(store.begin_detail(&s,1800000000,101,81003).unwrap().0);
        partial.stats_source="profile_record".into();partial.participants[0].damage=None;partial.participants[0].movement=None;
        store.save_detail(&s,partial).unwrap();
        data.matches=None;store.save(&s,data).unwrap();drop(store);
        let store=GameLibraryStore::new(Database::init(&path).unwrap()).unwrap();
        let saved=store.match_detail("BILIBILI",42,1800000000,101,81003).unwrap().unwrap();
        assert_eq!(saved.stats_source,"game_settlement");assert_eq!(saved.participants[0].damage,Some(35));assert_eq!(saved.participants[0].movement,Some(42));
        assert!(store.accounts().unwrap()[0].snapshot.matches.as_ref().unwrap()[0].detail_available);
    }

}
