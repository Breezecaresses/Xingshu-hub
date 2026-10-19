/**
 * Project XingShu — Spectator & Runtime Session Event Service
 * Phase: HS4-P1.5
 * Lane 3: Core Multi-Runtime Architecture
 *
 * Listens for live multi-runtime session lifecycle events and spectator hand snapshot streams:
 * - 'runtime://session-changed': Emitted whenever a runtime connects, authenticates, touches heartbeat, or updates playerId.
 * - 'runtime://session-disconnected': Emitted when a specific runtime disconnects without impacting other active sessions.
 * - 'spectator://hand-snapshot': Emitted with tagged connectionId and platformId for real-time four-player hand stream.
 *
 * Integrates with RuntimeSessionResolver for canonical routing and execution session selection.
 */

import { type RuntimeSession, type RuntimeResolutionResult, type ResolvedRuntimeSession } from '../types/runtimeSession.ts';
import { runtimeSessionResolver } from './runtimeSessionResolver.ts';
import type { SpectatorHandPayload } from '../shared/types/index.ts';
import { getRuntimeSessions } from '../shared/api/tauri.ts';

export type SessionChangeCallback = (sessions: RuntimeSession[], result: RuntimeResolutionResult) => void;
export type HandSnapshotCallback = (snapshot: SpectatorHandPayload) => void;

export class SpectatorService {
  private sessions: Map<string, RuntimeSession> = new Map();
  private snapshotsByConnection: Map<string, SpectatorHandPayload> = new Map();
  private latestSnapshot: SpectatorHandPayload | null = null;
  private sessionCallbacks: Set<SessionChangeCallback> = new Set();
  private snapshotCallbacks: Set<HandSnapshotCallback> = new Set();
  private unlistenFns: Array<() => void> = [];
  private isListening = false;

  constructor() {
    // Initial sync
    this.refreshSessions().catch(() => {});
  }

  /**
   * Starts listening to Tauri runtime session and spectator stream events.
   */
  public async initListeners(): Promise<void> {
    if (this.isListening) return;
    this.isListening = true;

    try {
      // Dynamic import to prevent Node test failures when @tauri-apps/api is unavailable
      const { listen } = await import('@tauri-apps/api/event');

      const unlistenChanged = await listen<RuntimeSession>('runtime://session-changed', (event) => {
        this.handleSessionChanged(event.payload);
      });
      this.unlistenFns.push(unlistenChanged);

      const unlistenDisconnected = await listen<{ connectionId: string }>('runtime://session-disconnected', (event) => {
        const connId = event.payload?.connectionId || (event.payload as unknown as string);
        if (connId) {
          this.handleSessionDisconnected(connId);
        }
      });
      this.unlistenFns.push(unlistenDisconnected);

      const unlistenSnapshot = await listen<SpectatorHandPayload>('spectator://hand-snapshot', (event) => {
        this.handleHandSnapshot(event.payload);
      });
      this.unlistenFns.push(unlistenSnapshot);
    } catch {
      // Tauri environment not present or mock mode
    }
  }

  /**
   * Refreshes active sessions directly from Tauri backend IPC.
   */
  public async refreshSessions(): Promise<RuntimeSession[]> {
    try {
      const backendSessions = await getRuntimeSessions();
      if (Array.isArray(backendSessions)) {
        this.sessions.clear();
        for (const s of backendSessions) {
          if (s && s.connectionId) {
            this.sessions.set(s.connectionId, {
              ...s,
              status: s.status || 'CONNECTED',
            });
          }
        }
        this.notifySessionSubscribers();
      }
    } catch {
      // Fallback
    }
    return this.getActiveSessions();
  }

  /**
   * Ingests a session changed event.
   */
  public handleSessionChanged(session: RuntimeSession): void {
    if (!session || !session.connectionId) return;
    const formatted: RuntimeSession = {
      ...session,
      status: 'CONNECTED',
    };
    this.sessions.set(session.connectionId, formatted);
    this.notifySessionSubscribers();
  }

  /**
   * Ingests a session disconnected event.
   */
  public handleSessionDisconnected(connectionId: string): void {
    if (this.sessions.has(connectionId)) {
      this.sessions.delete(connectionId);
      this.snapshotsByConnection.delete(connectionId);
      this.notifySessionSubscribers();
    }
  }

  /**
   * Ingests a spectator hand snapshot.
   */
  public handleHandSnapshot(snapshot: SpectatorHandPayload): void {
    if (!snapshot) return;
    this.latestSnapshot = snapshot;
    if (snapshot.connectionId) {
      this.snapshotsByConnection.set(snapshot.connectionId, snapshot);
    }
    for (const cb of this.snapshotCallbacks) {
      try {
        cb(snapshot);
      } catch (err) {
        console.error('Error in snapshot callback:', err);
      }
    }
  }

  /**
   * Returns all active runtime sessions.
   */
  public getActiveSessions(): RuntimeSession[] {
    return Array.from(this.sessions.values());
  }

  /**
   * Resolves active sessions using canonical bindings and routing rules.
   */
  public getResolution(accountMode: string | null = 'USER_SPECTATOR'): RuntimeResolutionResult {
    return runtimeSessionResolver.resolve(this.getActiveSessions(), accountMode);
  }

  /**
   * Returns the designated spectator execution session.
   */
  public getSpectatorExecutionSession(accountMode: string | null = 'USER_SPECTATOR'): ResolvedRuntimeSession | null {
    return runtimeSessionResolver.selectSpectatorExecutionSession(this.getActiveSessions(), accountMode);
  }

  /**
   * Returns the latest spectator hand snapshot, optionally filtered by connectionId.
   */
  public getLatestHandPayload(connectionId?: string): SpectatorHandPayload | null {
    if (connectionId) {
      return this.snapshotsByConnection.get(connectionId) || null;
    }
    return this.latestSnapshot;
  }

  /**
   * Subscribes to runtime session list and resolution changes.
   */
  public onSessionsUpdated(callback: SessionChangeCallback): () => void {
    this.sessionCallbacks.add(callback);
    callback(this.getActiveSessions(), this.getResolution());
    return () => {
      this.sessionCallbacks.delete(callback);
    };
  }

  /**
   * Subscribes to incoming spectator hand snapshots.
   */
  public onHandSnapshot(callback: HandSnapshotCallback): () => void {
    this.snapshotCallbacks.add(callback);
    if (this.latestSnapshot) {
      callback(this.latestSnapshot);
    }
    return () => {
      this.snapshotCallbacks.delete(callback);
    };
  }

  private notifySessionSubscribers(): void {
    const sessions = this.getActiveSessions();
    const resolution = this.getResolution();
    for (const cb of this.sessionCallbacks) {
      try {
        cb(sessions, resolution);
      } catch (err) {
        console.error('Error in session callback:', err);
      }
    }
  }

  /**
   * Cleans up all listeners and subscriptions.
   */
  public destroy(): void {
    for (const unlisten of this.unlistenFns) {
      try {
        unlisten();
      } catch {}
    }
    this.unlistenFns = [];
    this.sessionCallbacks.clear();
    this.snapshotCallbacks.clear();
    this.isListening = false;
  }
}

export const spectatorService = new SpectatorService();
