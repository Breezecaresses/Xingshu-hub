import { lazy, Suspense, useEffect, useState } from 'react';
import { cardTypes, gameCatalog, plainGameText } from '../../shared/gameLibrary';
import type { LibraryAltArt, LibraryCard } from '../../shared/gameLibrary';
import { GameImage } from './LibraryUI';
import type { SpineCardAsset } from './SpineCardArt';

const SpineCardArt = lazy(() => import('./SpineCardArt'));

export type AnimatedAltArt = LibraryAltArt & { video?: string; spine?: string; front?: string; animationKind?: 'video' | 'spine' | 'none' };

export function AltArtImage({ card, art }: { card: LibraryCard; art?: AnimatedAltArt | null }) {
  const poster = art?.spine ? (gameCatalog as unknown as { spineAssets?: Record<string, SpineCardAsset & { poster?: string }> }).spineAssets?.[art.spine]?.poster : undefined;
  return poster ? <img className="library-image" src={poster} alt={art?.name || card.name} loading="lazy" style={{ backgroundImage: art?.image && gameCatalog.assets[art.image] ? `url("${gameCatalog.assets[art.image]}")` : undefined, backgroundSize: 'cover', backgroundPosition: 'center' }} /> : <GameImage asset={art?.image || card.image} name={art?.name || card.name} />;
}

export function FullGameCard({ card, art, motion = false, allowSpine = false }: { card: LibraryCard; art?: AnimatedAltArt | null; motion?: boolean; allowSpine?: boolean }) {
  const [videoFailed, setVideoFailed] = useState(false);
  const [spineReady, setSpineReady] = useState(false);
  const [spineFailed, setSpineFailed] = useState(false);
  const video = art?.video ? gameCatalog.assets[art.video] : undefined;
  const spineAssets = (gameCatalog as unknown as { spineAssets?: Record<string, SpineCardAsset> }).spineAssets;
  const spine = art?.spine ? spineAssets?.[art.spine] : undefined;
  useEffect(() => { setVideoFailed(false); setSpineReady(false); setSpineFailed(false); }, [video, art?.spine, motion]);
  return <article className={`full-game-card card-type-${card.type}${art ? ' alt-art-card' : ''}`}>
    <div className="full-card-art">
      {motion && allowSpine && spineReady && !spineFailed ? <GameImage asset={art?.image || card.image} name={art?.name || card.name} /> : <AltArtImage card={card} art={art} />}
      {motion && video && !videoFailed && <video key={video} src={video} muted autoPlay loop playsInline preload="metadata" onError={() => setVideoFailed(true)} aria-label={`${art?.name || card.name}动态卡面`} />}
      {motion && allowSpine && spine && !spineFailed && <Suspense fallback={null}><SpineCardArt asset={spine} onReady={() => setSpineReady(true)} onError={() => setSpineFailed(true)} /></Suspense>}
      <div className="full-card-symbols"><span className="full-card-type">{(cardTypes[card.type] || '卡').slice(0, 1)}</span>{card.cost > 0 && <span className="full-card-cost" aria-label={`费用 ${card.cost}`}>{card.cost <= 5 ? '◆'.repeat(card.cost) : `◆ ×${card.cost}`}</span>}</div>
      {art && <span className="full-card-art-label">{motion && ((video && !videoFailed) || (allowSpine && spineReady && !spineFailed)) ? '动态异画' : '异画'}</span>}
      {(videoFailed || spineFailed) && <span className="card-animation-fallback">动态加载失败，已显示静态卡面</span>}
    </div>
    <div className="full-card-name">{card.name || `卡牌 #${card.id}`}</div>
    <div className="full-card-effect"><p>{plainGameText(card.description)}</p><div className="full-card-footer"><small>ASTRAL PARTY</small><span>{card.number || `#${card.id}`}</span><span className="full-card-star">✦</span></div></div>
  </article>;
}
