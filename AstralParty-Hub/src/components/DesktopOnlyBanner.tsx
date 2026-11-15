import React from 'react';
import { Link } from 'react-router-dom';
import { Monitor, Globe, ArrowRight } from 'lucide-react';
import { useTranslation } from '../i18n';
import { getDesktopFeatureMeta, isTauriRuntime } from '../shared/runtime';

export const DesktopOnlyBanner: React.FC<{ feature: string; compact?: boolean }> = ({
  feature,
  compact = false,
}) => {
  const { locale } = useTranslation();
  if (isTauriRuntime()) return null;

  const isZh = locale === 'zh-CN';
  const meta = getDesktopFeatureMeta(feature);

  return (
    <section
      className="panel"
      role="status"
      style={{
        marginBottom: compact ? 14 : 20,
        padding: compact ? '14px 18px' : '20px 22px',
        borderColor: 'rgba(232, 104, 10, 0.28)',
        background: 'linear-gradient(125deg, rgba(255, 142, 42, 0.08), rgba(107, 138, 255, 0.06))',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'flex-start', gap: 14, flexWrap: 'wrap' }}>
        <div
          style={{
            width: 38,
            height: 38,
            borderRadius: 11,
            background: 'rgba(232, 104, 10, 0.14)',
            color: 'var(--accent)',
            display: 'grid',
            placeItems: 'center',
            flexShrink: 0,
          }}
        >
          <Monitor size={19} />
        </div>
        <div style={{ flex: '1 1 280px' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap', marginBottom: 6 }}>
            <strong style={{ fontSize: 15, color: 'var(--text-primary)' }}>
              {isZh ? meta.titleZh : meta.titleEn}
            </strong>
            <span
              className="status-pill warning"
              style={{ fontSize: 11, padding: '2px 9px' }}
            >
              {isZh ? '仅桌面端支持' : 'Desktop Only'}
            </span>
          </div>
          <p style={{ fontSize: 13, color: 'var(--text-secondary)', lineHeight: 1.7 }}>
            {isZh ? meta.reasonZh : meta.reasonEn}
          </p>
          {!compact && (
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 12 }}>
              <Link className="btn btn-primary btn-sm" to="/player/replay">
                <Globe size={13} />
                <span>{isZh ? '前往回放深度解析' : 'Open Replay Analyzer'}</span>
                <ArrowRight size={13} />
              </Link>
              <Link className="btn btn-secondary btn-sm" to="/player/profile">
                <span>{isZh ? '免登录 UID 查询' : 'Public UID Lookup'}</span>
              </Link>
              <Link className="btn btn-secondary btn-sm" to="/player/matches">
                <span>{isZh ? '对战记录' : 'Match History'}</span>
              </Link>
              <Link className="btn btn-secondary btn-sm" to="/account">
                <span>{isZh ? '云端账号登录' : 'Cloud Sign-In'}</span>
              </Link>
            </div>
          )}
        </div>
      </div>
    </section>
  );
};
