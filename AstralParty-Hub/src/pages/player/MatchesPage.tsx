import React, { useEffect, useState } from 'react';
import { api } from '../../shared/api/tauri';
import type { MatchRecord } from '../../shared/types';
import {
  History,
  Timer,
  Calendar,
  RefreshCw,
} from 'lucide-react';
import { useTranslation } from '../../i18n';

export const MatchesPage: React.FC = () => {
  const { locale, t } = useTranslation();
  const [matches, setMatches] = useState<MatchRecord[]>([]);
  const [loading, setLoading] = useState(true);

  const fetchMatches = async () => {
    setLoading(true);
    try {
      const data = await api.listMatches();
      setMatches(data);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchMatches();
  }, []);

  const totalMatches = matches.length;
  const wins = matches.filter((m) => m.placement === 1).length;
  const winRate = totalMatches > 0 ? Math.round((wins / totalMatches) * 100) : 0;
  const totalStars = matches.reduce((acc, m) => acc + m.final_stars, 0);

  const isZh = locale === 'zh-CN';

  const localizeBilingualText = (val: string): string => {
    if (!val) return val;
    const match = val.match(/^([^\(（]+)[(（]([^\)）]+)[)）]$/);
    if (match) {
      return isZh ? match[1].trim() : match[2].trim();
    }
    return val;
  };

  return (
    <div>
      <div style={{ marginBottom: 20 }}>
        <h1 style={{ fontSize: 22, fontWeight: 700 }}>{t('player.matchesTitle')}</h1>
        <p style={{ color: 'var(--text-secondary)', fontSize: 13, marginTop: 4 }}>
          {t('player.matchesSubtitle')}
        </p>
      </div>

      {/* Analytics Summary Header */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 16, marginBottom: 20 }}>
        <div className="panel" style={{ margin: 0 }}>
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>{isZh ? '总对局场次' : 'Total Matches'}</div>
          <div style={{ fontSize: 24, fontWeight: 700, color: '#fff', marginTop: 4 }}>
            {totalMatches} <span style={{ fontSize: 13, fontWeight: 400, color: 'var(--text-secondary)' }}>{isZh ? '场' : 'Matches'}</span>
          </div>
        </div>

        <div className="panel" style={{ margin: 0 }}>
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>{isZh ? '登顶吃鸡率' : '1st Place Rate'}</div>
          <div style={{ fontSize: 24, fontWeight: 700, color: 'var(--warning)', marginTop: 4 }}>
            {winRate}% <span style={{ fontSize: 13, fontWeight: 400, color: 'var(--text-secondary)' }}>({wins} {isZh ? '胜' : 'Wins'})</span>
          </div>
        </div>

        <div className="panel" style={{ margin: 0 }}>
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>{isZh ? '累积收割星数' : 'Total Stars Collected'}</div>
          <div style={{ fontSize: 24, fontWeight: 700, color: 'var(--accent)', marginTop: 4 }}>
            ⭐ {totalStars}
          </div>
        </div>

        <div className="panel" style={{ margin: 0 }}>
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>{isZh ? '数据来源层级' : 'Data Source Level'}</div>
          <div style={{ fontSize: 14, fontWeight: 600, color: 'var(--success)', marginTop: 8 }}>
            Level 5: APX Runtime Hooks
          </div>
        </div>
      </div>

      {/* Match History Table */}
      <div className="panel">
        <div className="panel-header">
          <div className="panel-title">
            <History size={18} color="var(--primary)" />
            <span>{isZh ? '近期对局记录' : 'Recent Matches'}</span>
          </div>
          <button className="btn btn-secondary btn-sm" onClick={fetchMatches} disabled={loading}>
            <RefreshCw size={14} className={loading ? 'spin' : ''} />
            <span>{t('common.refresh')}</span>
          </button>
        </div>

        {matches.length === 0 ? (
          <div style={{ textAlign: 'center', padding: 36, color: 'var(--text-muted)' }}>
            <p style={{ fontSize: 15, fontWeight: 600, color: 'var(--text-primary)' }}>
              {t('player.matchesEmptyTitle')}
            </p>
            <p style={{ fontSize: 13, marginTop: 6 }}>
              {t('player.matchesEmptyDesc')}
            </p>
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            {matches.map((m) => {
              const isWinner = m.placement === 1;
              return (
                <div
                  key={m.match_id}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    padding: 16,
                    background: 'var(--bg-card)',
                    borderRadius: 'var(--radius-md)',
                    border: isWinner ? '1px solid rgba(245, 158, 11, 0.4)' : '1px solid var(--border-color)',
                  }}
                >
                  <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
                    <div
                      style={{
                        width: 44,
                        height: 44,
                        borderRadius: 'var(--radius-sm)',
                        background: isWinner ? 'rgba(245, 158, 11, 0.15)' : 'var(--bg-secondary)',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        color: isWinner ? 'var(--warning)' : 'var(--text-secondary)',
                        fontWeight: 800,
                        fontSize: 16,
                      }}
                    >
                      #{m.placement}
                    </div>

                    <div>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                        <span style={{ fontSize: 15, fontWeight: 700, color: '#fff' }}>
                          {localizeBilingualText(m.mode)}
                        </span>
                        <span className="badge badge-default">{localizeBilingualText(m.map_name)}</span>
                        {isWinner && <span className="badge badge-warning">🏆 {isZh ? '胜者吃鸡' : 'Winner'}</span>}
                      </div>

                      <div style={{ display: 'flex', gap: 14, marginTop: 6, fontSize: 12, color: 'var(--text-secondary)' }}>
                        <span>{t('player.character')}: <strong>{localizeBilingualText(m.character_used)}</strong></span>
                        <span>{isZh ? '造成伤害' : 'Damage'}: <strong>{m.total_damage_dealt}</strong></span>
                        <span>{isZh ? '出牌次数' : 'Cards Played'}: <strong>{m.total_cards_played}</strong></span>
                        <span>{t('player.stars')}: <strong>⭐ {m.final_stars}</strong></span>
                      </div>
                    </div>
                  </div>

                  <div style={{ textAlign: 'right' }}>
                    <div style={{ fontSize: 12, color: 'var(--text-muted)', display: 'flex', alignItems: 'center', gap: 4, justifyContent: 'flex-end' }}>
                      <Calendar size={12} />
                      <span>{m.played_at}</span>
                    </div>
                    <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 4, display: 'flex', alignItems: 'center', gap: 4, justifyContent: 'flex-end' }}>
                      <Timer size={12} />
                      <span>{isZh ? '耗时' : 'Duration'} {Math.floor(m.duration_seconds / 60)} {isZh ? '分钟' : 'mins'}</span>
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
