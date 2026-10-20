// A connection belongs to an installation only when both channel and executable match.
export function selectedRuntimeSessions<T extends { platformId: string; executablePath?: string | null; connectionState?: string }>(sessions: T[], platform: string, executable?: string | null): T[] {
  const normalize = (path: string) => path.replace(/\\/g, '/').replace(/\/+$/, '').toLowerCase();
  if (!executable) return [];
  return sessions.filter(session => session.platformId === platform && session.connectionState === 'CONNECTED' && !!session.executablePath && normalize(session.executablePath) === normalize(executable));
}
