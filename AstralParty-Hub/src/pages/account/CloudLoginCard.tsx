import {useEffect, useState} from 'react';
import {invoke} from '@tauri-apps/api/core';
import {openUrl} from '@tauri-apps/plugin-opener';
import {LogIn, LogOut, RefreshCw, X, ShieldCheck, Link2, Sparkles} from 'lucide-react';
import {Link} from 'react-router-dom';

type Account = {uid: string; nick: string};
type Grant = {loginUrl: string; qrPng: string; expiresIn: number};
type LoginState = {status: string; account?: Account};
type DetectedChannel = {platform: string; appId: string; label: string; updatedAt: number};
const readable = (error: unknown) => {
  const value = error as {message?: unknown};
  return typeof value?.message === 'string' ? value.message : '登录连接未完成，请稍后重试。';
};

export function CloudLoginCard({isZh}: {isZh: boolean}) {
  const [account, setAccount] = useState<Account | null>(null);
  const [grant, setGrant] = useState<Grant | null>(null);
  const [polling, setPolling] = useState(false);
  const [stage, setStage] = useState('');
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const [visible, setVisible] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [channels, setChannels] = useState<DetectedChannel[]>([]);
  const [qqOpen, setQqOpen] = useState(false);
  const [qqLink, setQqLink] = useState('');
  const [qqChannelIdx, setQqChannelIdx] = useState(0);

  const refresh = async () => {
    setBusy(true); setNotice('');
    try {
      const [result, ch] = await Promise.all([
        invoke<LoginState>('get_cloud_account'),
        invoke<{channels: DetectedChannel[]}>('detect_local_channel_tokens').catch(() => ({channels: []})),
      ]);
      setAccount(result.account ?? null);
      setChannels(ch.channels ?? []);
      setLoaded(true);
    } catch (error) { setNotice(readable(error)); }
    finally { setBusy(false); }
  };
  useEffect(() => { void refresh(); }, []);
  useEffect(() => {
    if (!polling) return;
    let cancelled = false;
    let failures = 0;
    let timer: ReturnType<typeof setTimeout>;
    const poll = async () => {
      try {
        const result = await invoke<LoginState>('poll_cloud_login');
        if (cancelled) return;
        failures = 0; setStage(result.status);
        if (result.status === 'succeeded') {
          setAccount(result.account ?? null); setGrant(null); setPolling(false); setVisible(false); setLoaded(true);
          setNotice(isZh ? '登录成功。' : 'Signed in.'); return;
        }
        if (result.status === 'failed') {
          setGrant(null); setPolling(false); setNotice(isZh ? '渠道服登录未成功，请确认游戏内已登录进入大厅后再试。' : 'Channel sign in failed.'); return;
        }
        if (result.status === 'expired' || result.status === 'superseded') {
          setGrant(null); setPolling(false); setNotice(isZh ? '本次登录已过期，请重新发起。' : 'Login expired. Please try again.'); return;
        }
      } catch (error) {
        if (cancelled) return;
        setNotice(readable(error));
        if (++failures >= 5) { setGrant(null); setPolling(false); return; }
      }
      timer = setTimeout(poll, 2000);
    };
    timer = setTimeout(poll, 1200);
    return () => { cancelled = true; clearTimeout(timer); };
  }, [polling, isZh]);

  const start = async () => {
    setBusy(true); setNotice('');
    try {
      const result = await invoke<Grant>('start_cloud_login');
      setGrant(result); setPolling(true); setStage('pending'); setVisible(true);
    } catch (error) { setNotice(readable(error)); }
    finally { setBusy(false); }
  };
  const startChannel = async (ch: DetectedChannel) => {
    setBusy(true); setNotice(isZh ? `正在使用本机 ${ch.label} 票据登录…` : `Signing in via ${ch.label}…`);
    try {
      await invoke('start_channel_cloud_login', {platform: ch.platform, appId: ch.appId});
      setGrant(null); setPolling(true); setStage('opened');
    } catch (error) { setNotice(readable(error)); }
    finally { setBusy(false); }
  };
  const pairQq = async () => {
    const ch = channels[qqChannelIdx];
    if (!ch || !qqLink.trim()) {
      setNotice(isZh ? '请先在本机启动渠道服进入大厅，并粘贴 QQ Bot「趴登录」发出的链接。' : 'Launch a channel client and paste the QQ Bot link first.');
      return;
    }
    setBusy(true); setNotice(isZh ? `正在用本机 ${ch.label} 授权给 QQ Bot…` : `Authorizing QQ Bot via ${ch.label}…`);
    try {
      const res = await invoke<{status: string; account?: Account}>('complete_qq_channel_login', {
        loginUrl: qqLink.trim(), platform: ch.platform, appId: ch.appId,
      });
      setQqLink(''); setQqOpen(false);
      setNotice(isZh ? `已授权给 QQ Bot：${res.account?.nick ?? ''}（UID ${res.account?.uid ?? ''}）` : `Authorized QQ Bot: ${res.account?.nick ?? ''}`);
    } catch (error) { setNotice(readable(error)); }
    finally { setBusy(false); }
  };
  const logout = async () => {
    setBusy(true); setNotice('');
    try { await invoke('logout_cloud_account'); setAccount(null); setGrant(null); setPolling(false); setLoaded(true); }
    catch (error) { setNotice(readable(error)); }
    finally { setBusy(false); }
  };
  return <>
    <section className="card" style={{padding: 22, marginBottom: 22}} aria-label={isZh ? '星枢账号登录' : 'XingShu sign in'}>
      <div style={{display: 'flex', gap: 16, alignItems: 'center', flexWrap: 'wrap'}}>
        <ShieldCheck size={28} style={{color: 'var(--primary, #818cf8)', flexShrink: 0}}/>
        <div style={{flex: 1, minWidth: 220}}>
          <h3 style={{margin: '0 0 7px'}}>{isZh ? '星枢账号登录' : 'XingShu sign in'}</h3>
          <div style={{fontSize: 14, color: 'var(--text-secondary)'}}>
            {account ? <><strong style={{color: 'var(--text-primary)'}}>{account.nick}</strong> · UID {account.uid}</> :
              isZh ? '用游戏账号绑定的手机号登录，QQ Bot 和 Hub 均可使用。' : 'Sign in with the phone number linked to your game account.'}
          </div>
        </div>
        {grant && <button className="btn btn-secondary" onClick={() => setVisible(true)}>{isZh ? '查看登录进度' : 'View login'}</button>}
        {account ? <button className="btn btn-secondary" disabled={busy} onClick={logout}><LogOut size={15}/>{isZh ? '退出登录' : 'Sign out'}</button> :
          <button className="btn btn-primary" disabled={busy} onClick={start}><LogIn size={16}/>{busy ? (isZh ? '连接中…' : 'Connecting…') : (isZh ? '登录账号' : 'Sign in')}</button>}
        {account&&<Link className="btn btn-secondary" to="/player/profile" onClick={()=>localStorage.setItem('library.account',`PHONE_CN:${account.uid}`)}>{isZh ? '查看游戏资料' : 'Game profile'}</Link>}
        <button className="btn btn-secondary" disabled={busy} onClick={refresh} aria-label={isZh ? '刷新登录状态' : 'Refresh login'}><RefreshCw size={15}/></button>
      </div>
      <p style={{fontSize: 12, lineHeight: 1.7, color: 'var(--text-secondary)', margin: '14px 0 0'}}>
        {isZh ? '登录只确认账号身份；若该账号正在游戏中，校验票据会顶下该游戏客户端一次，登录后重开游戏即可。日常手牌与战绩同步不会再顶号，多开时请用与主对局相同的账号登录，下方角色绑定仍由你管理。' :
          'Login confirms identity (an active game on the same account will be signed out once during verification; reopen afterwards). Routine hand sync does not kick active sessions.'}
      </p>
      {channels.length > 0 && (
        <div style={{marginTop: 14, paddingTop: 14, borderTop: '1px solid var(--border-subtle)', display: 'flex', flexDirection: 'column', gap: 10}}>
          <div style={{display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap'}}>
            <span style={{fontSize: 12, color: 'var(--text-secondary)', display: 'inline-flex', alignItems: 'center', gap: 5}}>
              <Sparkles size={13} style={{color: 'var(--primary, #818cf8)'}}/>
              {isZh ? '检测到本机渠道服日志票据：' : 'Local channel tickets detected:'}
            </span>
            {!account && channels.map(ch => (
              <button key={`${ch.platform}:${ch.appId}`} className="btn btn-secondary" disabled={busy || polling}
                onClick={() => startChannel(ch)} style={{padding: '5px 11px', fontSize: 12}}>
                {isZh ? `用 ${ch.label} 一键登录` : `Sign in via ${ch.label}`}
              </button>
            ))}
            <button className="btn btn-secondary" disabled={busy} onClick={() => setQqOpen(v => !v)}
              style={{padding: '5px 11px', fontSize: 12}}>
              <Link2 size={13}/>{isZh ? '一键授权给 QQ Bot（粘贴趴登录链接）' : 'Pair QQ Bot link'}
            </button>
          </div>
          {qqOpen && (
            <div style={{display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap'}}>
              <select value={qqChannelIdx} onChange={e => setQqChannelIdx(Number(e.target.value))}
                style={{padding: '7px 10px', borderRadius: 8, border: '1px solid var(--border-subtle)', background: 'var(--bg-subtle)', color: 'var(--text-primary)', fontSize: 12}}>
                {channels.map((ch, idx) => <option key={`${ch.platform}:${ch.appId}`} value={idx}>{ch.label}</option>)}
              </select>
              <input value={qqLink} onChange={e => setQqLink(e.target.value)}
                placeholder={isZh ? '粘贴 QQ Bot「趴登录」发出的 https://124.221.176.134/login#... 链接' : 'Paste https://124.221.176.134/login#...'}
                style={{flex: 1, minWidth: 240, padding: '7px 10px', borderRadius: 8, border: '1px solid var(--border-subtle)', background: 'var(--bg-subtle)', color: 'var(--text-primary)', fontSize: 12}}/>
              <button className="btn btn-primary" disabled={busy || !qqLink.trim()} onClick={pairQq} style={{padding: '7px 14px', fontSize: 12}}>
                {isZh ? '授权 QQ Bot' : 'Authorize QQ'}
              </button>
            </div>
          )}
        </div>
      )}
      {notice && <p role="status" style={{margin: '10px 0 0', fontSize: 13, color: account ? '#34d399' : 'var(--text-secondary)'}}>{notice}</p>}
      {!loaded && busy && <span role="status" style={{fontSize: 12}}>{isZh ? '正在读取登录状态…' : 'Loading account…'}</span>}
    </section>
    {visible && <div role="dialog" aria-modal="true" aria-label={isZh ? '手机号登录' : 'Phone sign in'} style={{position: 'fixed', inset: 0, background: 'rgba(0,0,0,.65)', zIndex: 1200, display: 'grid', placeItems: 'center', padding: 24}}>
      <div className="card" style={{width: 'min(440px, 100%)', padding: 26, position: 'relative', textAlign: 'center'}}>
        <button className="btn btn-secondary" onClick={() => setVisible(false)} aria-label={isZh ? '关闭' : 'Close'} style={{position: 'absolute', top: 12, right: 12, padding: 7}}><X size={17}/></button>
        <h2 style={{fontSize: 21, margin: '10px 0 12px'}}>{isZh ? '扫码登录星枢' : 'Sign in to XingShu'}</h2>
        {grant && stage === 'pending' ? <>
          <img src={`data:image/png;base64,${grant.qrPng}`} width={250} height={250} alt={isZh ? '星枢手机号授权二维码' : 'Phone authorization QR code'} style={{maxWidth: '100%', height: 'auto', borderRadius: 12, background: 'white', padding: 8}}/>
          <p style={{lineHeight: 1.7, color: 'var(--text-secondary)', fontSize: 13}}>{isZh ? '使用手机扫码，在授权页填写手机号和短信验证码。二维码有效期 2 分钟。' : 'Scan with your phone and sign in by SMS. QR expires in 2 minutes.'}</p>
          <button className="btn btn-primary" onClick={() => openUrl(grant.loginUrl).catch(() => setNotice(isZh ? '无法打开浏览器，请使用手机扫码。' : 'Unable to open browser. Please scan the QR.'))}>{isZh ? '在浏览器打开登录页' : 'Open login page'}</button>
        </> : grant ? <p style={{padding: '50px 0', lineHeight: 1.8}}>{isZh ? '授权页已打开，请完成手机号登录。成功后此处将自动更新。' : 'Authorization page opened. Finish sign in to continue.'}</p> :
          <p style={{lineHeight: 1.8}}>{notice}</p>}
        {!grant && <button className="btn btn-primary" disabled={busy} onClick={start}>{isZh ? '重新生成二维码' : 'New QR code'}</button>}
      </div>
    </div>}
  </>;
}
