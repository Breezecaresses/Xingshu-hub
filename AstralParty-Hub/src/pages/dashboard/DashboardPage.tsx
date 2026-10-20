import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../shared/api/tauri';
import type { DiagnosticReport, GameInstallation } from '../../shared/types';
import { gameCatalog, platformName } from '../../shared/gameLibrary';
import { LibraryHeader, LibraryStats, useGameLibrary } from '../player/LibraryUI';

export function DashboardPage() {
  const state = useGameLibrary();
  const [game,setGame] = useState<GameInstallation|null>(null);
  const [report,setReport] = useState<DiagnosticReport|null>(null);
  const [failure,setFailure] = useState('');
  const detect = async () => {
    try { setGame(await api.detectGame()); setReport(await api.getDiagnosticReport()); setFailure(''); }
    catch(e) { setFailure(`探测未完成：${e && typeof e==='object' && 'message' in e ? e.message : String(e)}`); }
  };
  useEffect(()=>{
    let active=true;
    const read=async()=>{
      const [g,r]=await Promise.allSettled([api.getPrimaryGame(),api.getDiagnosticReport()]);
      if(!active)return;
      setGame(g.status==='fulfilled'?g.value:null);setReport(r.status==='fulfilled'?r.value:null);
    };
    void read(); const timer=setInterval(()=>void read(),10000);
    return()=>{active=false;clearInterval(timer);};
  },[]);
  const characters=gameCatalog.characters.filter(c=>c.name.trim());
  const catalog=gameCatalog as typeof gameCatalog & {chips?:unknown[]};
  const connected=state.sessions.filter(s=>s.connectionState==='CONNECTED');
  return <div>
    <LibraryHeader title="总览控制台" description="游戏连接、账号进度与离线图鉴" state={state} />
    <LibraryStats items={[{label:'游戏环境',value:game?.is_valid?'已检测到':game?'需校验':'未检测到'},{label:'在线运行时',value:connected.length ? `${connected.length} 个连接` : '未连接'},{label:'玩家等级',value:state.account?`Lv.${state.account.snapshot.level}`:'待同步'},{label:'角色图鉴',value:characters.length},{label:'卡牌图鉴',value:gameCatalog.cards.length},{label:'已保存战绩',value:state.account?.snapshot.matches?.length ?? '待同步'}]} />
    <div className="library-summary"><h2>当前游戏环境</h2><p>平台：{game?platformName(game.platform.toUpperCase()):'待探测'}</p><p>安装目录：{game?.install_path || '尚未设置游戏目录'}</p><p>Build ID：{game?.detected_build_id || '未识别'}</p><p>运行时：{connected.length?'已通过本机网桥连接':report?.snapshot.apx_detected?'检测到加载器文件，尚未确认运行时连接':'等待游戏启动并连接星枢'}</p><div className="library-actions" style={{marginTop:16}}><button className="btn btn-primary" onClick={()=>void detect()}>探测游戏</button><Link className="btn btn-secondary" to="/settings">设置游戏目录</Link><Link className="btn btn-secondary" to="/runtime">查看运行时</Link></div></div>
    {failure&&<div className="library-notice">{failure}</div>}
    <div className="library-links"><Link to="/player/profile">玩家资料<small>等级、收藏与账号同步</small></Link><Link to="/player/characters">角色中心<small>{characters.length} 个角色 · {characters.reduce((n,c)=>n+c.skins.length,0)} 款装扮 · 羁绊奖励</small></Link><Link to="/player/cards">卡牌图鉴<small>{gameCatalog.cards.length} 张卡牌 · {gameCatalog.altArts.length} 张异画 · 完整效果</small></Link><Link to="/player/chips">筹码图鉴<small>{catalog.chips?.length || 0} 项合作挑战筹码 · 品质与完整效果</small></Link><Link to="/player/maps">地图详情<small>地图介绍 · 模式规则 · 难度与场地效果</small></Link><Link to="/player/matches">对战记录<small>历史结算 · 玩家表现 · 星枢评分</small></Link></div>
    <div className="library-summary" style={{marginTop:20}}><h2>资料状态</h2><p>图鉴来自官方游戏 {gameCatalog.version} 原版配置，可离线浏览。账号数据通过已登录游戏的星枢运行时同步，关闭游戏后保留上次资料。</p><p>{state.account?`所选资料：${state.account.snapshot.name} · ${platformName(state.account.platform)} · ${state.account.snapshot.playerId}`:'暂无已选择的账号资料。登录新版运行时后可同步，观战账号的资料不会自动替代主账号。'}</p></div>
  </div>;
}
