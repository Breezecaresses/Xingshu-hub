import React, { useCallback, useEffect, useRef, useState } from 'react';
import { invoke, isTauri } from '@tauri-apps/api/core';
import { api } from '../../shared/api/tauri';
import { accountKey, defaultLibraryKey, gameCatalog, plainGameText, platformName, sessionKey } from '../../shared/gameLibrary';
import type { LibraryAccount, LibrarySession } from '../../shared/gameLibrary';
import './library.css';

export function useGameLibrary() {
  const [accounts, setAccounts] = useState<LibraryAccount[]>([]);
  const [sessions, setSessions] = useState<LibrarySession[]>([]);
  const [selected, setSelected] = useState<string | null>(() => localStorage.getItem('library.account'));
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const refresh = useCallback(async () => {
    if (!isTauri()) return;
    try {
      const [a, s] = await Promise.all([invoke<LibraryAccount[]>('get_game_library'), api.getRuntimeSessions()]);
      setAccounts(a); setSessions(s as LibrarySession[]); setError('');
    } catch (e) { setError(typeof e === 'object' && e && 'message' in e ? String(e.message) : String(e)); }
  }, []);
  useEffect(() => {
    void refresh();
    const poll = setInterval(() => void refresh(), 5000);
    return () => { clearInterval(poll); if (timer.current) clearTimeout(timer.current); };
  }, [refresh]);
  const key = defaultLibraryKey(accounts, sessions, selected);
  const account = accounts.find(a => accountKey(a) === key);
  const session = sessions.find(s => sessionKey(s) === key && s.connectionState === 'CONNECTED');
  const select = (value: string) => { setSelected(value); localStorage.setItem('library.account', value); setNotice(''); };
  const sync = async () => {
    if (!session) { setNotice('请启动所选平台的新版星枢插件并登录游戏，再同步账号数据。'); return; }
    setBusy(true); setNotice('已请求同步，正在等待游戏返回数据…');
    const previous = account?.observedAt;
    try {
      await invoke('sync_game_library', { connectionId: session.connectionId });
      timer.current = setTimeout(async () => {
        try {
          const next = await invoke<LibraryAccount[]>('get_game_library'); setAccounts(next);
          const updated = next.find(a => accountKey(a) === key);
          setNotice(updated && updated.observedAt !== previous ? '账号数据已更新。' : '暂未收到新数据。请确认已安装新版运行时并进入大厅；战绩须由游戏服务器返回。');
        } catch { setNotice('读取同步结果失败，请刷新重试。'); }
        setBusy(false);
      }, 11000);
    } catch (e) { setNotice(`同步未完成：${typeof e === 'object' && e && 'message' in e ? e.message : String(e)}`); setBusy(false); }
  };
  return { accounts, sessions, account, session, key, select, refresh, sync, error, notice, busy };
}
export type LibraryState = ReturnType<typeof useGameLibrary>;

export function LibraryHeader({ title, description, state }: { title: string; description: string; state: LibraryState }) {
  const options = new Map(state.accounts.map(a => [accountKey(a), `${platformName(a.platform)} · ${a.snapshot.name || a.snapshot.playerId} (${a.snapshot.playerId})`]));
  state.sessions.filter(s => s.livePlayerId).forEach(s => { if (!options.has(sessionKey(s))) options.set(sessionKey(s), `${platformName(s.platformId)} · ${s.livePlayerId} · 待同步`); });
  return <>
    <header className="library-header"><div><h1>{title}</h1><p>{description}</p></div><div className="library-actions">
      <select aria-label="选择资料账号" value={state.key} onChange={e => state.select(e.target.value)}><option value="">选择账号</option>{[...options].map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select>
      <button className="btn btn-primary" onClick={() => void state.sync()} disabled={state.busy}>{state.busy ? '同步中…' : '同步游戏数据'}</button>
      <button className="btn btn-secondary" onClick={() => void state.refresh()}>刷新</button>
    </div></header>
    <div className="library-source"><span>图鉴：官方配置 {gameCatalog.version}</span><span className={state.session ? 'library-online' : ''}>{state.session ? '所选账号已连接' : '当前显示离线资料'}</span>
      <span>{state.account ? `账号同步于 ${new Date(state.account.observedAt).toLocaleString('zh-CN')}` : '账号尚未同步，持有情况与进度未知'}</span>
    </div>
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
  useEffect(() => { const d = ref.current!; if (!d.open) d.showModal(); }, []);
  return <dialog ref={ref} className="library-modal" aria-label={title} onClose={close} onClick={e => { if (e.target === ref.current) close(); }}>
    <header><h2>{title}</h2><button className="btn btn-secondary" aria-label="关闭详情" onClick={close}>关闭 ×</button></header><div className="library-modal-body">{children}</div>
  </dialog>;
}
export function LibraryStats({ items }: { items: { label: string; value: React.ReactNode }[] }) {
  return <div className="library-stats">{items.map(i => <div className="stat-card" key={i.label}><span>{i.label}</span><strong>{i.value}</strong></div>)}</div>;
}
export function RewardItems({ items }: { items?: Record<string, number> }) {
  return <span>{items && Object.keys(items).length ? Object.entries(items).map(([id, qty]) => `${gameCatalog.itemNames?.[id] || `道具 #${id}`} × ${qty}`).join('、') : '无额外道具奖励'}</span>;
}
