import { useCallback, useEffect, useRef, useState } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { Archive, RefreshCw, ShieldCheck } from 'lucide-react';

interface ModFile { target: string; assets: string[]; size: number }
interface LibraryEntry { id: string; name: string; author: string; archive: string; files: ModFile[]; unmappedCount: number; conflicts: string[]; status: string; canInstall: boolean; canRestore: boolean }
interface Finding { name: string; path: string; sha256: string; level: string; description: string }
interface ModReport { gamePath: string; libraryPath: string | null; compatible: boolean; findings: Finding[]; library: LibraryEntry[]; notes: string[] }
const errorText = (e: unknown) => e && typeof e === 'object' && 'message' in e ? String(e.message) : String(e);

export function ExternalModsPanel({ installationId, onBusy, disabled = false }: { installationId: string; onBusy: (busy: boolean) => void; disabled?: boolean }) {
  const [report, setReport] = useState<ModReport | null>(null);
  const [busy, setBusy] = useState(false);
  const [feedback, setFeedback] = useState('');
  const [error, setError] = useState('');
  const [query, setQuery] = useState('');
  const request = useRef(0);
  const acting = useRef(false);
  const alive = useRef(true);
  const load = useCallback(async () => {
    const ticket = ++request.current;
    setBusy(true); setError('');
    try {
      const result = await invoke<ModReport>('get_external_mod_report', { installationId });
      if (ticket === request.current && alive.current) setReport(result);
    } catch (e) { if (ticket === request.current && alive.current) setError(errorText(e)); }
    finally { if (ticket === request.current && alive.current) setBusy(false); }
  }, [installationId]);
  useEffect(() => { alive.current = true; setReport(null); setFeedback(''); void load(); return () => { alive.current = false; request.current++; }; }, [load]);
  const action = async (item: LibraryEntry, operation: 'install' | 'restore') => {
    if (disabled || acting.current || !confirm(operation === 'install'
      ? `将为当前目录备份并替换 ${item.files.length} 个纹理文件。${item.unmappedCount ? '本条目只启用已适配的立绘/界面部分，模型和语音不安装。' : ''}请先正常退出游戏。继续启用“${item.name}”？`
      : `还原“${item.name}”的安装前文件？备份会保留，外部改动的文件不会强制覆盖。`)) return;
    acting.current = true; setBusy(true); onBusy(true); setError(''); setFeedback('');
    try {
      const message = await invoke<string>('external_mod_action', { installationId, itemId: item.id, action: operation });
      if (alive.current) { setFeedback(message); await load(); }
    } catch (e) { if (alive.current) setError(errorText(e)); }
    finally { acting.current = false; onBusy(false); if (alive.current) setBusy(false); }
  };
  const archive = async () => {
    if (disabled || acting.current) return;
    acting.current = true; setBusy(true); onBusy(true); setError(''); setFeedback('');
    try { const path = await invoke<string>('archive_external_mod_report', { installationId }); if (alive.current) setFeedback(`报告及待处理组件副本已保存：${path}。原文件保留。`); }
    catch (e) { if (alive.current) setError(errorText(e)); }
    finally { acting.current = false; onBusy(false); if (alive.current) setBusy(false); }
  };
  const visible = report?.library.filter(item => `${item.name} ${item.author} ${item.archive}`.toLowerCase().includes(query.toLowerCase())) || [];
  return <section className="external-mods">
    <div className="extension-header"><div><h2>外部模组与加速组件</h2><p>检查当前游戏目录，区分兼容版本、已知冲突和待核验文件</p></div><div>
      <button className="btn btn-secondary" disabled={busy || disabled} onClick={() => { setFeedback(''); void load(); }}><RefreshCw size={14} />重新检测</button>
      <button className="btn btn-secondary" disabled={busy || disabled || !report} onClick={() => void archive()}><Archive size={14} />归档检查结果</button>
    </div></div>
    {error && <p className="extension-error" role="alert">{error}</p>}
    {feedback && <p className="extension-hint" role="status">{feedback}</p>}
    {!report && <p className="extension-path">{busy ? '正在检查文件指纹和本地模组…' : '检测尚未完成'}</p>}
    {report && <>
      <div className="mod-findings">{report.findings.length ? report.findings.map(f => <article className={`panel mod-finding ${f.level}`} key={f.path}>
        <div><strong>{f.name}</strong><span>{({ conflict: '已知冲突', compatible: '已识别兼容文件', review: '待核验' } as Record<string, string>)[f.level]}</span></div>
        <p>{f.description}</p><code className="extension-path">{f.path}</code><details><summary>文件指纹</summary><code className="extension-path">{f.sha256}</code></details>
      </article>) : <p className="extension-hint"><ShieldCheck size={15} /> 未发现额外 DLL 或加载器文件；不代表所有资源都未经手动修改。</p>}</div>
      <div className="extension-header"><div><h2>本地模组库</h2><p>已检查下载的 11 个原包；下方按替换部件和互斥方案分别管理</p></div><input className="form-input mod-search" aria-label="搜索模组库" placeholder="搜索模组或作者" value={query} onChange={e => setQuery(e.target.value)} /></div>
      <p className="extension-path">{report.libraryPath ? `配套文件：${report.libraryPath}` : '未找到配套 ModLibrary 目录；可查看适配信息，启用需保留完整本地模组库。'}</p>
      <div className="extension-grid">{visible.map(item => <article className="panel extension-card" key={item.id}>
        <h2>{item.name}</h2><div className="extension-tags"><span>{item.files.length} 个纹理</span>{item.unmappedCount > 0 && <span>{item.unmappedCount} 个文件待适配</span>}</div>
        <p>作者：{item.author}<br />来源：{item.archive}</p>
        {item.conflicts.length > 0 && <p className="mod-conflict">不可同时启用：{item.conflicts.join('、')}</p>}
        <details><summary>适配位置与资源</summary><div className="mod-targets">{item.files.length ? item.files.map(f => <p key={f.target}><strong>{f.assets.join('、')}</strong><code className="extension-path">{f.target}</code></p>) : <p>原包涉及模型、动画或语音依赖，尚未提供可安全替换的当前版本文件。</p>}</div></details>
        <footer><span>{item.status}</span><div>{item.canRestore && <button className="btn btn-secondary btn-sm" disabled={busy || disabled} onClick={() => void action(item, 'restore')}>还原安装前文件</button>}{item.canInstall && <button className="btn btn-primary btn-sm" disabled={busy || disabled} onClick={() => void action(item, 'install')}>启用纹理替换</button>}</div></footer>
      </article>)}</div>
      {!visible.length && <p className="extension-path">没有符合搜索条件的模组。</p>}
      <details className="mod-notes"><summary>适配与备份说明</summary>{report.notes.map(note => <p key={note}>{note}</p>)}<p>归档只复制待处理组件，不停用、不删除兼容 DLL；原包文件仍保留在下载目录。</p></details>
    </>}
  </section>;
}
