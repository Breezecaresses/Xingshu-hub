import React, { useEffect, useState } from 'react';
import { api } from '../../shared/api/tauri';
import type { Card, CardInventory } from '../../shared/types';
import { Layers, RefreshCw } from 'lucide-react';
import { useTranslation } from '../../i18n';

export const CardsPage: React.FC = () => {
  const { locale, t } = useTranslation();
  const isZh = locale === 'zh-CN';
  const [cards, setCards] = useState<Card[]>([]);
  const [inventoryList, setInventoryList] = useState<CardInventory[]>([]);
  const [loading, setLoading] = useState(true);

  const fetchData = async () => {
    setLoading(true);
    try {
      const allCards = await api.listCards();
      setCards(allCards);
      const profile = await api.getPlayerProfile();
      if (profile) {
        const inv = await api.listCardInventory(profile.id);
        setInventoryList(inv);
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
          <h1 style={{ fontSize: 22, fontWeight: 700 }}>{t('player.cardsTitle')}</h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: 13, marginTop: 4 }}>
            {t('player.cardsSubtitle')}
          </p>
        </div>
        <button className="btn btn-secondary" onClick={fetchData} disabled={loading}>
          <RefreshCw size={14} className={loading ? 'spin' : ''} />
          <span>{t('common.refresh')}</span>
        </button>
      </div>

      <div className="panel">
        {cards.length === 0 ? (
          <div style={{ textAlign: 'center', padding: 36, color: 'var(--text-muted)' }}>
            <Layers size={36} style={{ marginBottom: 12, opacity: 0.5 }} />
            <p style={{ fontSize: 15, fontWeight: 600, color: 'var(--text-primary)' }}>
              {t('player.cardsEmptyTitle')}
            </p>
            <p style={{ fontSize: 13, marginTop: 6, maxWidth: 540, margin: '6px auto 16px auto', lineHeight: 1.6 }}>
              {t('player.cardsEmptyDesc')}
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
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 16 }}>
            {cards.map((c) => {
              const inv = inventoryList.find((i) => i.card_key === c.card_key);
              return (
                <div key={c.card_key} className="stat-card" style={{ padding: 16 }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <span style={{ fontWeight: 600, fontSize: 14 }}>{c.name}</span>
                    <span className="sidebar-tag">{c.rarity || (isZh ? '普通' : 'Common')}</span>
                  </div>
                  <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 4 }}>
                    {isZh ? '类型' : 'Type'}: {c.card_type || (isZh ? '主动' : 'Active')}
                  </div>
                  <div style={{ borderTop: '1px solid var(--border-color)', marginTop: 10, paddingTop: 10 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12 }}>
                      <span style={{ color: 'var(--text-secondary)' }}>{t('player.inventory')}:</span>
                      <strong>{inv?.quantity ? `x${inv.quantity}` : '0'}</strong>
                    </div>
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
