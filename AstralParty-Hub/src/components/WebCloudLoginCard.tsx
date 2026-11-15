import React, { useEffect, useState, useSyncExternalStore } from 'react';
import { ExternalLink, LogOut, QrCode, RefreshCw, ShieldCheck, X } from 'lucide-react';
import { deriveQqAuthorizedBanner } from '../pages/account/cloudLoginStatus';
import {
  getWebCloudSnapshot,
  logoutWebCloud,
  pollWebCloudLogin,
  refreshWebCloudState,
  startWebCloudLogin,
  subscribeWebCloud,
  syncWebCloudLibrary,
  type WebCloudGrant,
} from '../shared/webCloudClient';
import { openExternalUrl } from '../shared/runtime';

export const WebCloudLoginCard: React.FC<{ isZh: boolean }> = ({ isZh }) => {
  const cloud = useSyncExternalStore(subscribeWebCloud, getWebCloudSnapshot, getWebCloudSnapshot);
  const [grant, setGrant] = useState<WebCloudGrant | null>(null);
  const [modalOpen, setModalOpen] = useState(false);
  const [stage, setStage] = useState<'pending' | 'opened' | 'idle'>('idle');
  const [busy, setBusy] = useState(false);
  const [syncing, setSyncing] = useState(false);
  const [notice, setNotice] = useState('');

  useEffect(() => {
    void refreshWebCloudState();
  }, []);

  useEffect(() => {
    if (!modalOpen || !grant) return;
    let cancelled = false;
    const tick = async () => {
      try {
        const res = await pollWebCloudLogin(grant);
        if (cancelled) return;
        if (res.status === 'opened') setStage('opened');
        if (res.status === 'succeeded') {
          setModalOpen(false);
          setGrant(null);
          setStage('idle');
          setNotice(isZh ? '云端账号登录成功，已解锁私有同步数据。' : 'Signed in to cloud account.');
          return;
        }
        if (['expired', 'superseded', 'failed'].includes(res.status)) {
          setGrant(null);
          setStage('idle');
          setNotice(
            res.message ||
              (res.status === 'superseded'
                ? isZh ? '已生成新的登录二维码，旧会话已作废。' : 'Superseded by new QR session.'
                : isZh ? '登录二维码已过期，请重新获取。' : 'QR expired.'),
          );
        }
      } catch (err) {
        if (!cancelled) setNotice(err instanceof Error ? err.message : String(err));
      }
    };
    const timer = window.setInterval(() => void tick(), 2000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [modalOpen, grant, isZh]);

  const handleStart = async () => {
    setBusy(true);
    setNotice('');
    try {
      const next = await startWebCloudLogin();
      setGrant(next);
      setStage('pending');
      setModalOpen(true);
    } catch (err) {
      setNotice(err instanceof Error ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  };

  const handleSync = async () => {
    setSyncing(true);
    setNotice('');
    try {
      await syncWebCloudLibrary();
      setNotice(isZh ? '已提交云端同步请求，请稍候刷新。' : 'Cloud sync requested.');
    } catch (err) {
      setNotice(err instanceof Error ? err.message : String(err));
    } finally {
      setSyncing(false);
    }
  };

  const qqNotice = deriveQqAuthorizedBanner(cloud.account, null, isZh);

  return (
    <>
      <section className="panel" style={{ marginBottom: 22 }}>
        <div
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            gap: 16,
            flexWrap: 'wrap',
          }}
        >
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
              <h2 style={{ fontSize: 18 }}>
                {isZh ? '云端账号授权（网页版 · 仅登录后可查私密数据）' : 'Cloud Account Sign-In (Web PKCE)'}
              </h2>
              <span className={`status-pill ${cloud.account ? 'ok' : 'info'}`} style={{ fontSize: 11 }}>
                {cloud.account
                  ? isZh ? '已登录' : 'Signed In'
                  : isZh ? '公开查询免登录 · 私密同步需登录' : 'Sign-In Optional for Public Lookup'}
              </span>
            </div>
            <p style={{ fontSize: 13, color: 'var(--text-secondary)', marginTop: 6, lineHeight: 1.7 }}>
              {cloud.account
                ? isZh
                  ? `当前登录：${cloud.account.nick || cloud.account.uid}（UID ${cloud.account.uid}）。`
                  : `Signed in as ${cloud.account.nick || cloud.account.uid} (UID ${cloud.account.uid}).`
                : isZh
                ? '公开 UID 资料、历史战绩与回放深度解析已对网页版直接开放；仅角色持有、背包道具与主动刷新等私密数据需要扫码登录。'
                : 'Public UID lookup, match history, and replay analysis work without signing in. Sign in only for private inventory and cloud sync.'}
            </p>
            {qqNotice && (
              <p style={{ fontSize: 12, color: 'var(--success)', marginTop: 4 }}>
                <ShieldCheck size={13} style={{ verticalAlign: 'text-bottom', marginRight: 4 }} />
                {qqNotice}
              </p>
            )}
          </div>

          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            {cloud.account ? (
              <>
                <button
                  type="button"
                  className="btn btn-primary"
                  disabled={syncing}
                  onClick={() => void handleSync()}
                >
                  <RefreshCw size={14} className={syncing ? 'spin' : ''} />
                  <span>{syncing ? (isZh ? '同步中…' : 'Syncing…') : isZh ? '同步云端账号数据' : 'Sync Cloud Data'}</span>
                </button>
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => void logoutWebCloud()}
                >
                  <LogOut size={14} />
                  <span>{isZh ? '退出登录' : 'Sign Out'}</span>
                </button>
              </>
            ) : (
              <button
                type="button"
                className="btn btn-primary"
                disabled={busy}
                onClick={() => void handleStart()}
              >
                <QrCode size={15} />
                <span>{busy ? (isZh ? '正在生成二维码…' : 'Creating QR…') : isZh ? '扫码 / 跳转登录' : 'Sign In via QR'}</span>
              </button>
            )}
          </div>
        </div>
        {notice && (
          <p role="status" style={{ marginTop: 10, fontSize: 12.5, color: 'var(--accent-text)' }}>
            {notice}
          </p>
        )}
      </section>

      {modalOpen && (
        <div
          role="dialog"
          aria-modal="true"
          aria-label={isZh ? '星枢网页版扫码登录' : 'XingShu Web Sign In'}
          style={{
            position: 'fixed',
            inset: 0,
            background: 'rgba(15, 18, 28, 0.65)',
            backdropFilter: 'blur(4px)',
            zIndex: 1200,
            display: 'grid',
            placeItems: 'center',
            padding: 20,
          }}
        >
          <div className="panel" style={{ width: 'min(430px, 100%)', textAlign: 'center', position: 'relative' }}>
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              aria-label={isZh ? '关闭' : 'Close'}
              onClick={() => setModalOpen(false)}
              style={{ position: 'absolute', top: 14, right: 14 }}
            >
              <X size={15} />
            </button>
            <h3 style={{ fontSize: 19, margin: '8px 0 12px' }}>
              {isZh ? '扫码或跳转登录星枢' : 'Sign in to XingShu'}
            </h3>
            {grant && stage === 'pending' ? (
              <>
                <img
                  src={`data:image/png;base64,${grant.qrPng}`}
                  width={230}
                  height={230}
                  alt={isZh ? '星枢授权二维码' : 'Authorization QR'}
                  style={{
                    borderRadius: 12,
                    background: '#fff',
                    padding: 8,
                    margin: '0 auto 12px',
                    border: '1px solid var(--border-color)',
                  }}
                />
                <p style={{ fontSize: 12.5, color: 'var(--text-secondary)', lineHeight: 1.7, marginBottom: 14 }}>
                  {isZh
                    ? '使用手机扫码，或在当前浏览器打开官方授权页完成验证。授权成功后将自动关闭弹窗。'
                    : 'Scan with mobile or open the authorization tab to complete login.'}
                </p>
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={() => void openExternalUrl(grant.loginUrl)}
                >
                  <ExternalLink size={14} />
                  <span>{isZh ? '在新标签页打开登录页' : 'Open Login Tab'}</span>
                </button>
              </>
            ) : (
              <p style={{ padding: '28px 0', color: 'var(--text-secondary)', fontSize: 13 }}>
                {grant
                  ? isZh
                    ? '检测到授权页已打开，请在授权页完成登录后自动刷新…'
                    : 'Login page opened, waiting for completion…'
                  : notice || (isZh ? '二维码已失效。' : 'QR expired.')}
              </p>
            )}
          </div>
        </div>
      )}
    </>
  );
};
