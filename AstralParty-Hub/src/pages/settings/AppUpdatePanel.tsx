import { useEffect, useRef, useState } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { openUrl } from '@tauri-apps/plugin-opener';
import { RefreshCw, Download } from 'lucide-react';
import { PROJECT_SOURCE } from '../../shared/projectSource';

type Release = { version: string; sequence: number; notes: string; desktop: { size: number }; android: { versionCode: number } };
type Result = { available: boolean; currentVersion: string; canApply: boolean; release: Release | null; releaseUrl: string; message: string };
export function AppUpdatePanel() {
  const [version, setVersion] = useState('读取中…');
  const [preview, setPreview] = useState(true);
  const [result, setResult] = useState<Result | null>(null);
  const [phase, setPhase] = useState<'idle' | 'checking' | 'downloading' | 'ready' | 'applying'>('idle');
  const [error, setError] = useState('');
  const gate = useRef(false);
  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    void invoke<{ version: string }>('get_app_build').then(b => { if (alive.current) setVersion(b.version); })
      .catch(() => { if (alive.current) setVersion('无法读取构建版本'); });
    return () => { alive.current = false; };
  }, []);
  const act = async (kind: 'check' | 'download' | 'apply') => {
    if (gate.current) return;
    gate.current = true; setError('');
    setPhase(kind === 'check' ? 'checking' : kind === 'download' ? 'downloading' : 'applying');
    try {
      if (kind === 'apply') await invoke('apply_app_update');
      else {
        const next = await invoke<Result>(kind === 'check' ? 'check_app_update' : 'prepare_app_update', {
          preview, ...(kind === 'download' ? { sequence: result?.release?.sequence } : {}),
        });
        if (alive.current) { setResult(next); setPhase(kind === 'download' ? 'ready' : 'idle'); }
      }
    } catch (e) { if (alive.current) {
      const message = e instanceof Error ? e.message : typeof e === 'object' && e !== null && 'message' in e ? String(e.message) : String(e);
      setError(message); setPhase('idle');
    } }
    finally { gate.current = false; }
  };
  const busy = ['checking', 'downloading', 'applying'].includes(phase);
  return <section className="panel">
    <div className="panel-header">
      <h2 className="panel-title"><RefreshCw size={18} color="var(--primary)" />软件更新</h2>
      <button className="btn btn-secondary" disabled={busy} onClick={() => void act('check')}>
        {phase === 'checking' ? '正在检查…' : '检查更新'}
      </button>
    </div>
    <p style={{ color: 'var(--text-secondary)', lineHeight: 1.8 }}>当前 Hub：{version}</p>
    <label style={{ display: 'flex', alignItems: 'center', gap: 8, margin: '10px 0' }}>
      <input type="checkbox" checked={preview} disabled={busy} onChange={e => { setPreview(e.target.checked); setResult(null); setPhase('idle'); }} />包含测试版
    </label>
    <p style={{ color: 'var(--text-muted)', lineHeight: 1.8 }}>这里只更新 Hub。手机安装助手请在手机上点“检查助手更新”，新版 APK 需要确认安装。游戏内插件在“星枢包管理器”或手机助手中单独更新。</p>
    {result && <div style={{ marginTop: 14, lineHeight: 1.8 }}>
      <p>{result.message}</p>
      {result.available && result.release && <>
        <p><strong>新版本 {result.release.version}</strong> · {(result.release.desktop.size / 1048576).toFixed(1)} MB</p>
        <p style={{ whiteSpace: 'pre-wrap', color: 'var(--text-secondary)' }}>{result.release.notes}</p>
        <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', marginTop: 12 }}>
          {phase === 'ready'
            ? <button className="btn btn-primary" disabled={busy} onClick={() => void act('apply')}>更新并重启 Hub</button>
            : <button className="btn btn-primary" disabled={busy || !result.canApply} onClick={() => void act('download')}><Download size={14} />{phase === 'downloading' ? '下载和校验中，请稍候…' : '下载更新'}</button>}
        </div>
        <p style={{ color: 'var(--text-muted)', marginTop: 10 }}>{result.canApply ? '请等待安装或还原任务完成，再更新并重启。更新时会暂停本包的手机连接服务，保留配对身份；下次连接手机时服务自动启动。更新前会备份本包文件。' : '请完整解压电脑版 ZIP，并从其中的 Hub 文件夹启动后更新。'}</p>
      </>}
    </div>}
    {error && <p role="alert" style={{ marginTop: 12, color: 'var(--danger)' }}>未完成：{error}。可重试或从官方发布页下载。</p>}
    <button className="btn btn-secondary btn-sm" style={{ marginTop: 14 }} onClick={() => void openUrl(PROJECT_SOURCE + '/releases')}>打开官方发布页 ↗</button>
  </section>;
}
