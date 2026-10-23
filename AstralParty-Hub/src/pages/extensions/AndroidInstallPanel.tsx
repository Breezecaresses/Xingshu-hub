import { useCallback, useEffect, useRef, useState } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { Download, RefreshCw, Smartphone, Wifi } from 'lucide-react';
import './android-install.css';

interface Device { serial: string; state: string; model: string }
interface Status {
  available: boolean; adbPath: string | null; runtimeVersion: string | null;
  phoneHelperAvailable: boolean; devices: Device[]; message: string;
}
const errorText = (error: unknown) => error && typeof error === 'object' && 'message' in error ? String(error.message) : String(error);
const stateName = (state: string) => ({ device: '已连接', unauthorized: '等待手机授权', offline: '已离线', 'no permissions': '无设备访问权限' }[state] || state);

export function AndroidInstallPanel() {
  const [expanded, setExpanded] = useState(false);
  const [status, setStatus] = useState<Status | null>(null);
  const [serial, setSerial] = useState('');
  const [busy, setBusy] = useState('');
  const [result, setResult] = useState('');
  const [error, setError] = useState('');
  const [pairAddress, setPairAddress] = useState('');
  const [connectAddress, setConnectAddress] = useState('');
  const [pairingCode, setPairingCode] = useState('');
  const lock = useRef(false);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const refreshStatus = useCallback(async (preferred?: string) => {
    const value = await invoke<Status>('get_android_installer_status');
    if (!mounted.current) return;
    setStatus(value);
    setSerial(current => {
      const wanted = preferred || current;
      if (value.devices.some(d => d.serial === wanted && d.state === 'device')) return wanted;
      return value.devices.length === 1 && value.devices[0].state === 'device' ? value.devices[0].serial : '';
    });
  }, []);
  const refresh = useCallback(async () => {
    if (lock.current) return;
    lock.current = true; setBusy('检测手机'); setError(''); setResult('');
    try { await refreshStatus(); } catch (e) { if (mounted.current) setError(errorText(e)); }
    finally { lock.current = false; if (mounted.current) setBusy(''); }
  }, [refreshStatus]);
  useEffect(() => { if (expanded && !status) void refresh(); }, [expanded, status, refresh]);
  const act = async (action: 'pair' | 'connect' | 'check' | 'install' | 'restore' | 'install-helper', label: string) => {
    if (lock.current) return;
    lock.current = true; setBusy(label); setResult(''); setError('');
    try {
      const text = await invoke<string>('android_installer_action', { request: {
        action, serial, address: action === 'pair' ? pairAddress.trim() : connectAddress.trim(),
        pairingCode: action === 'pair' ? pairingCode : null,
      } });
      if (!mounted.current) return;
      setResult(text);
      if (action === 'pair') setPairingCode('');
      await refreshStatus(action === 'connect' ? connectAddress.trim() : undefined);
    } catch (e) { if (mounted.current) setError(errorText(e)); }
    finally { lock.current = false; if (mounted.current) setBusy(''); }
  };
  const ready = status?.available && status.devices.some(d => d.serial === serial && d.state === 'device');
  return <section className="panel android-install" aria-label="安卓手机安装">
    <div className="android-install-heading"><Smartphone size={25} /><div><h2>安卓手机安装 / 更新</h2><p>连接手机，在电脑上安装星枢；也可把手机安装助手装到手机，方便之后独立更新。</p></div>
      <button className={`btn ${expanded ? 'btn-secondary' : 'btn-primary'}`} aria-expanded={expanded} aria-controls="android-install-body" disabled={!!busy} onClick={() => setExpanded(!expanded)}>{expanded ? '收起' : '连接手机'}</button>
    </div>
    {expanded && <div id="android-install-body" className="android-install-body">
      <ol className="android-install-steps"><li>手机先在官方游戏完成资源更新，进入大厅后正常退出。</li><li>用数据线连接电脑，开启 USB 调试并在手机允许授权；或使用下方无线连接。</li><li>选择手机后安装。完成并显示校验通过，再从官方入口启动游戏。</li></ol>
      <div className="android-install-device"><label htmlFor="android-device">目标手机</label><select id="android-device" value={serial} disabled={!!busy || !status?.available} onChange={e => { setSerial(e.target.value); setResult(''); setError(''); }}>
        <option value="">{status?.devices.length ? '请选择已授权的手机' : '尚未检测到手机'}</option>
        {status?.devices.map(device => <option key={device.serial} value={device.serial} disabled={device.state !== 'device'}>{device.model || device.serial} · {stateName(device.state)}{device.model ? ` · ${device.serial}` : ''}</option>)}
      </select><button className="btn btn-secondary" disabled={!!busy} onClick={() => void refresh()}><RefreshCw size={14} />检测手机</button></div>
      {status && <p className={status.available ? 'android-install-note' : 'extension-hint'} role="status">{status.message}</p>}
      <div className="android-install-actions">
        <button className="btn btn-primary" disabled={!ready || !!busy} onClick={() => void act('install', '安装 / 更新星枢')}><Download size={15} />安装 / 更新星枢</button>
        <button className="btn btn-secondary" disabled={!ready || !!busy} onClick={() => void act('check', '检查游戏与目录')}>检查游戏与目录</button>
        <button className="btn btn-secondary" disabled={!ready || !!busy} onClick={() => void act('restore', '还原官方原版')}>还原官方原版</button>
        <button className="btn btn-secondary" disabled={!ready || !!busy || !status?.phoneHelperAvailable} onClick={() => void act('install-helper', '安装手机助手 APK')}>安装手机助手 APK</button>
      </div>
      <p className="android-install-note">安装星枢会保留原版备份；安装手机助手 APK 只安装工具，之后在手机上打开使用。多台设备时请明确选择目标。</p>
      <details className="android-wireless"><summary><Wifi size={15} />无线连接（同一 Wi-Fi）</summary><p>在手机打开“开发者选项 → 无线调试”。首次连接先配对；已配对的手机可直接连接。配对弹窗与主页面的端口通常不同。</p>
        <div className="android-wireless-row"><label>配对地址<input aria-label="配对 IP 地址和端口" type="text" placeholder="配对码弹窗的 IP:端口" value={pairAddress} disabled={!!busy} onChange={e => setPairAddress(e.target.value)} /></label><label className="android-pair-code">六位配对码<input aria-label="六位配对码" type="text" inputMode="numeric" autoComplete="off" maxLength={6} value={pairingCode} disabled={!!busy} onChange={e => setPairingCode(e.target.value.replace(/\D/g, '').slice(0, 6))} /></label><button className="btn btn-secondary" disabled={!!busy || !status?.available || !pairAddress.trim() || pairingCode.length !== 6} onClick={() => void act('pair', '配对手机')}>① 配对手机</button></div>
        <div className="android-wireless-row"><label>连接地址<input aria-label="连接 IP 地址和端口" type="text" placeholder="无线调试主页面的 IP:端口" value={connectAddress} disabled={!!busy} onChange={e => setConnectAddress(e.target.value)} /></label><button className="btn btn-secondary" disabled={!!busy || !status?.available || !connectAddress.trim()} onClick={() => void act('connect', '连接手机')}>② 连接手机</button></div>
      </details>
      {!!busy && <p className="android-install-busy" role="status" aria-live="polite">正在{busy}… 请保持连接，等待操作完成。</p>}
      {error && <div className="extension-hint" role="alert"><strong>未完成</strong><pre>{error}</pre><p>若提示未授权，请在手机上允许调试；无线断开时，请重新读取当前连接端口。安装 APK 被手机拦截时，请查看手机上的安装提示。</p></div>}
      {result && <pre className="android-install-result" role="status" aria-live="polite">{result}</pre>}
      {status?.runtimeVersion && <p className="android-install-note">配套安卓版本：{status.runtimeVersion}</p>}
    </div>}
  </section>;
}
