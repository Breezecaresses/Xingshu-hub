import { useCallback, useEffect, useRef, useState } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { Link } from 'react-router-dom';
import { FolderOpen, Package, Puzzle, RefreshCw, Trash2, Upload } from 'lucide-react';
import { api } from '../../shared/api/tauri';
import type { GameInstallation } from '../../shared/types';
import { AndroidInstallPanel } from './AndroidInstallPanel';
import { ExternalModsPanel } from './ExternalModsPanel';
import { ImportPackagePage } from './ImportPackagePage';
import './extensions.css';

export const extensionChannels = [['STEAM_CN', 'Steam 国服'], ['STEAM_GLOBAL', 'Steam 国际服'], ['TAPTAP', 'TapTap Windows'], ['BILIBILI', '哔哩哔哩'], ['WEGAME', 'WeGame']] as const;
export interface ExtensionEntry { id: string; packageId: string; name: string; category: string; version: string | null; path: string; source: string; status: string; description: string; managed: boolean }
export interface Inventory { entries: ExtensionEntry[]; pluginDirectory: string; modDirectory: string; resourceDirectory: string; warnings: string[]; installerAvailable: boolean }
const message = (e: unknown) => e && typeof e === 'object' && 'message' in e ? String(e.message) : String(e);
export const categoryName = (category: string) => ({ 'xingshu-plugin': 'DLL / 功能插件', mod: '游戏模组', 'resource-pack': '资源包' }[category] || category);

export function useExtensionTarget() {
  const [channel, setChannelState] = useState(() => localStorage.getItem('extensions.channel') || 'STEAM_CN');
  const [targets, setTargets] = useState<GameInstallation[]>([]);
  const [id, setId] = useState(() => localStorage.getItem(`extensions.target.${channel}`) || '');
  const [path, setPath] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const refresh = useCallback(async () => {
    setBusy(true);
    try { setTargets(await invoke<GameInstallation[]>('list_extension_targets')); setError(''); }
    catch (e) { setError(message(e)); } finally { setBusy(false); }
  }, []);
  useEffect(() => { void refresh(); }, [refresh]);
  const options = targets.filter(g => g.platform === channel);
  const selected = options.find(g => g.id === id) || options[0];
  const select = (value: string) => { setId(value); localStorage.setItem(`extensions.target.${channel}`, value); };
  const setChannel = (value: string) => { setChannelState(value); localStorage.setItem('extensions.channel', value); setId(localStorage.getItem(`extensions.target.${value}`) || ''); setPath(''); setError(''); };
  const save = async () => {
    if (!path.trim()) return;
    setBusy(true); setError('');
    try {
      const game = await invoke<GameInstallation>('register_extension_target', { platformId: channel, path: path.trim() });
      setTargets(old => [...old.filter(g => g.id !== game.id), game]); select(game.id); setPath('');
    } catch (e) { setError(message(e)); } finally { setBusy(false); }
  };
  return { channel, setChannel, targets, options, selected, select, path, setPath, error, busy, save, refresh };
}
export function ExtensionTargetPicker({ target, disabled = false, purpose = 'install' }: { target: ReturnType<typeof useExtensionTarget>; disabled?: boolean; purpose?: 'install' | 'diagnose' }) {
  return <section className="panel extension-target" aria-label="扩展安装位置">
    <div className="extension-target-title"><FolderOpen size={19} /><strong>{purpose === 'diagnose' ? '检查渠道与游戏目录' : '渠道与安装位置'}</strong><span>{purpose === 'diagnose' ? '仅检查下方选定目录的文件、进程与连接' : '扩展只安装到下方选定目录'}</span></div>
    <div className="extension-channels">{extensionChannels.map(([id, name]) => <button key={id} className={`btn ${target.channel === id ? 'btn-primary' : 'btn-secondary'}`} onClick={() => target.setChannel(id)} disabled={disabled}>{name}<small>{target.targets.filter(g => g.platform === id).length || '未配置'}</small></button>)}</div>
    <div className="extension-location"><select aria-label="选择游戏安装位置" value={target.selected?.id || ''} disabled={disabled || !target.options.length} onChange={e => target.select(e.target.value)}>{!target.options.length && <option value="">此渠道尚未配置安装位置</option>}{target.options.map(g => <option key={g.id} value={g.id}>{g.install_path}</option>)}</select><button className="btn btn-secondary" disabled={target.busy || disabled} onClick={() => void target.refresh()}><RefreshCw size={14} />重新检测</button></div>
    <form className="extension-location" onSubmit={e => { e.preventDefault(); void target.save(); }}><input aria-label="添加游戏安装路径" value={target.path} disabled={disabled} onChange={e => target.setPath(e.target.value)} placeholder="添加另一安装位置：粘贴实际游戏 exe 或所在文件夹的完整路径" /><button className="btn btn-secondary" disabled={target.busy || disabled || !target.path.trim()}>保存此位置</button></form>
    {target.selected && <p className="extension-path">实际游戏程序：<code>{target.selected.executable_path}</code></p>}
    {target.error && <p className="extension-error" role="alert">{target.error}</p>}
  </section>;
}

export function ExtensionManager({ view }: { view: 'packages' | 'mods' }) {
  const target = useExtensionTarget();
  const [inventory, setInventory] = useState<Inventory | null>(null);
  const [loadedId, setLoadedId] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [actionBusy, setActionBusy] = useState(false);
  const [query, setQuery] = useState('');
  const [importOpen, setImportOpen] = useState(false);
  const request = useRef(0);
  const refresh = useCallback(async () => {
    const ticket = ++request.current;
    setInventory(null); setLoadedId(''); setError('');
    if (!target.selected) { setBusy(false); return; }
    setBusy(true);
    try { const result = await invoke<Inventory>('get_extension_inventory', { installationId: target.selected.id }); if (ticket === request.current) { setInventory(result); setLoadedId(target.selected.id); } }
    catch (e) { if (ticket === request.current) setError(message(e)); } finally { if (ticket === request.current) setBusy(false); }
  }, [target.selected?.id]);
  useEffect(() => { void refresh(); return () => { request.current++; }; }, [refresh]);
  const current = loadedId === target.selected?.id ? inventory : null;
  const runtime = current?.entries.find(e => e.id === 'builtin.runtime');
  const entries = (current?.entries || []).filter(e => e.id !== 'builtin.runtime');
  const visible = entries.filter(e => `${e.name} ${e.packageId} ${e.description}`.toLowerCase().includes(query.toLowerCase()));
  const uninstall = async (entry: ExtensionEntry) => {
    if (!target.selected || !confirm(`从 ${target.selected.install_path} 移除“${entry.name}”？只删除此位置由 Hub 记录且校验一致的文件；安装前备份仍保留。`)) return;
    setActionBusy(true); setError('');
    try { await api.uninstallExtension(entry.id, entry.category === 'xingshu-plugin', target.selected.id); await refresh(); }
    catch (e) { setError(message(e)); } finally { setActionBusy(false); }
  };
  const openInstaller = async () => {
    if (!target.selected) return;
    setActionBusy(true); setError('');
    try { await invoke('open_group_installer', { installationId: target.selected.id }); }
    catch (e) { setError(message(e)); } finally { setActionBusy(false); }
  };
  const packages = view === 'packages';
  const runtimeSupported = ['STEAM_CN', 'TAPTAP', 'BILIBILI', 'WEGAME'].includes(target.channel);
  return <div>
    <header className="extension-header"><div><h1>{packages ? '星枢包管理器' : '游戏模组'}</h1><p>{packages ? '安装、更新和还原电脑端或安卓端的星枢' : '统一管理外部 DLL、加速组件、立绘、模型、卡面与语音替换'}</p></div><div><button className="btn btn-secondary" onClick={() => void refresh()} disabled={busy || actionBusy}><RefreshCw size={14} />刷新</button>{!packages && <button className="btn btn-primary" disabled={actionBusy} onClick={() => setImportOpen(v => !v)}><Upload size={14} />{importOpen ? '收起模组包安装' : '安装模组包'}</button>}</div></header>
    {packages && <AndroidInstallPanel />}
    {packages && <h2 className="extension-section-title">电脑端星枢</h2>}
    <ExtensionTargetPicker target={target} disabled={actionBusy} />
    {target.selected && packages && <>
      <section className="panel extension-runtime"><Puzzle size={25} /><div><h2>安装 / 更新 / 还原</h2><p>{runtimeSupported ? '打开配套助手，自动带入所选渠道和目录。写入前需关闭游戏，安装与还原保留备份。' : '此渠道尚未提供经过验证的星枢安装文件。'}</p><p>{busy ? '正在检查当前文件…' : runtime ? `当前文件：${runtime.version || '已识别星枢'} · ${runtime.status}` : '当前文件未识别为本版星枢，请使用助手检查。'}</p></div>{runtimeSupported && <button className="btn btn-primary" disabled={actionBusy || !current?.installerAvailable} onClick={() => void openInstaller()}>打开安装助手</button>}</section>
      {runtimeSupported && current && !current.installerAvailable && <p className="extension-hint">请完整解压发布 ZIP，并从其中的 Hub 文件夹启动，以使用配套安装助手。</p>}
      <div className="extension-tags"><Link className="btn btn-secondary" to="/diagnostics?section=runtime">检查安装与连接状态</Link><Link className="btn btn-secondary" to="/extensions/mods">管理游戏模组</Link></div>
    </>}
    {!packages && target.selected && <>
      {importOpen && <ImportPackagePage key={target.selected.id} target={target} onBusy={setActionBusy} disabled={actionBusy} onInstalled={() => void refresh()} />}
      <ExternalModsPanel key={target.selected.id} installationId={target.selected.id} onBusy={setActionBusy} disabled={actionBusy} />
      <details className="mod-notes"><summary>其他本地文件与模组包安装记录（{entries.length}）</summary>
        <section className="panel extension-toolbar"><input aria-label="搜索其他本地模组" value={query} onChange={e => setQuery(e.target.value)} placeholder="搜索名称或描述…" /></section>
        <div className="extension-grid">{visible.map(entry => <article className="panel extension-card" key={entry.id}><h2>{entry.name}</h2><div className="extension-tags"><span>{categoryName(entry.category)}</span><span>{entry.source}</span></div><p>{entry.description}</p><code className="extension-path">{entry.path}</code><footer><span>{entry.status}</span>{entry.managed && <button className="btn btn-danger btn-sm" disabled={actionBusy} onClick={() => void uninstall(entry)}><Trash2 size={13} />卸载</button>}</footer></article>)}</div>
        {!visible.length && <p className="extension-path">尚未检测到其他本地模组。</p>}
      </details>
    </>}
    {!target.selected && <div className="panel extension-empty"><Package size={32} /><h2>先配置此渠道的游戏位置</h2><p>选择或添加实际游戏目录后，即可{packages ? '安装电脑端星枢' : '检查和管理该位置的模组'}。</p></div>}
    {(!!error || !!current?.warnings.length) && <div className="extension-hint" role="status">{error || current?.warnings.join(' ')}</div>}
  </div>;
}
