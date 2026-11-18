import { useEffect, useState } from 'react';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { Copy, Minus, Square, X, Sparkles } from 'lucide-react';

interface Props {
  title: string;
  english: boolean;
}

export function DesktopWindowBar({ title, english }: Props) {
  const [maximized, setMaximized] = useState(false);
  const [error, setError] = useState('');
  useEffect(() => {
    let disposed = false;
    let unlisten: undefined | (() => void);
    const window = getCurrentWindow();
    const sync = () => window.isMaximized().then(value => { if (!disposed) setMaximized(value); }).catch(() => {});
    void sync();
    window.onResized(sync).then(fn => { if (disposed) fn(); else unlisten = fn; }).catch(() => {});
    return () => { disposed = true; unlisten?.(); };
  }, []);
  const run = (action: 'minimize' | 'toggleMaximize' | 'close') => {
    setError('');
    getCurrentWindow()[action]().catch(() => setError(english ? 'Window action failed. Please retry.' : '窗口操作未完成，请重试。'));
  };
  return <header className="desktop-window-bar">
    <div className="desktop-drag-region" data-tauri-drag-region onMouseDown={event => {
      if (event.button !== 0) return;
      event.preventDefault();
      event.stopPropagation();
      if (event.detail === 2) run('toggleMaximize');
      else getCurrentWindow().startDragging().catch(() => setError(english ? 'Window move failed. Please retry.' : '窗口移动未完成，请重试。'));
    }}>
      <Sparkles size={13} aria-hidden="true" data-tauri-drag-region />
      <span data-tauri-drag-region>{title}</span>
    </div>
    {error && <span className="desktop-window-error" role="alert">{error}</span>}
    <div className="desktop-window-controls">
      <button type="button" aria-label={english ? 'Minimize' : '最小化'} onClick={() => run('minimize')}><Minus size={15} /></button>
      <button type="button" aria-label={maximized ? (english ? 'Restore window' : '还原窗口') : (english ? 'Maximize' : '最大化')} onClick={() => run('toggleMaximize')}>{maximized ? <Copy size={12} /> : <Square size={12} />}</button>
      <button type="button" className="desktop-close-button" aria-label={english ? 'Close' : '关闭'} onClick={() => run('close')}><X size={16} /></button>
    </div>
  </header>;
}
