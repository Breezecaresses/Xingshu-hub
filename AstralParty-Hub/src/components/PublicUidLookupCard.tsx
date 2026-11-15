import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Globe, Search, Sparkles, User, Trophy, RefreshCw } from 'lucide-react';
import { characterName, gameCatalog, matchDate, pickStaticEmoteAsset } from '../shared/gameLibrary';
import { fetchPublicPlayer, type PublicPlayerSnapshot } from '../shared/webCloudClient';
import { matchModeName } from '../shared/matchRating';
import { matchResult } from '../shared/matchResults';
import { GameImage } from '../pages/player/LibraryUI';

const LAST_UID_KEY = 'xingshu.public.lastUid';

export const PublicUidLookupCard: React.FC<{
  initialUid?: string;
  showMatches?: boolean;
  onLoaded?: (player: PublicPlayerSnapshot) => void;
}> = ({ initialUid, showMatches = true, onLoaded }) => {
  const [uid, setUid] = useState(() => {
    if (initialUid) return initialUid;
    try {
      return typeof localStorage !== 'undefined' ? localStorage.getItem(LAST_UID_KEY) || '' : '';
    } catch {
      return '';
    }
  });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [player, setPlayer] = useState<PublicPlayerSnapshot | null>(null);

  const runLookup = async (targetUid = uid) => {
    const clean = targetUid.trim();
    if (!clean) return;
    setLoading(true);
    setError('');
    try {
      const snap = await fetchPublicPlayer(clean);
      setPlayer(snap);
      onLoaded?.(snap);
      try {
        localStorage.setItem(LAST_UID_KEY, snap.uid || clean);
      } catch {
        // Ignore storage error
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (initialUid && /^\d{1,18}$/.test(initialUid)) {
      void runLookup(initialUid);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [initialUid]);

  const winRate =
    player?.career?.matches && player.career.matches > 0
      ? `${(((player.career.wins || 0) / player.career.matches) * 100).toFixed(1)}%`
      : '—';

  return (
    <section className="panel" style={{ marginBottom: 22 }} aria-label="公开 UID 查询（免登录）">
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, flexWrap: 'wrap', marginBottom: 12 }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <Globe size={17} color="var(--accent)" />
            <h2 style={{ fontSize: 17 }}>公开玩家与战绩查询（免登录）</h2>
            <span className="status-pill ok" style={{ fontSize: 11, padding: '2px 8px' }}>
              与 QQ Bot 趴数据 / 趴战绩同源公开接口
            </span>
          </div>
          <p style={{ fontSize: 12.5, color: 'var(--text-secondary)', marginTop: 4 }}>
            非登录可查资料（等级、胜场、公开战绩、回放入口）已与私密账号登录系分离；输入任意游戏 UID 即可即时查询并一键深入解析回放。
          </p>
        </div>
      </div>

      <form
        onSubmit={(e) => {
          e.preventDefault();
          void runLookup();
        }}
        style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center' }}
      >
        <input
          type="text"
          inputMode="numeric"
          aria-label="输入游戏 UID"
          placeholder="输入游戏 UID，例如 1556514"
          value={uid}
          onChange={(e) => setUid(e.target.value)}
          disabled={loading}
          style={{
            flex: '1 1 240px',
            background: 'var(--bg-input)',
            border: '1px solid var(--border-color)',
            borderRadius: 9,
            padding: '10px 12px',
            color: 'var(--text-primary)',
            font: 'inherit',
          }}
        />
        <button type="submit" className="btn btn-primary" disabled={loading || !uid.trim()}>
          {loading ? <RefreshCw size={14} className="spin" /> : <Search size={14} />}
          <span>{loading ? '查询中…' : '查询公开资料'}</span>
        </button>
      </form>

      {error && (
        <p role="alert" style={{ marginTop: 10, fontSize: 12.5, color: 'var(--danger)' }}>
          {error}
        </p>
      )}

      {player && (
        <div style={{ marginTop: 18 }}>
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 14,
              flexWrap: 'wrap',
              padding: '14px 16px',
              borderRadius: 12,
              background: 'rgba(28, 28, 28, 0.03)',
              border: '1px solid var(--border-color)',
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
              <div
                style={{
                  width: 40,
                  height: 40,
                  borderRadius: 11,
                  background: 'var(--accent-soft)',
                  color: 'var(--accent)',
                  display: 'grid',
                  placeItems: 'center',
                }}
              >
                <User size={19} />
              </div>
              <div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                  <strong style={{ fontSize: 16 }}>{player.nick || `UID ${player.uid}`}</strong>
                  <span className="status-pill info" style={{ fontSize: 11, padding: '2px 8px' }}>
                    Lv.{player.level ?? '—'}
                  </span>
                  <span
                    className={`status-pill ${player.online ? 'ok' : 'warning'}`}
                    style={{ fontSize: 11, padding: '2px 8px' }}
                  >
                    {player.online ? (player.busy ? '在线 · 对局/房间中' : '在线 · 大厅') : '当前离线'}
                  </span>
                </div>
                <small style={{ color: 'var(--text-muted)', fontSize: 12 }}>
                  UID {player.uid}
                  {player.observedAt ? ` · 更新于 ${new Date(player.observedAt).toLocaleString('zh-CN')}` : ''}
                </small>
              </div>
            </div>

            <div style={{ display: 'flex', gap: 18, flexWrap: 'wrap', fontSize: 12.5 }}>
              <div>
                <span style={{ color: 'var(--text-muted)', display: 'block', fontSize: 11 }}>总对局</span>
                <strong>{player.career?.matches ?? '—'}</strong>
              </div>
              <div>
                <span style={{ color: 'var(--text-muted)', display: 'block', fontSize: 11 }}>胜场 / 胜率</span>
                <strong>
                  {player.career?.wins ?? '—'} ({winRate})
                </strong>
              </div>
              <div>
                <span style={{ color: 'var(--text-muted)', display: 'block', fontSize: 11 }}>角色 / 皮肤</span>
                <strong>
                  {player.career?.heroes ?? '—'} / {player.career?.skins ?? '—'}
                </strong>
              </div>
              <div>
                <span style={{ color: 'var(--text-muted)', display: 'block', fontSize: 11 }}>收到点赞</span>
                <strong>{player.praise ?? '—'}</strong>
              </div>
            </div>
          </div>

          {showMatches && player.matches && player.matches.length > 0 && (
            <div style={{ marginTop: 14 }}>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 10 }}>
                <strong style={{ fontSize: 13.5, display: 'flex', alignItems: 'center', gap: 6 }}>
                  <Trophy size={14} color="var(--accent)" />
                  最近公开战绩（{player.matches.length} 场）
                </strong>
              </div>
              <div style={{ display: 'grid', gap: 8 }}>
                {player.matches.slice(0, 10).map((m, idx) => {
                  const result = matchResult(m.rank, m.mapId, gameCatalog.modes);
                  return (
                    <div
                      key={`${m.time}-${m.heroId}-${idx}`}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 12,
                        padding: '10px 14px',
                        borderRadius: 10,
                        border: '1px solid var(--border-color)',
                        background: 'var(--bg-input)',
                        flexWrap: 'wrap',
                      }}
                    >
                      <span
                        className={`status-pill ${result.outcome === 'win' ? 'ok' : 'info'}`}
                        style={{ minWidth: 56, justifyContent: 'center', fontSize: 11 }}
                      >
                        {result.label}
                      </span>
                      <div className="uid-match-avatar">
                        <GameImage asset={pickStaticEmoteAsset(m.heroId, `uid:${m.time}:${idx}`)} name={characterName(m.heroId)} className="portrait" />
                      </div>
                      <div style={{ flex: '1 1 200px' }}>
                        <strong style={{ fontSize: 13.5 }}>{characterName(m.heroId)}</strong>
                        <div style={{ fontSize: 11.5, color: 'var(--text-secondary)' }}>
                          {matchModeName(m.mapId, gameCatalog.modes)} · {m.version || '—'} · {matchDate(m.time)}
                        </div>
                      </div>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                        <code style={{ fontSize: 11.5, color: 'var(--text-muted)' }}>
                          {m.replayId || '无回放号'}
                        </code>
                        {m.replayId && (
                          <Link
                            className="btn btn-primary btn-sm"
                            to={`/player/replay?mid=${encodeURIComponent(m.replayId)}&server=cn`}
                          >
                            <Sparkles size={13} />
                            <span>深度解析</span>
                          </Link>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          )}
        </div>
      )}
    </section>
  );
};
