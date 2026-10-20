import { useEffect, useState } from 'react';
import { invoke, isTauri } from '@tauri-apps/api/core';
import { characterName, gameCatalog, matchDate } from '../../shared/gameLibrary';
import type { MatchDetail, PlayedMatch } from '../../shared/gameLibrary';
import { matchModeName, rateMatch, ratingMethod } from '../../shared/matchRating';
import { matchHistorySummary, matchResult } from '../../shared/matchResults';
import { GameImage, LibraryHeader, LibraryModal, LibraryStats, useGameLibrary } from './LibraryUI';
import type { LibraryState } from './LibraryUI';
import './matches.css';

const errorMessage = (e: unknown) => typeof e === 'object' && e && 'message' in e ? String(e.message) : String(e);
const metric = (v: number | null | undefined) => v == null ? '—' : v.toLocaleString('zh-CN');
const detailErrors: Record<string,string> = {
  REPLAY_UNAVAILABLE:'该回放暂不可用，已保存服务器战绩详情。', REPLAY_NOT_FOUND:'服务器未保留该回放，已保存基础战绩。',
  REPLAY_DOWNLOAD_FAILED:'回放下载失败，可稍后重新读取。', REPLAY_VERSION_UNSUPPORTED:'此版本回放无法解析，已保留基础战绩。',
  REPLAY_PARSE_FAILED:'此版本回放无法解析，已保留基础战绩。', REPLAY_ID_MISMATCH:'回放身份不一致，未采用其统计数据。',
  REPLAY_IDENTITY_MISMATCH:'回放与所选账号或对局不一致，未采用其统计数据。',
  REPLAY_READ_FAILED:'回放暂时读取失败，可稍后重新读取。',
  REPLAY_TRUNCATED:'回放文件不完整，已保留基础战绩。',
  REPLAY_STATISTICS_MISSING:'该回放未提供完整统计，已保留基础战绩。',
  REPLAY_SETTLEMENT_MISSING:'该回放未保留结算，已保留基础战绩。',
  REPLAY_TIMEOUT:'读取回放超时，可稍后重新读取。',
  REPLAY_CDN_UNAVAILABLE:'游戏回放服务暂不可用，可稍后重新读取。',
  REPLAY_ID_UNAVAILABLE:'此战绩未提供回放编号，已保留基础战绩。',
  REPLAY_TOO_LARGE:'回放超出读取大小限制，已保留基础战绩。',
};

function MatchInspector({ record, state, close }: { record: PlayedMatch; state: LibraryState; close: () => void }) {
  const [detail, setDetail] = useState<MatchDetail | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [request, setRequest] = useState(0);
  const pid=state.account!.snapshot.playerId;
  const platform=state.account!.platform;
  const session=state.session;
  useEffect(() => {
    let cancelled=false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const args={platform,playerId:pid,time:record.time,heroId:record.heroId,mapId:record.mapId};
    async function run() {
      setLoading(true);setError('');
      if (!isTauri()) {setLoading(false);return;}
      try {
        let next=await invoke<MatchDetail | null>('get_game_match_detail',args);
        if (cancelled) return;
        if (next?.status==='ready' && !request) {setDetail(next);setLoading(false);return;}
        if (!session) {setDetail(next);setLoading(false);return;}
        await invoke('request_game_match_detail',{connectionId:session.connectionId,...args});
        const started=Date.now();
        async function poll() {
          if (cancelled) return;
          try {
            next=await invoke<MatchDetail | null>('get_game_match_detail',args);
            if (cancelled) return;
            setDetail(next);
            if (next?.status==='ready' || next?.status==='error') {setLoading(false);void state.refresh();return;}
            if (Date.now()-started>45000) {setError('游戏读取超时，请确认已进入大厅后重试。');setLoading(false);return;}
            timer=setTimeout(()=>void poll(),800);
          } catch(e) {if (!cancelled) {setError(errorMessage(e));setLoading(false);}}
        }
        await poll();
      } catch(e) {if (!cancelled) {setError(errorMessage(e));setLoading(false);}}
    }
    void run();
    return ()=>{cancelled=true;if(timer)clearTimeout(timer);};
  },[platform,pid,record.time,record.heroId,record.mapId,session?.connectionId,request]);
  const players=detail?.participants || [];
  const ratings=detail?.statsSource==='replay_settlement' ? rateMatch(players) : players.map(()=>null);
  const best=Math.max(...ratings.filter((v):v is number=>v!=null));
  const hero=gameCatalog.characters.find(c=>c.id===record.heroId);
  const map=gameCatalog.maps.find(m=>m.id===detail?.arenaId);
  const mode=matchModeName(record.mapId,gameCatalog.modes);
  const result=matchResult(record.rank,record.mapId,gameCatalog.modes);
  return <LibraryModal title="历史对局详情" close={close}>
    <div className="match-detail-hero"><GameImage asset={hero?.image || ''} name={characterName(record.heroId)} /><div><span className="match-eyebrow">{mode} · {matchDate(record.time)}</span><h2>{characterName(record.heroId)} <span className={`match-result-${result.outcome}`}>{result.label}</span></h2><p>{map?.name || '地图信息待回放补全'}{detail?.rounds!=null ? ` · ${detail.rounds} 回合` : ''}{detail?.durationSeconds!=null ? ` · ${Math.floor(detail.durationSeconds/60)} 分 ${detail.durationSeconds%60} 秒` : ''}</p><small>{record.version ? `版本 ${record.version} · ` : ''}回放 {record.replayId || '未提供'}</small></div></div>
    <div className="match-detail-toolbar"><span>{loading ? '正在读取本场历史战绩与回放结算…' : detail?.status==='ready' ? `详情已保存 · ${new Date(detail.observedAt).toLocaleString('zh-CN')}` : '首次读取需要所选账号在游戏中在线，保存后可离线查看。'}</span><button className="btn btn-secondary" disabled={loading || !session} onClick={()=>setRequest(v=>v+1)}>{loading ? '读取中…' : '重新读取'}</button></div>
    {(error || detail?.status==='error') && <div className="library-notice" role="status">{error || detail?.message || detail?.error || '游戏未返回本场详情，请稍后重试。'}</div>}
    {detail?.refreshError&&<div className="library-notice" role="status">重新读取未完成：{detail.refreshError}。当前显示已保存的结算。</div>}
    {detail?.statsError && <div className="match-source-note">{detailErrors[detail.statsError] || `回放统计暂不可用（${detail.statsError}），下方保留服务器提供的战绩。`}</div>}
    {players.length>0 ? <>
      <div className="match-scoreboard">{players.map((p,i)=>{
        const c=gameCatalog.characters.find(c=>c.id===p.heroId);
        const own=p.playerId===pid;
        const playerResult=matchResult(p.rank,record.mapId,gameCatalog.modes);
        return <article className={`match-player${own ? ' own-player' : ''}`} key={`${p.playerId}:${p.slot}`}>
          <div className="match-player-heading"><GameImage asset={c?.image || ''} name={characterName(p.heroId)} /><div><h3>{p.name || `玩家 ${p.playerId}`}{own&&<small>本人</small>}</h3><p>{characterName(p.heroId)} · 对局星级 {p.level}{p.giveUp ? ' · 已退出' : ''}</p></div><div className="match-player-score"><strong>{ratings[i]==null ? '—' : ratings[i]!.toFixed(2)}</strong><small>星枢评分 / 10</small>{ratings[i]!=null && ratings[i]===best && <em>MVP</em>}</div><b className={`match-place match-result-${playerResult.outcome}`}>{playerResult.label}</b></div>
          <dl className="match-metrics">{[['击杀',p.kills],['死亡',p.deaths],['输出',p.damage],['承伤',p.taken],['治疗',p.healing],['累计金币',p.coins],['转账',p.transfer],['行动回合',p.turns],['结算金币',p.gold]].map(([name,value])=><div key={String(name)}><dt>{name}</dt><dd>{metric(value as number | undefined)}</dd></div>)}</dl>
        </article>;
      })}</div>
      <details className="match-rating-method"><summary>评分规则与数据来源</summary><p>{ratingMethod}</p><p>胜负、PVP 名次、对局星级和结算金币来自游戏战绩接口；击杀、伤害、治疗等来自本场回放结算。缺少统计的对局不评分。评分只适合参考本局表现，不用于跨模式排名。</p></details>
    </> : !loading && <div className="library-empty">{session ? '本场尚未保存详情。可重新读取；服务器已过期的历史回放可能无法补全。' : '本场尚未保存详情。启动所选账号的游戏并进入大厅后，可读取并保存。'}</div>}
  </LibraryModal>;
}

export function MatchesPage() {
  const state=useGameLibrary();
  const [filter,setFilter]=useState('all');
  const [mode,setMode]=useState('all');
  const [page,setPage]=useState(0);
  const [selected,setSelected]=useState<PlayedMatch | null>(null);
  useEffect(()=>{setSelected(null);setPage(0);},[state.key]);
  const records=state.account?.snapshot.matches;
  const visible=(records || []).filter(m=>(filter!=='win'||matchResult(m.rank,m.mapId,gameCatalog.modes).outcome==='win')&&(mode==='all'||String(m.mapId)===mode));
  const safePage=Math.min(page,Math.max(0,Math.ceil(visible.length/20)-1));
  const summary=matchHistorySummary(records || [],gameCatalog.modes);
  return <div><LibraryHeader title="对战记录" description="回顾每一场对局，查看参战阵容、结算数据与表现评分" state={state} />
    <LibraryStats items={[{label:'已保存对局',value:records?.length ?? '待同步'},{label:'胜利次数 / 胜率',value:summary.winRate!=null ? `${summary.wins} / ${Math.round(summary.winRate)}%` : '—'},{label:'平均名次（PVP）',value:summary.averagePvpRank?.toFixed(2) ?? '—'},{label:'已保存单场详情',value:records ? `${records.filter(m=>m.detailAvailable).length} / ${records.length}` : '—'}]} />
    <div className="match-source-note">点击对局查看详情。PVE 显示胜负，平均名次仅统计 PVP；胜率按已知结果计算。历史记录和结算按账号保存，未提供的指标显示“—”。星枢评分规则可在详情中查看。</div>
    <div className="library-filters"><select aria-label="战绩筛选" value={filter} onChange={e=>{setFilter(e.target.value);setPage(0);}}><option value="all">全部结果</option><option value="win">仅胜利</option></select><select aria-label="对局模式筛选" value={mode} onChange={e=>{setMode(e.target.value);setPage(0);}}><option value="all">全部模式</option>{[...new Set(records?.map(m=>m.mapId))].map(id=><option key={id} value={id}>{matchModeName(id,gameCatalog.modes)}</option>)}</select><span>{visible.length} 场</span></div>
    <div className="match-record-list">{visible.slice(safePage*20,(safePage+1)*20).map(m=>{
      const c=gameCatalog.characters.find(c=>c.id===m.heroId);
      const result=matchResult(m.rank,m.mapId,gameCatalog.modes);
      return <button className={`match-record-card${result.outcome==='win' ? ' match-win' : ''}`} key={`${m.time}:${m.heroId}:${m.mapId}`} onClick={()=>setSelected(m)}>
        <span className="match-record-rank">{result.label}<small>{result.caption}</small></span><GameImage asset={c?.image || ''} name={characterName(m.heroId)} /><span className="match-record-content"><strong>{characterName(m.heroId)}</strong><span>{matchModeName(m.mapId,gameCatalog.modes)} · {m.version}</span><small>回放 {m.replayId || '未提供'}</small></span><span className="match-record-end"><time>{matchDate(m.time)}</time><span>{m.detailAvailable ? '已保存详情' : '查看对局详情'} <b>↗</b></span></span>
      </button>;
    })}</div>
    {!visible.length&&<div className="library-empty">{!records ? '尚未收到所选账号的真实战绩，请同步游戏数据。' : records.length ? '没有符合条件的对局。' : '服务器返回的战绩为空。'}</div>}
    {visible.length>20&&<div className="library-actions"><button className="btn btn-secondary" disabled={!safePage} onClick={()=>setPage(safePage-1)}>上一页</button><span>{safePage+1} / {Math.ceil(visible.length/20)}</span><button className="btn btn-secondary" disabled={(safePage+1)*20>=visible.length} onClick={()=>setPage(safePage+1)}>下一页</button></div>}
    {selected&&state.account&&<MatchInspector record={selected} state={state} close={()=>setSelected(null)} />}
  </div>;
}
