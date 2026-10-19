import type { RuntimeSession } from '../types/runtimeSession.ts';

/** These values come only from the current backend connection. Bindings are expectations. */
export function livePlayerId(session: RuntimeSession | null | undefined): number | null {
  if (!session || session.status === 'DISCONNECTED' || session.connectionState === 'DISCONNECTED') return null;
  const value = session.livePlayerId !== undefined ? session.livePlayerId : session.playerId;
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : null;
}

export function liveMatchContext(session: RuntimeSession | null | undefined) {
  if (!session || session.status === 'DISCONNECTED' || session.connectionState === 'DISCONNECTED') return null;
  const context = session.matchContext;
  if (!context || (context.connectionId && context.connectionId !== session.connectionId)) return null;
  return context;
}
