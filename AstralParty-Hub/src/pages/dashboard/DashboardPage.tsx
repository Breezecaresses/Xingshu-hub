import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../shared/api/tauri';
import type { DiagnosticReport, GameInstallation } from '../../shared/types';
import { gameCatalog, platformName } from '../../shared/gameLibrary';
import { isTauriRuntime } from '../../shared/runtime';
import { useWebCloudState } from '../../shared/useWebCloudState';
import { LibraryHeader, LibraryStats, useGameLibrary } from '../player/LibraryUI';
import { DashboardHelpCard } from './DashboardHelpCard';

export function DashboardPage() {
  const state = useGameLibrary();
  const webCloud = useWebCloudState();
  const webMode = !isTauriRuntime();
  const activeAccount = state.account || (webMode ? webCloud.libraryAccount : null);
  const [game,setGame] = useState<GameInstallation|null>(null);
  const [report,setReport] = useState<DiagnosticReport|null>(null);
  const [failure,setFailure] = useState('');
  const detect = async () => {
    if (webMode) return;
    try { setGame(await api.detectGame()); setReport(await api.getDiagnosticReport()); setFailure(''); }
    catch(e) { setFailure(`探测未完成：${e && typeof e==='object' && 'message' in e ? e.message : String(e)}`); }
  };
  useEffect(()=>{
    if (webMode) return;
    let active=true;
    const read=async()=>{
      const [g,r]=await Promise.allSettled([api.getPrimaryGame(),api.getDiagnosticReport()]);
      if(!active)return;
      setGame(g.status==='fulfilled'?g.value:null);setReport(r.status==='fulfilled'?r.value:null);
    };
    void read(); const timer=setInterval(()=>void read(),10000);
    return()=>{active=false;clearInterval(timer);};
  },[webMode]);
  const characters=gameCatalog.characters.filter(c=>c.name.trim());
  const connected=state.sessions.filter(s=>s.connectionState==='CONNECTED');
  return <div>
    <LibraryHeader title="总览控制台" description={webMode ? '星枢网页版：回放深度解析、公开 UID 查询与图鉴中心' : '游戏连接、账号进度与离线图鉴'} state={state} />
    <LibraryStats className="dashboard-stats-grid" items={[{label:'服务形态',value:webMode?'网页版在线':game?.is_valid?'已检测到游戏':game?'需校验':'未检测到'},{label:webMode?'云端账号':'在线运行时',value:webMode?(webCloud.account?`${webCloud.account.nick}`:'免登录可用'):connected.length ? `${connected.length} 个连接` : '未连接'},{label:'玩家等级',value:activeAccount?`Lv.${activeAccount.snapshot.level}`:'待查询'},{label:'角色图鉴',value:characters.length},{label:'卡牌图鉴',value:gameCatalog.cards.length},{label:'已保存战绩',value:activeAccount?.snapshot.matches?.length ?? (webMode?'支持查UID':'待同步')}]} />
    {webMode ? (
      <div className="library-summary">
        <h2>星枢网页版工作台</h2>
        <p>直接在浏览器中使用公开 UID 资料与战绩查询、全帧深度回放解析（.rep 上传 / 对局编号直连）、官方完整图鉴（角色 / 卡牌 / 筹码 / 地图），以及扫码登录星枢云端查看个人档案。</p>
        <div className="library-actions" style={{marginTop:14}}>
          <Link className="btn btn-primary" to="/player/replay">回放分析</Link>
          <Link className="btn btn-secondary" to="/player/matches">公开 UID 战绩</Link>
          <Link className="btn btn-secondary" to="/player/accounts">星枢云端登录</Link>
        </div>
      </div>
    ) : (
      <div className="library-summary">
        <h2>当前游戏环境</h2>
        <p>平台：{game?platformName(game.platform.toUpperCase()):'待探测'} · 安装目录：{game?.install_path || '尚未设置游戏目录'}</p>
        <p>运行时：{connected.length?'已通过本机网桥连接':report?.snapshot.xingshu_detected?'检测到加载器文件，尚未确认运行时连接':'等待游戏启动并连接星枢'}{game?.detected_build_id ? ` · Build ${game.detected_build_id}` : ''}</p>
        <div className="library-actions" style={{marginTop:14}}>
          <button className="btn btn-primary" onClick={()=>void detect()}>探测游戏</button>
          <Link className="btn btn-secondary" to="/settings">设置游戏目录</Link>
          <Link className="btn btn-secondary" to="/diagnostics?section=runtime">查看运行时</Link>
        </div>
      </div>
    )}
    {failure&&<div className="library-notice">{failure}</div>}
    <DashboardHelpCard />
    <div className="library-summary">
      <h2>资料状态</h2>
      <p>图鉴来自官方游戏 {gameCatalog.version} 原版配置，可离线浏览。{webMode ? '公开 UID 资料与回放解析直接由星枢云端提供服务。' : '账号数据通过已登录游戏的星枢运行时同步，关闭游戏后保留上次资料。'}</p>
      <p>{activeAccount?`所选资料：${activeAccount.snapshot.name} · ${platformName(activeAccount.platform)} · ${activeAccount.snapshot.playerId}`:'暂无已选择的账号资料。可使用免登录 UID 查询或登录星枢云端同步。'}</p>
    </div>
  </div>;
}
