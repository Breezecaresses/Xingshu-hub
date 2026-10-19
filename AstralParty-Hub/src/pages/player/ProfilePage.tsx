import React, { useEffect, useState } from 'react';
import { api } from '../../shared/api/tauri';
import type { PlayerProfile } from '../../shared/types';
import { User, RefreshCw } from 'lucide-react';
import { useTranslation } from '../../i18n';

export const ProfilePage: React.FC = () => {
  const { t } = useTranslation();
  const [profile, setProfile] = useState<PlayerProfile | null>(null);
  const [loading, setLoading] = useState(true);

  const fetchProfile = () => {
    setLoading(true);
    api.getPlayerProfile()
      .then(setProfile)
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    fetchProfile();
  }, []);

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
        <div>
          <h1 style={{ fontSize: 22, fontWeight: 700 }}>{t('player.profileTitle')}</h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: 13, marginTop: 4 }}>
            {t('player.profileSubtitle')}
          </p>
        </div>
        <button className="btn btn-secondary" onClick={fetchProfile} disabled={loading}>
          <RefreshCw size={14} className={loading ? 'spin' : ''} />
          <span>{t('common.refresh')}</span>
        </button>
      </div>

      <div className="panel">
        {profile ? (
          <div style={{ display: 'flex', gap: 20, alignItems: 'center' }}>
            <div
              style={{
                width: 72,
                height: 72,
                borderRadius: '50%',
                backgroundColor: 'var(--bg-secondary)',
                border: '2px solid var(--primary)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontSize: 28,
              }}
            >
              🎮
            </div>
            <div>
              <h2 style={{ fontSize: 20, fontWeight: 700 }}>
                {profile.display_name || 'Astral Party Player'}
              </h2>
              <div style={{ fontSize: 13, color: 'var(--text-muted)', marginTop: 4 }}>
                {t('player.canonicalId')}: <code>{profile.id}</code>
              </div>
              <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
                {t('player.profileCreated')}: {new Date(profile.created_at).toLocaleString()}
              </div>
            </div>
          </div>
        ) : (
          <div style={{ textAlign: 'center', padding: 36, color: 'var(--text-muted)' }}>
            <User size={36} style={{ marginBottom: 12, opacity: 0.5 }} />
            <p style={{ fontSize: 15, fontWeight: 600, color: 'var(--text-primary)' }}>
              {t('player.profileEmptyTitle')}
            </p>
            <p style={{ fontSize: 13, marginTop: 6, maxWidth: 540, margin: '6px auto 16px auto', lineHeight: 1.6 }}>
              {t('player.profileEmptyDesc')}
            </p>
            <div style={{ display: 'flex', gap: 12, justifyContent: 'center' }}>
              <a href="#/diagnostics" className="btn btn-primary btn-sm">
                {t('player.syncProviders')}
              </a>
              <a href="#/extensions/import" className="btn btn-secondary btn-sm">
                {t('player.importData')}
              </a>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
