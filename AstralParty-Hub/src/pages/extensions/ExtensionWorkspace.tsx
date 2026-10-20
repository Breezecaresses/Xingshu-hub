import { useCallback, useEffect, useRef, useState } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { Link } from 'react-router-dom';
import { FolderOpen, Package, Puzzle, RefreshCw, Sparkles, Trash2, Upload } from 'lucide-react';
import { api } from '../../shared/api/tauri';
import type { GameInstallation } from '../../shared/types';
import './extensions.css';

export const extensionChannels = [['STEAM_CN', 'Steam 国服'], ['STEAM_GLOBAL', 'Steam 国际服'], ['TAPTAP', 'TapTap Windows'], ['BILIBILI', '哔哩哔哩'], ['WEGAME', 'WeGame']] as const;
export interface ExtensionEntry { id: string; packageId: string; name: string; category: string; version: string | null; path: string; source: string; status: string; description: string; managed: boolean }
export interface Inventory { entries: ExtensionEntry[]; pluginDirectory: string; modDirectory: string; resourceDirectory: string; warnings: string[]; installerAvailable: boolean }
const message = (e: unknown) => e && typeof e === 'object' && 'message' in e ? String(e.message) : String(e);
export const categoryName = (category: string) => ({ 'apx-plugin': '运行时插件', mod: '游戏模组', 'resource-pack': '资源包' }[category] || category);

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
export function ExtensionTargetPicker({ target, disabled = false }: { target: ReturnType<typeof useExtensionTarget>; disabled?: boolean }) {
  return <section className="panel extension-target" aria-label="扩展安装位置">
    <div className="extension-target-title"><FolderOpen size={19} /><strong>渠道与安装位置</strong><span>扩展只安装到下方选定目录</span></div>
    <div className="extension-channels">{extensionChannels.map(([id, name]) => <button key={id} className={`btn ${target.channel === id ? 'btn-primary' : 'btn-secondary'}`} onClick={() => target.setChannel(id)} disabled={disabled}>{name}<small>{target.targets.filter(g => g.platform === id).length || '未配置'}</small></button>)}</div>
    <div className="extension-location"><select aria-label="选择游戏安装位置" value={target.selected?.id || ''} disabled={disabled || !target.options.length} onChange={e => target.select(e.target.value)}>{!target.options.length && <option value="">此渠道尚未配置安装位置</option>}{target.options.map(g => <option key={g.id} value={g.id}>{g.install_path}</option>)}</select><button className="btn btn-secondary" disabled={target.busy || disabled} onClick={() => void target.refresh()}><RefreshCw size={14} />重新检测</button></div>
    <form className="extension-location" onSubmit={e => { e.preventDefault(); void target.save(); }}><input aria-label="添加游戏安装路径" value={target.path} disabled={disabled} onChange={e => target.setPath(e.target.value)} placeholder="添加另一安装位置：粘贴实际游戏 exe 或所在文件夹的完整路径" /><button className="btn btn-secondary" disabled={target.busy || disabled || !target.path.trim()}>保存此位置</button></form>
    {target.selected && <p className="extension-path">实际游戏程序：<code>{target.selected.executable_path}</code></p>}
    {target.error && <p className="extension-error" role="alert">{target.error}</p>}
  </section>;
}

export function ExtensionManager({ view }: { view: 'packages' | 'plugins' | 'mods' }) {
  const target = useExtensionTarget();
  const [inventory, setInventory] = useState<Inventory | null>(null);
  const [loadedId, setLoadedId] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [actionBusy, setActionBusy] = useState(false);
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState('all');
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
  const title = view === 'packages' ? '星枢包管理器' : view === 'plugins' ? '运行时插件' : '游戏模组';
  const entries = loadedId === target.selected?.id ? inventory?.entries || [] : [];
  const visible = entries.filter(e => (view === 'packages' || (view === 'plugins' ? e.category === 'apx-plugin' : e.category !== 'apx-plugin')) && (filter === 'all' || e.category === filter) && `${e.name} ${e.packageId} ${e.description}`.toLowerCase().includes(query.toLowerCase()));
  const uninstall = async (entry: ExtensionEntry) => {
    if (!target.selected || !confirm(`从 ${target.selected.install_path} 移除“${entry.name}”？只删除此位置由 Hub 记录且校验一致的文件；安装前备份仍保留。`)) return;
    setActionBusy(true); setError('');
    try { await api.uninstallExtension(entry.id, entry.category === 'apx-plugin', target.selected.id); await refresh(); }
    catch (e) { setError(message(e)); } finally { setActionBusy(false); }
  };
  const openInstaller = async () => {
    if (!target.selected) return;
    setActionBusy(true); setError('');
    try { await invoke('open_group_installer', { installationId: target.selected.id }); }
    catch (e) { setError(message(e)); } finally { setActionBusy(false); }
  };
  const runtimeSupported = ['STEAM_CN', 'TAPTAP'].includes(target.channel);
  return <div>
    <header className="extension-header"><div><h1>{title}</h1><p>{view === 'packages' ? '按渠道和安装目录管理本地扩展、运行时与资源包' : view === 'plugins' ? '查看实际运行时文件及独立插件，管理当前安装位置' : '查看当前安装位置的模型、卡面、贴图与资源替换'}</p></div><div><button className="btn btn-secondary" onClick={() => void refresh()} disabled={busy || actionBusy}><RefreshCw size={14} />刷新</button><Link to="/extensions/import" className="btn btn-primary"><Upload size={14} />导入安装包</Link></div></header>
    <ExtensionTargetPicker target={target} disabled={actionBusy} />
    {target.selected && <>
      <div className="extension-summary"><div><strong>{entries.filter(e => e.category === 'apx-plugin').length}</strong><span>检测到的运行时与插件</span></div><div><strong>{entries.filter(e => e.category !== 'apx-plugin').length}</strong><span>本地模组与资源</span></div><div><strong>{entries.filter(e => e.managed).length}</strong><span>可由 Hub 管理的扩展</span></div></div>
      {view !== 'mods' && <section className="panel extension-runtime"><Puzzle size={25} /><div><h2>星枢运行时安装 / 更新</h2><p>{runtimeSupported ? '本版为此渠道提供已校验的运行时文件。打开安装助手后，所选渠道与目录会自动带入，安装和还原均保留备份。' : '此渠道已纳入目录管理；本版尚未提供经过验证的运行时安装文件。可导入明确声明支持此渠道的独立扩展。'}</p></div>{runtimeSupported && <button className="btn btn-primary" disabled={actionBusy || !inventory?.installerAvailable} onClick={() => void openInstaller()}>打开安装助手</button>}</section>}
      {view !== 'mods' && runtimeSupported && inventory && !inventory.installerAvailable && <p className="extension-hint">请完整解压发布 ZIP，并从其中的 Hub 文件夹启动，以使用配套安装助手。</p>}
      <div className="extension-directories"><span>插件 <code>{inventory?.pluginDirectory || '读取中…'}</code></span><span>模组 <code>{inventory?.modDirectory || '读取中…'}</code></span><span>资源 <code>{inventory?.resourceDirectory || '读取中…'}</code></span></div>
    </>}
    {(error || inventory?.warnings.length) && <div className="extension-hint" role="status">{error || inventory?.warnings.join(' ')}</div>}
    <section className="panel extension-toolbar"><div>{['all', 'apx-plugin', 'mod', 'resource-pack'].filter(c => view === 'packages' || c === 'all' || (view === 'plugins' ? c === 'apx-plugin' : c !== 'apx-plugin')).map(c => <button key={c} className={`btn btn-sm ${filter === c ? 'btn-primary' : 'btn-secondary'}`} onClick={() => setFilter(c)}>{c === 'all' ? '全部' : categoryName(c)}</button>)}</div><input aria-label="搜索本地扩展" value={query} onChange={e => setQuery(e.target.value)} placeholder="搜索名称、包标识或描述…" /></section>
    <div className="extension-grid">{visible.map(entry => <article className="panel extension-card" key={entry.id}><div className="extension-card-heading">{entry.category === 'apx-plugin' ? <Puzzle size={22} /> : <Sparkles size={22} />}<h2>{entry.name}</h2>{entry.version && <span className="sidebar-tag">{entry.version}</span>}</div><div className="extension-tags"><span>{categoryName(entry.category)}</span><span>{entry.source}</span></div>{entry.packageId && <code>{entry.packageId}</code>}<p>{entry.description}</p><code className="extension-path">{entry.path}</code><footer><span className={entry.status === '文件缺失' ? 'extension-error' : ''}>{entry.status}</span>{entry.managed && <button className="btn btn-danger btn-sm" disabled={actionBusy} onClick={() => void uninstall(entry)}><Trash2 size={13} />卸载</button>}</footer></article>)}</div>
    {!visible.length && <div className="panel extension-empty"><Package size={32} /><h2>{busy || target.busy ? '正在读取本地安装状态…' : !target.selected ? '先配置此渠道的游戏位置' : query || filter !== 'all' ? '没有符合筛选条件的扩展' : '此位置尚未检测到独立扩展'}</h2><p>{target.selected ? '可导入声明支持所选渠道的 .xspkg 或 .zip 包。外部安装的资源会显示检测结果，不会被当作 Hub 已管理的包。' : '各渠道和不同客户端目录分别保存，配置后即可查看插件与模组。'}</p><Link to="/extensions/import" className="btn btn-secondary">导入本地扩展包</Link></div>}
  </div>;
}
