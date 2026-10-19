import React, { useEffect, useState } from 'react';
import { api } from '../../shared/api/tauri';
import type { Character, CharacterProgress } from '../../shared/types';
import { Users, RefreshCw } from 'lucide-react';
import { useTranslation } from '../../i18n';

export const CharactersPage: React.FC = () => {
  const { locale, t } = useTranslation();
  const isZh = locale === 'zh-CN';
  const [characters, setCharacters] = useState<Character[]>([]);
  const [progressList, setProgressList] = useState<CharacterProgress[]>([]);
  const [loading, setLoading] = useState(true);

  const fetchData = async () => {
    setLoading(true);
    try {
      const chars = await api.listCharacters();
      setCharacters(chars);
      const profile = await api.getPlayerProfile();
      if (profile) {
        const prog = await api.listCharacterProgress(profile.id);
        setProgressList(prog);
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchData();
  }, []);

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
        <div>
          <h1 style={{ fontSize: 22, fontWeight: 700 }}>{t('player.charactersTitle')}</h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: 13, marginTop: 4 }}>
            {t('player.charactersSubtitle')}
          </p>
        </div>
        <button className="btn btn-secondary" onClick={fetchData} disabled={loading}>
          <RefreshCw size={14} className={loading ? 'spin' : ''} />
          <span>{t('common.refresh')}</span>
        </button>
      </div>

      <div className="panel">
        {characters.length === 0 ? (
          <div style={{ textAlign: 'center', padding: 36, color: 'var(--text-muted)' }}>
            <Users size={36} style={{ marginBottom: 12, opacity: 0.5 }} />
            <p style={{ fontSize: 15, fontWeight: 600, color: 'var(--text-primary)' }}>
              {t('player.charactersEmptyTitle')}
            </p>
            <p style={{ fontSize: 13, marginTop: 6, maxWidth: 540, margin: '6px auto 16px auto', lineHeight: 1.6 }}>
              {t('player.charactersEmptyDesc')}
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
        ) : (
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: 16 }}>
            {characters.map((c) => {
              const prog = progressList.find((p) => p.character_key === c.character_key);
              return (
                <div key={c.character_key} className="stat-card" style={{ padding: 16 }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <span style={{ fontWeight: 600, fontSize: 15 }}>{c.name}</span>
                    <span className="sidebar-tag">{c.rarity || (isZh ? '普通' : 'Common')}</span>
                  </div>
                  <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 4 }}>
                    {isZh ? '标识' : 'Key'}: <code>{c.character_key}</code>
                  </div>
                  <div style={{ borderTop: '1px solid var(--border-color)', marginTop: 10, paddingTop: 10 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12 }}>
                      <span style={{ color: 'var(--text-secondary)' }}>{t('common.status')}:</span>
                      <strong style={{ color: prog?.owned ? 'var(--success)' : 'var(--text-muted)' }}>
                        {prog?.owned ? t('player.owned') : t('player.unowned')}
                      </strong>
                    </div>
                    {prog?.level && (
                      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, marginTop: 4 }}>
                        <span style={{ color: 'var(--text-secondary)' }}>{t('player.level')}:</span>
                        <strong>Lv. {prog.level}</strong>
                      </div>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
};
