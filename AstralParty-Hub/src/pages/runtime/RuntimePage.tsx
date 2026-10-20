import { useCallback, useEffect, useRef, useState } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { Link } from 'react-router-dom';
import { Activity, Cpu, FileCheck2, Radio, RefreshCw } from 'lucide-react';
import { ExtensionTargetPicker, useExtensionTarget, type Inventory } from '../extensions/ExtensionWorkspace';
import { selectedRuntimeSessions } from './selectedRuntimeSessions';

interface Session {
  connectionId: string; platformId: string; pid: number; executablePath?: string;
  runtimeVersion?: string; boundRole?: string; connectionState: string; lastSeenAt?: string;
}
interface ProcessState { processIds: number[]; bepInExFilesDetected: boolean }
const errorText = (error: unknown) => error && typeof error === 'object' && 'message' in error ? String(error.message) : String(error);

export function RuntimePage() {
  const target = useExtensionTarget();
  const game = target.selected;
  const [inventory, setInventory] = useState<Inventory | null>(null);
  const [process, setProcess] = useState<ProcessState | null>(null);
  const [sessions, setSessions] = useState<Session[]>([]);
  const [liveId, setLiveId] = useState('');
  const [loadedId, setLoadedId] = useState('');
  const [error, setError] = useState('');
  const [liveError, setLiveError] = useState('');
  const [busy, setBusy] = useState(false);
  const [opening, setOpening] = useState(false);
  const request = useRef(0);
  const refresh = useCallback(async () => {
    const ticket = ++request.current;
    setInventory(null); setLoadedId(''); setError('');
    if (!game) { setBusy(false); return; }
    setBusy(true);
    try {
      const result = await invoke<Inventory>('get_extension_inventory', { installationId: game.id });
      if (ticket === request.current) { setInventory(result); setLoadedId(game.id); }
    } catch (error) { if (ticket === request.current) setError(errorText(error)); }
    finally { if (ticket === request.current) setBusy(false); }
  }, [game?.id]);
  useEffect(() => { void refresh(); return () => { request.current++; }; }, [refresh]);
  useEffect(() => {
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout>;
    setLiveId(''); setProcess(null); setSessions([]); setLiveError('');
    const poll = async () => {
      if (!game) return;
      const results = await Promise.allSettled([
        invoke<Session[]>('get_runtime_sessions'),
        invoke<ProcessState>('get_extension_runtime_state', { installationId: game.id }),
      ]);
      if (cancelled) return;
      setLiveId(game.id);
      setSessions(results[0].status === 'fulfilled' ? selectedRuntimeSessions(results[0].value, game.platform, game.executable_path) : []);
      setProcess(results[1].status === 'fulfilled' ? results[1].value : null);
      setLiveError(results.filter(result => result.status === 'rejected').map(result => errorText((result as PromiseRejectedResult).reason)).join('；'));
      timer = setTimeout(poll, 3000);
    };
    void poll();
    return () => { cancelled = true; clearTimeout(timer); };
  }, [game?.id, game?.platform, game?.executable_path]);
  const current = loadedId === game?.id ? inventory : null;
  const currentProcess = liveId === game?.id ? process : null;
  const currentSessions = liveId === game?.id ? sessions : [];
  const runtime = current?.entries.find(entry => entry.id === 'builtin.runtime');
  const supported = ['STEAM_CN', 'TAPTAP'].includes(target.channel);
  const openInstaller = async () => {
    if (!game) return;
    setOpening(true); setError('');
    try { await invoke('open_group_installer', { installationId: game.id }); }
    catch (error) { setError(errorText(error)); }
    finally { setOpening(false); }
  };
  return <div>
    <header className="extension-header"><div><h1>星枢运行时管理器</h1><p>按实际安装目录查看运行时文件、游戏进程与实时连接</p></div><button className="btn btn-secondary" disabled={busy || opening} onClick={() => void refresh()}><RefreshCw size={14} />重新校验文件</button></header>
    <ExtensionTargetPicker target={target} disabled={opening} />
    {game ? <>
      <div className="runtime-status-grid">
        <section className="panel"><FileCheck2 size={21} /><span>本地运行时文件</span><strong>{busy ? '正在校验…' : runtime?.version || (current ? '未识别为本版运行时' : '尚未读取')}</strong><small>{runtime?.status || '文件校验与实时连接分别显示'}</small></section>
        <section className="panel"><Activity size={21} /><span>所选目录的游戏进程</span><strong>{currentProcess ? currentProcess.processIds.length ? '游戏运行中' : '游戏未运行' : liveError ? '读取失败' : '正在检测…'}</strong><small>{currentProcess?.processIds.length ? `PID：${currentProcess.processIds.join('、')}` : game.executable_path}</small></section>
        <section className="panel"><Radio size={21} /><span>当前连接上报版本</span><strong>{currentSessions.length ? currentSessions.map(session => session.runtimeVersion || '未上报版本').join(' / ') : '尚无已确认连接'}</strong><small>仅匹配当前渠道与此目录的游戏程序</small></section>
        <section className="panel"><Cpu size={21} /><span>本版运行时安装支持</span><strong>{supported ? '已提供配套安装文件' : '尚未提供验证版本'}</strong><small>{supported ? '可使用安装助手安装、校验和还原' : '此渠道支持目录与兼容扩展管理'}</small></section>
      </div>
      <section className="panel extension-runtime"><Radio size={24} /><div><h2>运行时连接</h2><p>{currentSessions.length ? '已收到所选游戏程序的实时连接。下方版本由运行时上报。' : '游戏进程存在或磁盘文件匹配，不代表运行时已连接；连接成功后将显示上报版本。'}</p>
        <div className="runtime-sessions">{currentSessions.map(session => <div key={session.connectionId}><strong>PID {session.pid}</strong><span>{session.runtimeVersion || '未上报版本'}</span><span>{({ MAIN: '主游戏', USER_SPECTATOR: '观战端', UNBOUND: '尚未绑定账号' } as Record<string, string>)[session.boundRole || ''] || session.boundRole || '尚未绑定账号'}</span>{session.lastSeenAt && <small>最近通信：{session.lastSeenAt}</small>}</div>)}</div>
      </div></section>
      <section className="panel extension-runtime"><Cpu size={24} /><div><h2>安装与维护</h2><p>{supported ? '安装助手会带入当前渠道和所选目录，并保留安装前备份。更新后需重新启动游戏，才能加载新运行时。' : '本版尚未提供此渠道的验证运行时；可在包管理器中管理明确声明支持此渠道的扩展。'}</p><p>{currentProcess?.bepInExFilesDetected ? '此目录检测到 BepInEx/core 文件；是否加载以对应运行日志为准。' : '可在运行时插件页查看独立插件与外部文件。'}</p></div>{supported && <button className="btn btn-primary" disabled={opening || !current?.installerAvailable} onClick={() => void openInstaller()}>打开安装助手</button>}</section>
      {supported && current && !current.installerAvailable && <p className="extension-hint">请完整解压发布 ZIP，并从其中的 Hub 文件夹启动，以使用配套安装助手。</p>}
      <div className="extension-tags"><Link className="btn btn-secondary" to="/extensions/plugins">运行时插件</Link><Link className="btn btn-secondary" to="/extensions/mods">游戏模组</Link></div>
    </> : <div className="panel extension-empty"><Cpu size={32} /><h2>先配置此渠道的游戏位置</h2><p>选择或添加实际游戏目录后，即可查看文件校验和运行时连接。</p></div>}
    {(error || liveError || current?.warnings.length) && <p className="extension-hint" role="status">{[error, liveError, ...(current?.warnings || [])].filter(Boolean).join('；')}</p>}
  </div>;
}
