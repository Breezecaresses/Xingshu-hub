import {useEffect,useRef,useState} from 'react';
import {invoke} from '@tauri-apps/api/core';
import {platformName} from '../../shared/gameLibrary';
import type {LibraryState} from './LibraryUI';

type Result={requestId:string;status:string;message:string};
export function ReplayCodePlayer({state}:{state:LibraryState}){
  const [code,setCode]=useState(''),[target,setTarget]=useState(''),[busy,setBusy]=useState(false),[notice,setNotice]=useState('');
  const generation=useRef(0),timer=useRef<ReturnType<typeof setTimeout>|null>(null);
  const available=state.sessions.filter(s=>s.connectionState==='CONNECTED'&&s.livePlayerId);
  const connection=available.find(s=>s.connectionId===target)||state.session||available[0];
  useEffect(()=>()=>{generation.current++;if(timer.current)clearTimeout(timer.current);},[]);
  const play=async()=>{
    if(!/^[0-9]{1,30}$/.test(code.trim())){setNotice('请输入 1～30 位数字回放码。');return;}
    if(!connection){setNotice('请启动安装了新版星枢插件的电脑游戏，登录并停在大厅。');return;}
    setBusy(true);setNotice('正在请求所选游戏加载回放…');const current=++generation.current;
    try{
      const response=await invoke<Result>('request_game_replay',{connectionId:connection.connectionId,replayId:code.trim()});
      if(current!==generation.current)return;
      setNotice(response.message);
      const poll=async()=>{
        try{
          const result=await invoke<Result>('get_game_replay_status',{requestId:response.requestId});
          if(current!==generation.current)return;setNotice(result.message);
          if(result.status==='loading')timer.current=setTimeout(()=>void poll(),1000);else setBusy(false);
        }catch{if(current===generation.current){setBusy(false);setNotice('回放状态读取未完成，请确认游戏仍在运行。');}}
      };
      timer.current=setTimeout(()=>void poll(),800);
    }catch(error){if(current===generation.current){setBusy(false);setNotice(typeof error==='object'&&error&&'message'in error?String(error.message):'回放请求未完成，请稍后重试。');}}
  };
  return <section className="library-summary" aria-label="按回放码播放" style={{marginBottom:22}}>
    <h2>回放码播放</h2><p>输入官方回放码，在下方所选游戏中播放。请先返回大厅；过期或版本不兼容的回放可能无法加载。</p>
    <form onSubmit={e=>{e.preventDefault();void play();}} style={{display:'flex',gap:12,flexWrap:'wrap'}}>
      <input aria-label="回放码" placeholder="输入或粘贴数字回放码" inputMode="numeric" maxLength={30} value={code} onChange={e=>setCode(e.target.value)} disabled={busy} style={{flex:'1 1 260px'}}/>
      <select aria-label="播放回放的游戏" value={connection?.connectionId||''} disabled={busy||!available.length} onChange={e=>setTarget(e.target.value)}>
        {!available.length&&<option value="">游戏未连接</option>}{available.map(s=><option key={s.connectionId} value={s.connectionId}>{platformName(s.platformId)} · UID {s.livePlayerId}</option>)}
      </select>
      <button className="btn btn-primary" type="submit" disabled={busy||!connection||!code.trim()}>{busy?'加载中…':'在游戏中播放'}</button>
    </form>
    {notice&&<p role="status" style={{marginBottom:0}}>{notice}</p>}
  </section>;
}
