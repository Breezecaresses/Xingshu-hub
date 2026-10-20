import { useEffect, useRef } from 'react';
import { SpinePlayer } from '@esotericsoftware/spine-player';
import '@esotericsoftware/spine-player/dist/spine-player.css';

export interface SpineCardAsset { skeleton: string; atlas: string; animation?: string; runtimeVersion?: string }

export default function SpineCardArt({ asset, onReady, onError }: { asset: SpineCardAsset; onReady: () => void; onError: () => void }) {
  const host = useRef<HTMLDivElement>(null);
  const playerRef = useRef<SpinePlayer | undefined>(undefined);
  const handlers = useRef({ onReady, onError });
  handlers.current = { onReady, onError };
  useEffect(() => {
    const container = host.current;
    if (!container) return;
    let disposed = false;
    let player: SpinePlayer | undefined;
    try {
      player = new SpinePlayer(container, {
        skeleton: asset.skeleton,
        atlas: asset.atlas,
        animation: asset.animation || 'Idle',
        showControls: false,
        showLoading: false,
        interactive: false,
        alpha: true,
        backgroundColor: '#00000000',
        preserveDrawingBuffer: true,
        viewport: { padLeft: 0, padRight: 0, padTop: 0, padBottom: 0 },
        success: current => { if (disposed) current.dispose(); else handlers.current.onReady(); },
        error: () => { if (!disposed) handlers.current.onError(); },
      });
      playerRef.current = player;
    } catch {
      handlers.current.onError();
    }
    return () => { disposed = true; playerRef.current = undefined; player?.dispose(); container.replaceChildren(); };
  }, [asset.skeleton, asset.atlas, asset.animation]);
  const exportPoster = () => playerRef.current?.canvas?.toBlob(blob => {
    if (!blob) return;
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = `${asset.skeleton.split('/').slice(-2)[0]}.png`;
    link.click();
    setTimeout(() => URL.revokeObjectURL(link.href), 1000);
  });
  return <><div ref={host} className="spine-card-art" aria-label="游戏原始 Spine 动态卡面" />{import.meta.env.DEV && <button className="spine-dev-poster" onClick={exportPoster}>导出异画预览</button>}</>;
}
