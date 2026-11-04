//! Explicit replay playback in one selected, authenticated game connection.
use std::{collections::HashMap, sync::{Arc, Mutex}, time::{Duration, Instant}};
use serde::{Serialize, Deserialize};
use crate::{domain::error::{HubError,HubResult}, xing_shu::bridge::{server::BridgeServer, session::RuntimeSession, protocol::BridgeEnvelope}};

#[derive(Default, Clone)]
pub struct GameReplayStore(Arc<Mutex<HashMap<String,Pending>>>);
struct Pending {connection: String, generation: u64, started: Instant, result: ReplayStatus}
#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all="camelCase", deny_unknown_fields)]
pub struct ReplayStatus {pub request_id: String, pub status: String, pub message: String}
fn error(message:&str)->HubError {HubError::new("GAME_REPLAY",message,"GAME_DATA")}
pub fn valid_replay_id(id:&str)->bool { !id.is_empty() && id.len()<=30 && id.bytes().all(|c|c.is_ascii_digit()) }
fn supports(version:&str)->bool {
    version.rsplit_once("game-ui-r8.").and_then(|(_,tail)|tail.split('-').next()?.parse::<u32>().ok()).is_some_and(|r|r>=18)
}
impl GameReplayStore {
    fn begin(&self, session:&RuntimeSession)->HubResult<ReplayStatus>{
        let mut map=self.0.lock().map_err(|_|error("回放状态暂不可用。"))?;
        map.retain(|_,p|p.started.elapsed()<Duration::from_secs(180));
        if map.values().any(|p|p.connection==session.connection_id&&p.result.status=="loading"&&p.started.elapsed()<Duration::from_secs(90)){
            return Err(error("所选游戏正在加载上一份回放。"));
        }
        if map.len()>=32{return Err(error("回放请求较多，请稍后重试。"));}
        let result=ReplayStatus{request_id:uuid::Uuid::new_v4().to_string(),status:"loading".into(),message:"正在等待游戏检查并加载回放…".into()};
        map.insert(result.request_id.clone(),Pending{connection:session.connection_id.clone(),generation:session.generation,started:Instant::now(),result:result.clone()});
        Ok(result)
    }
    pub fn accept(&self,session:&RuntimeSession,result:ReplayStatus){
        if !matches!(result.status.as_str(),"loading"|"ready"|"error")||result.message.len()>1000{return;}
        if let Ok(mut map)=self.0.lock(){
            if let Some(p)=map.get_mut(&result.request_id){
                if p.connection==session.connection_id&&p.generation==session.generation&&p.started.elapsed()<Duration::from_secs(90){p.result=result;}
            }
        }
    }
    fn status(&self,id:&str)->HubResult<ReplayStatus>{
        let map=self.0.lock().map_err(|_|error("回放状态暂不可用。"))?;
        let p=map.get(id).ok_or_else(||error("回放请求不存在或已过期。"))?;
        if p.started.elapsed()>Duration::from_secs(90)&&p.result.status=="loading"{
            return Ok(ReplayStatus{request_id:id.into(),status:"error".into(),message:"游戏加载超时，请返回大厅后重试。".into()});
        }
        Ok(p.result.clone())
    }
}
#[cfg_attr(feature="web-ui", tauri::command)]
pub fn request_game_replay(store:crate::desktop_host::State<GameReplayStore>,bridge:crate::desktop_host::State<Arc<BridgeServer>>,connection_id:String,replay_id:String)->HubResult<ReplayStatus>{
    let id=replay_id.trim();
    if !valid_replay_id(id){return Err(error("请输入 1～30 位数字回放码。"));}
    let session=bridge.get_runtime_session(&connection_id).ok_or_else(||error("所选游戏未连接，请启动游戏并进入大厅。"))?;
    if session.connection_state!="CONNECTED"||session.live_player_id.is_none(){return Err(error("所选游戏尚未登录。"));}
    if session.in_match{return Err(error("请结束当前对局并返回大厅，再播放回放。"));}
    if !supports(&session.runtime_version){return Err(error("请先安装 r8.18 或更新的电脑插件，再重新启动游戏。"));}
    let result=store.begin(&session)?;
    let message=BridgeEnvelope::new("GAME_REPLAY_REQUEST",serde_json::json!({"requestId":result.request_id,"playerId":session.live_player_id,"replayId":id}));
    if !bridge.send_envelope_to(&connection_id,&message){
        store.accept(&session,ReplayStatus{request_id:result.request_id.clone(),status:"error".into(),message:"游戏连接已断开。".into()});
        return Err(error("游戏连接已断开。"));
    }
    Ok(result)
}
#[cfg_attr(feature="web-ui", tauri::command)]
pub fn get_game_replay_status(store:crate::desktop_host::State<GameReplayStore>,request_id:String)->HubResult<ReplayStatus>{store.status(&request_id)}

#[cfg(test)] mod tests{
    use super::*;
    #[test] fn replay_code_and_version_are_strict(){
        for id in ["1","1793550351774606"]{assert!(valid_replay_id(id));}
        for id in ["","../123","https://example/123","123\n456","1;exit"]{assert!(!valid_replay_id(id));}
        assert!(supports("1.0.0-game-ui-r8.18"));assert!(!supports("1.0.0-game-ui-r8.17"));
    }
    #[test] fn another_connection_or_generation_cannot_finish_request(){
        let store=GameReplayStore::default();
        let mut s=RuntimeSession::new("a","STEAM_CN",1,"game",Some(123),"1",1,"MAIN");
        let pending=store.begin(&s).unwrap();
        let done=ReplayStatus{request_id:pending.request_id.clone(),status:"ready".into(),message:"done".into()};
        s.generation=2;store.accept(&s,done.clone());assert_eq!(store.status(&pending.request_id).unwrap().status,"loading");
        s.generation=1;store.accept(&s,done);assert_eq!(store.status(&pending.request_id).unwrap().status,"ready");
    }
}
