import type { ResolvedRuntimeSession, RuntimeSession } from '../../types/runtimeSession.ts';

export const SPECTATOR_PLATFORM_NAMES: Record<string, string> = {
  TAPTAP: 'TapTap',
  BILIBILI: '哔哩哔哩',
  STEAM_CN: 'Steam 国服',
  STEAM_GLOBAL: 'Steam 国际服',
  WEGAME: 'WeGame',
};

export interface MainRuntimeSummary {
  connected: boolean;
  bound: boolean;
  badgeText: string;
  summaryText: string;
}

function getPlatformName(platformId?: string | null): string {
  return SPECTATOR_PLATFORM_NAMES[platformId ?? ''] ?? '已绑定平台';
}

function resolveSessionUid(session: Pick<RuntimeSession, 'livePlayerId' | 'playerId'>): number | null {
  const uid = session.livePlayerId ?? session.playerId ?? null;
  return typeof uid === 'number' && uid > 0 ? uid : null;
}

function isConnectedSession(session: RuntimeSession): boolean {
  const state = session.connectionState ?? session.status ?? 'CONNECTED';
  return state === 'CONNECTED';
}

/**
 * Formats the header badge and summary label for the primary desktop game runtime.
 * Falls back to connected unbound desktop sessions so users are not told "Disconnected"
 * merely because they did not configure manual multi-account bindings.
 */
export function summarizeMainGameRuntime(
  mainSession: ResolvedRuntimeSession | null,
  activeRuntimeSessions: RuntimeSession[],
  spectatorSession: ResolvedRuntimeSession | null,
  isZh: boolean,
): MainRuntimeSummary {
  if (mainSession) {
    const platform = getPlatformName(mainSession.platformId);
    const uid = resolveSessionUid(mainSession.session) ?? mainSession.playerId;
    const badgeText = `${platform} · ${uid ?? (isZh ? '已连接' : 'Connected')}`;
    return {
      connected: true,
      bound: true,
      badgeText,
      summaryText: `${isZh ? '主游戏账号：' : 'Main Account: '}${badgeText}`,
    };
  }

  const fallback = activeRuntimeSessions
    .filter((s) => s.connectionId !== spectatorSession?.connectionId && isConnectedSession(s))
    .sort((a, b) => Number(resolveSessionUid(b) !== null) - Number(resolveSessionUid(a) !== null))[0];

  if (fallback) {
    const platform = SPECTATOR_PLATFORM_NAMES[fallback.platformId] ?? (fallback.platformId || (isZh ? '桌面端' : 'Desktop'));
    const uid = resolveSessionUid(fallback);
    const badgeText = uid
      ? `${platform} · ${uid} (${isZh ? '未绑定' : 'Unbound'})`
      : `${platform} · ${isZh ? '已连接 (未绑定)' : 'Connected (Unbound)'}`;
    return {
      connected: true,
      bound: false,
      badgeText,
      summaryText: `${isZh ? '主游戏账号：' : 'Main Account: '}${badgeText}`,
    };
  }

  const disconnected = isZh ? '未连接' : 'Disconnected';
  return {
    connected: false,
    bound: false,
    badgeText: disconnected,
    summaryText: isZh ? '主游戏账号：未连接' : 'Main: Disconnected',
  };
}
