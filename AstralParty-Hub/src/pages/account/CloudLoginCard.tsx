import {useEffect, useState} from 'react';
import {invoke} from '@tauri-apps/api/core';
import {openUrl} from '@tauri-apps/plugin-opener';
import {LogIn, LogOut, RefreshCw, X, ShieldCheck} from 'lucide-react';
import {Link} from 'react-router-dom';

type Account = {uid: string; nick: string};
type Grant = {loginUrl: string; qrPng: string; expiresIn: number};
type LoginState = {status: string; account?: Account};
const readable = (error: unknown) => {
  const value = error as {message?: unknown};
  return typeof value?.message === 'string' ? value.message : '登录连接未完成，请稍后重试。';
};

export function CloudLoginCard({isZh}: {isZh: boolean}) {
  const [account, setAccount] = useState<Account | null>(null);
  const [grant, setGrant] = useState<Grant | null>(null);
  const [stage, setStage] = useState('');
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const [visible, setVisible] = useState(false);
  const [loaded, setLoaded] = useState(false);

  const refresh = async () => {
    setBusy(true); setNotice('');
    try {
      const result = await invoke<LoginState>('get_cloud_account');
      setAccount(result.account ?? null); setLoaded(true);
    } catch (error) { setNotice(readable(error)); }
    finally { setBusy(false); }
  };
  useEffect(() => { void refresh(); }, []);
  useEffect(() => {
    if (!grant) return;
    let cancelled = false;
    let failures = 0;
    let timer: ReturnType<typeof setTimeout>;
    const poll = async () => {
      try {
        const result = await invoke<LoginState>('poll_cloud_login');
        if (cancelled) return;
        failures = 0; setStage(result.status);
        if (result.status === 'succeeded') {
          setAccount(result.account ?? null); setGrant(null); setVisible(false); setLoaded(true);
          setNotice(isZh ? '登录成功。' : 'Signed in.'); return;
        }
        if (result.status === 'expired' || result.status === 'superseded') {
          setGrant(null); setNotice(isZh ? '本次登录已过期，请重新发起。' : 'Login expired. Please try again.'); return;
        }
      } catch (error) {
        if (cancelled) return;
        setNotice(readable(error));
        if (++failures >= 5) { setGrant(null); return; }
      }
      timer = setTimeout(poll, 2000);
    };
    timer = setTimeout(poll, 1500);
    return () => { cancelled = true; clearTimeout(timer); };
  }, [grant, isZh]);

  const start = async () => {
    setBusy(true); setNotice('');
    try {
      const result = await invoke<Grant>('start_cloud_login');
      setGrant(result); setStage('pending'); setVisible(true);
    } catch (error) { setNotice(readable(error)); }
    finally { setBusy(false); }
  };
  const logout = async () => {
    setBusy(true); setNotice('');
    try { await invoke('logout_cloud_account'); setAccount(null); setGrant(null); setLoaded(true); }
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
        {isZh ? '登录只确认账号身份。登录后可到玩家资料或对战记录主动同步；相同 UID 的本机资料会与云端衔接。登录前请退出同账号游戏，下方角色绑定仍由你管理。' :
          'Login confirms identity only. Close the game for this account first. Local account roles below remain manually assigned.'}
      </p>
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
