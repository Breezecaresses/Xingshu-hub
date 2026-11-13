import React, { useCallback, useEffect, useRef, useState } from 'react';
import { invoke, isTauri } from '@tauri-apps/api/core';
import { api } from '../../shared/api/tauri';
import { accountKey, defaultLibraryKey, gameCatalog, plainGameText, platformName, sessionKey } from '../../shared/gameLibrary';
import type { LibraryAccount, LibrarySession } from '../../shared/gameLibrary';
import {cloudMatchesAccount,mergeCloudLibrary} from '../../shared/cloudLibrary';
import './library.css';

type CloudLibrary = {identity?: {uid: string; nick: string} | null; account?: LibraryAccount | null;
  sync?: {status: string; stage: string}};

export function useGameLibrary() {
  const [accounts, setAccounts] = useState<LibraryAccount[]>([]);
  const [sessions, setSessions] = useState<LibrarySession[]>([]);
  const [selected, setSelected] = useState<string | null>(() => localStorage.getItem('library.account'));
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState(false);
  const [cloud, setCloud] = useState<CloudLibrary>({});
  const refreshing = useRef(false);
  const localAccounts = useRef<LibraryAccount[]>([]);
  const syncGeneration = useRef(0);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const refresh = useCallback(async () => {
    if (!isTauri() || refreshing.current) return;
    refreshing.current = true;
    try {
      const [local, live, remote] = await Promise.allSettled([invoke<LibraryAccount[]>('get_game_library'), api.getRuntimeSessions(), invoke<CloudLibrary>('get_cloud_game_library')]);
      if (live.status==='fulfilled') setSessions(live.value as LibrarySession[]);
      if (remote.status==='fulfilled') setCloud(remote.value);
      if (local.status==='fulfilled') {localAccounts.current=local.value;setAccounts(mergeCloudLibrary(local.value,remote.status==='fulfilled'?remote.value.account:null));}
      setError(local.status==='rejected' ? '本机资料暂未读取成功，请刷新重试。' : remote.status==='rejected' ? '云端资料暂未读取成功；本机资料仍可查看。' : '');
    } catch (e) { setError(typeof e === 'object' && e && 'message' in e ? String(e.message) : String(e)); }
    finally { refreshing.current=false; }
  }, []);
  useEffect(() => {
    void refresh();
    const poll = setInterval(() => void refresh(), 5000);
    return () => { clearInterval(poll); syncGeneration.current++; if (timer.current) clearTimeout(timer.current); };
  }, [refresh]);
  const linkedCloud=accounts.find(a=>a.role==='MAIN'&&cloudMatchesAccount(cloud.identity?.uid,a))||accounts.find(a=>cloudMatchesAccount(cloud.identity?.uid,a));
  const cloudKey = cloud.identity ? linkedCloud?accountKey(linkedCloud):`PHONE_CN:${cloud.identity.uid}` : '';
  const selectedCloud=!!cloud.identity&&selected===`PHONE_CN:${cloud.identity.uid}`;
  const key = selectedCloud ? cloudKey : defaultLibraryKey(accounts, sessions, selected);
  const account = accounts.find(a => accountKey(a) === key);
  const isCloud = !!cloud.identity && (cloudMatchesAccount(cloud.identity.uid,account)||key===cloudKey);
  const session = sessions.filter(s => sessionKey(s) === key && s.connectionState === 'CONNECTED')
    .sort((a, b) => (b.lastSeenAt || '').localeCompare(a.lastSeenAt || ''))[0];
  const select = (value: string) => { syncGeneration.current++; if (timer.current) clearTimeout(timer.current); setBusy(false); setSelected(value); localStorage.setItem('library.account', value); setNotice(''); };
  const sync = async () => {
    if (isCloud&&!session) {
      const generation=++syncGeneration.current;
      setBusy(true);setNotice('正在请求云端官方同步，请先退出同账号游戏…');
      try {
        await invoke('sync_cloud_game_library');
        const deadline=Date.now()+210000;
        const poll=async()=>{
          if(generation!==syncGeneration.current)return;
          try {
            const result=await invoke<CloudLibrary>('get_cloud_game_library');
            if(generation!==syncGeneration.current)return;
            setCloud(result);
            if(result.account)setAccounts(mergeCloudLibrary(localAccounts.current,result.account));
            setNotice(result.sync?.stage||'等待官方数据…');
            if(result.sync?.status!=='running'||Date.now()>deadline){setBusy(false);return;}
            timer.current=setTimeout(()=>void poll(),1500);
          }catch{if(generation===syncGeneration.current){setNotice('云端同步状态暂未读取成功，稍后点刷新可查看。');setBusy(false);}}
        };
        await poll();
      }catch{if(generation===syncGeneration.current){setNotice('云端同步未启动：请确认已登录；重复同步需间隔 60 秒。');setBusy(false);}}
      return;
    }
    if (!session) { setNotice('请启动所选平台的新版星枢插件并登录游戏，再同步账号数据。'); return; }
    setBusy(true); setNotice('已请求同步，正在等待游戏返回数据…');
    const previous = account?.observedAt;
    const generation = ++syncGeneration.current;
    const deadline = Date.now() + 45000;
    const waitForData = async () => {
      if (generation !== syncGeneration.current) return;
      try {
        const next = await invoke<LibraryAccount[]>('get_game_library');
        if (generation !== syncGeneration.current) return;
        setAccounts(next);
        const updated = next.find(a => accountKey(a) === key);
        if (updated && updated.observedAt !== previous) {
          setNotice('账号数据已更新。结算后的新战绩会继续自动同步。'); setBusy(false); return;
        }
        if (Date.now() >= deadline) {
          setNotice('暂未收到新数据。请确认所选账号已进入大厅；服务器延迟返回的新战绩会继续自动同步。'); setBusy(false); return;
        }
        timer.current = setTimeout(() => void waitForData(), 1000);
      } catch { if (generation === syncGeneration.current) { setNotice('读取同步结果失败，请刷新重试。'); setBusy(false); } }
    };
    try {
      await invoke('sync_game_library', { connectionId: session.connectionId });
      if (generation === syncGeneration.current) timer.current = setTimeout(() => void waitForData(), 1000);
    } catch (e) { if (generation === syncGeneration.current) { setNotice(`同步未完成：${typeof e === 'object' && e && 'message' in e ? e.message : String(e)}`); setBusy(false); } }
  };
  return { accounts, sessions, account, session, key, select, refresh, sync, error, notice, busy, cloud, cloudKey, isCloud };
}
export type LibraryState = ReturnType<typeof useGameLibrary>;

export function LibraryHeader({ title, description, state }: { title: string; description: string; state: LibraryState }) {
  const options = new Map(state.accounts.map(a => [accountKey(a), `${platformName(a.platform)} · ${a.snapshot.name || a.snapshot.playerId} (${a.snapshot.playerId})`]));
  if(state.cloud.identity&&!options.has(state.cloudKey))options.set(state.cloudKey,`手机号账号 · ${state.cloud.identity.nick} (${state.cloud.identity.uid}) · 待同步`);
  state.sessions.filter(s => s.livePlayerId).forEach(s => { if (!options.has(sessionKey(s))) options.set(sessionKey(s), `${platformName(s.platformId)} · ${s.livePlayerId} · 待同步`); });
  return <>
    <header className="library-header"><div><h1 className="aurora-sheen-text">{title}</h1><p>{description}</p></div><div className="library-actions">
      <select aria-label="选择资料账号" value={state.key} onChange={e => state.select(e.target.value)}><option value="">选择账号</option>{[...options].map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select>
      <button className="btn btn-primary" onClick={() => void state.sync()} disabled={state.busy}>{state.busy ? '同步中…' : '同步游戏数据'}</button>
      <button className="btn btn-secondary" onClick={() => void state.refresh()}>刷新</button>
    </div></header>
    <div className="library-source"><span>图鉴：官方配置 {gameCatalog.version}</span><span className={state.session || state.isCloud ? 'library-online' : ''}>{state.session ? '所选账号游戏已连接' : state.isCloud ? '同 UID 云端账号已授权 · 可直接同步' : '当前显示离线资料'}</span>
      <span>{state.account ? `账号同步于 ${new Date(state.account.observedAt).toLocaleString('zh-CN')}` : '账号尚未同步，持有情况与进度未知'}</span>
    </div>
    {state.isCloud&&<div className="library-source">点击同步会短暂登录所选游戏账号，请先退出该账号的游戏。授权页只负责登录，不会自动同步或观战。</div>}
    {(state.error || state.notice) && <div className="library-notice" role="status">{state.error || state.notice}</div>}
  </>;
}

export function GameImage({ asset, name, className = '' }: { asset: string; name: string; className?: string }) {
  const src = gameCatalog.assets[asset];
  return src ? <img className={`library-image ${className}`} src={src} alt={name} loading="lazy" /> : <div className={`library-image library-image-missing ${className}`}>暂无图片</div>;
}
export function GameText({ text }: { text?: string }) { return <p className="library-text">{plainGameText(text || '官方配置暂未提供说明。')}</p>; }
export function LibraryModal({ title, close, children }: { title: string; close: () => void; children: React.ReactNode }) {
  const ref = useRef<HTMLDialogElement>(null);
  const startedOutside = useRef(false);
  useEffect(() => { const d = ref.current!; if (!d.open) d.showModal(); }, []);
  const isOutside = (x: number, y: number) => {
    const bounds = ref.current?.getBoundingClientRect();
    return !!bounds && (x < bounds.left || x > bounds.right || y < bounds.top || y > bounds.bottom);
  };
  return <dialog ref={ref} className="library-modal" aria-label={title} onClose={close}
    onPointerDown={e => { startedOutside.current = e.target === ref.current && isOutside(e.clientX, e.clientY); }}
    onPointerCancel={() => { startedOutside.current = false; }}
    onClick={e => {
      if (startedOutside.current && e.target === ref.current && isOutside(e.clientX, e.clientY)) close();
      startedOutside.current = false;
    }}>
    <header><h2>{title}</h2><button className="btn btn-secondary" aria-label="关闭详情" onClick={close}>关闭 ×</button></header><div className="library-modal-body">{children}</div>
  </dialog>;
}
export function LibraryStats({ items }: { items: { label: string; value: React.ReactNode }[] }) {
  return <div className="library-stats">{items.map(i => <div className="stat-card" key={i.label}><span>{i.label}</span><strong>{i.value}</strong></div>)}</div>;
}
export function RewardItems({ items }: { items?: Record<string, number> }) {
  return <span>{items && Object.keys(items).length ? Object.entries(items).map(([id, qty]) => `${gameCatalog.itemNames?.[id] || `道具 #${id}`} × ${qty}`).join('、') : '无额外道具奖励'}</span>;
}
