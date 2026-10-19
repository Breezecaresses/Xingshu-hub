/**
 * Project XingShu — Auto-Follow Spectator Integration Service
 * Phase: HS4-P1.6
 *
 * Coordinates between:
 * - RuntimeSessionResolver (canonical dual-routing)
 * - GameAccountService (expected main and spectator account bindings)
 * - AutoFollowStateMachine (11 lifecycle states & transitions)
 * - Tauri IPC bridge (api.sendWatchTarget, api.launchGameUri)
 */

import {
  AutoFollowStateMachine,
  type AutoFollowState,
  type AutoFollowContext,
  type WatchTargetPrimitive,
  type WatchTargetResponse,
  verifyWatchTargetSecurity,
} from './autoFollowStateMachine.ts';

export {
  type AutoFollowState,
  type AutoFollowContext,
  type WatchTargetPrimitive,
  type WatchTargetResponse,
  AUTO_FOLLOW_STATE_CONFIG_ZH,
} from './autoFollowStateMachine.ts';

import { api } from '../shared/api/tauri.ts';
import { gameAccountService } from './gameAccountService.ts';
import { runtimeSessionResolver } from './runtimeSessionResolver.ts';
import type { RuntimeSession } from '../types/runtimeSession.ts';
import { liveMatchContext, livePlayerId } from './runtimeTruth.ts';

export class AutoFollowService {
  private machine: AutoFollowStateMachine;
  private isListening = false;
  private unlistenFns: Array<() => void> = [];
  private sendingWatchTarget = false;
  private lastWatchTargetKey: string | null = null;
  private lastMainContext: { connectionId: string; sequence: number } | null = null;
  private executionMode: 'USER_OWNED' | 'LOCAL_MANAGED' | 'PAUSED' = 'USER_OWNED';
  private managedDispatch: ((target: WatchTargetPrimitive) => Promise<string>) | null = null;

  public setExecutionMode(mode: 'USER_OWNED' | 'LOCAL_MANAGED' | 'PAUSED', dispatch?: (target: WatchTargetPrimitive) => Promise<string>): void {
    if (this.executionMode === mode) {
      // Route remounts must use the current hook's dispatcher and refs.
      if (dispatch) this.managedDispatch = dispatch;
      return;
    }
    this.executionMode = mode;
    this.managedDispatch = dispatch ?? null;
    this.lastMainContext = null;
    this.resetToIdle();
  }

  constructor(customContext?: Partial<AutoFollowContext>) {
    this.machine = new AutoFollowStateMachine(customContext);
    this.syncBindings();
  }

  /**
   * Syncs bound player IDs from GameAccountService.
   */
  public syncBindings(): void {
    try {
      const bindings = gameAccountService.getBindings();
      const main = bindings.find((b) => b.role === 'MAIN');
      const spectator = bindings.find((b) => b.role === 'USER_SPECTATOR');

      this.machine.configureBindings(main?.playerId, spectator?.playerId, spectator?.platformId);
    } catch {
      // Fallback
    }
  }

  public getContext(): AutoFollowContext {
    return this.machine.getContext();
  }

  public getState(): AutoFollowState {
    return this.machine.getState();
  }

  public subscribe(
    listener: (ctx: AutoFollowContext) => void
  ): () => void {
    return this.machine.subscribe((ctx) => listener(ctx));
  }

  /**
   * Initializes Tauri event listeners for real-time auto-follow coordination.
   */
  public async initListeners(): Promise<void> {
    if (this.isListening) return;
    this.isListening = true;

    try {
      const { listen } = await import('@tauri-apps/api/event');

      const unlistenResponse = await listen<WatchTargetResponse>(
        'spectator://watch-target-response',
        (event) => {
          if (event.payload) {
            this.machine.dispatch({
              type: 'WATCH_RESPONSE_RECEIVED',
              response: event.payload,
            });
          }
        }
      );
      this.unlistenFns.push(unlistenResponse);

      const unlistenSnap = await listen<any>('spectator://hand-snapshot', (event) => {
        if (event.payload && event.payload.roomId) {
          this.machine.dispatch({
            type: 'HAND_SNAPSHOT_RECEIVED',
            roomId: event.payload.roomId,
            epoch: event.payload.spectatorSessionEpoch,
            sequence: event.payload.sequenceNumber,
          });
        }
      });
      this.unlistenFns.push(unlistenSnap);

      const unlistenDisconn = await listen<{ connectionId: string }>(
        'runtime://session-disconnected',
        (event) => {
          const connId = event.payload?.connectionId;
          this.machine.dispatch({
            type: 'SPECTATOR_DISCONNECTED',
            connectionId: connId,
          });
        }
      );
      this.unlistenFns.push(unlistenDisconn);
    } catch {
      // Running in mock/test environment without Tauri
    }
  }

  /**
   * Evaluates active runtime sessions and synchronizes state machine inputs.
   */
  public updateRuntimeSessions(sessions: RuntimeSession[]): void {
    if (this.executionMode === 'PAUSED') return;
    this.syncBindings();
    runtimeSessionResolver.setBindings(gameAccountService.getBindings());
    const resolution = runtimeSessionResolver.resolve(sessions, 'USER_SPECTATOR');
    const specSession = resolution.spectatorSession;
    const mainSession = resolution.mainSession;

    // 1. Process and Connection Status
    if (specSession && specSession.status === 'CONNECTED') {
      const livePid = specSession.processId;
      const observedPlayerId = livePlayerId(specSession.session);
      this.machine.configureBindings(mainSession?.session.expectedBoundPlayerId, specSession.session.expectedBoundPlayerId);

      this.machine.dispatch({
        type: 'PROCESS_STATUS',
        isRunning: true,
        pid: livePid,
      });

      const connected = this.machine.dispatch({
        type: 'RUNTIME_CONNECTED',
        connectionId: specSession.connectionId,
        platformId: specSession.platformId,
        playerId: observedPlayerId,
      });
      if (connected.actionRequired === 'SEND_WATCH_TARGET') void this.sendWatchTargetCommand();

      if (observedPlayerId !== null) {
        const identified = this.machine.dispatch({
          type: 'IDENTITY_RESOLVED',
          playerId: observedPlayerId,
        });
        if (identified.actionRequired === 'SEND_WATCH_TARGET') void this.sendWatchTargetCommand();
      }
    } else {
      const hasTap = sessions.some((s) => s.platformId === this.machine.getContext().targetPlatform && s.status === 'CONNECTED');
      if (!hasTap) {
        this.machine.dispatch({ type: 'SPECTATOR_DISCONNECTED', connectionId: this.machine.getContext().spectatorConnectionId ?? undefined });
        this.machine.dispatch({
          type: 'PROCESS_STATUS',
          isRunning: false,
        });
      }
    }

    const rawSession = mainSession?.role === 'MAIN' ? mainSession.session : null;
    const matchCtx = liveMatchContext(rawSession);
    if (rawSession && matchCtx) {
      if (this.lastMainContext?.connectionId === rawSession.connectionId && matchCtx.sequence < this.lastMainContext.sequence) return;
      this.lastMainContext = { connectionId: rawSession.connectionId, sequence: matchCtx.sequence };
    }
    if (matchCtx?.matchState === 'MAIN_IN_MATCH') {
      const internalRoomId = matchCtx.internalRoomId ?? null;
      const code = typeof matchCtx.spectatorCode === 'string' ? matchCtx.spectatorCode : null;
      const previous = this.machine.getContext().activeTarget;
      if (previous && (previous.roomId !== (internalRoomId ?? 0) || (previous.watchCode && code && previous.watchCode !== code))) {
        this.onMainMatchEnded('Main runtime moved to another match');
      }
      void this.onMainMatchDetected({
        roomId: internalRoomId ?? 0,
        internalRoomId,
        spectatorCode: code,
        watchCode: code,
        matchKind: matchCtx.matchKind,
        playerCount: matchCtx.playerCount,
        rosterPlayerIds: matchCtx.rosterPlayerIds,
      });
    } else if (this.machine.getContext().activeTarget) {
      this.onMainMatchEnded('Main runtime is no longer in an active match');
    }
  }

  /**
   * Invoked when Main Player enters a match.
   */
  public async onMainMatchDetected(target: WatchTargetPrimitive): Promise<void> {
    if (this.executionMode === 'PAUSED') return;
    const res = this.machine.dispatch({
      type: 'MAIN_MATCH_DETECTED',
      target,
    });

    if (res.actionRequired === 'LAUNCH_CLIENT') {
      await this.launchSpectatorClient();
    } else if (res.actionRequired === 'SEND_WATCH_TARGET') {
      await this.sendWatchTargetCommand();
    }
  }

  /**
   * Invoked when Main Player leaves or ends match.
   */
  public onMainMatchEnded(reason?: string): void {
    this.lastWatchTargetKey = null;
    this.machine.dispatch({
      type: 'MAIN_MATCH_ENDED',
      reason,
    });
  }

  /**
   * Launches TapTap Spectator Client via Tauri launcher.
   */
  public async launchSpectatorClient(): Promise<boolean> {
    if (this.executionMode === 'PAUSED') return false;
    const binding = gameAccountService.getBindings().find(b => b.role === 'USER_SPECTATOR');
    if (!binding) return false;
    this.syncBindings();
    this.machine.dispatch({ type: 'CLIENT_LAUNCH_INITIATED' });
    try {
      const result = await api.launchGameUri(undefined, this.machine.getContext().targetPlatform);
      if (!result.success && !result.alreadyRunning) {
        console.warn('Failed to launch spectator client:', result.message);
        this.machine.dispatch({ type: 'CLIENT_LAUNCH_TIMEOUT' });
        return false;
      }
      return true;
    } catch (err) {
      console.warn('Failed to launch spectator client:', err);
      return false;
    }
  }

  /**
   * Dispatches WATCH_TARGET local control command to TapTap spectator runtime.
   * STRICT SECURITY ENFORCEMENT:
   * - Destination MUST be verified TAPTAP session.
   * - Never route to Steam MAIN!
   */
  public async sendWatchTargetCommand(): Promise<string | null> {
    if (this.executionMode === 'PAUSED') return null;
    if (this.sendingWatchTarget) return null;
    const ctx = this.machine.getContext();
    const connId = ctx.spectatorConnectionId;
    const target = ctx.activeTarget;

    if (!connId || !target || !(target.watchCode || target.spectatorCode)) {
      console.warn('Cannot send WATCH_TARGET: Missing connectionId or activeTarget');
      return null;
    }
    const targetKey = JSON.stringify([connId, target.internalRoomId ?? target.roomId, target.spectatorCode || target.watchCode]);
    if (targetKey === this.lastWatchTargetKey) return null;

    // Security Gate Verification
    const securityCheck = verifyWatchTargetSecurity(
      {
        connectionId: connId,
        platformId: ctx.targetPlatform,
        playerId: ctx.spectatorPlayerId,
      },
      ctx.expectedSpectatorPlayerId, ctx.targetPlatform
    );

    if (!securityCheck.allowed) {
      console.error('WATCH_TARGET blocked by Security Guard:', securityCheck.violationReason);
      this.machine.dispatch({
        type: 'WATCH_RESPONSE_RECEIVED',
        response: {
          status: 'WATCH_TARGET_INVALID',
          requestId: 'security-rejected',
          reason: securityCheck.violationReason,
        },
      });
      return null;
    }

    this.sendingWatchTarget = true;
    this.lastWatchTargetKey = targetKey;
    try {
      const requestId = this.executionMode === 'LOCAL_MANAGED'
        ? await this.managedDispatch!(target)
        : await api.sendWatchTarget(
        connId,
        target,
        ctx.expectedSpectatorPlayerId
      );

      // A concurrent session refresh may have ended or replaced this match.
      if (this.lastWatchTargetKey === targetKey && this.machine.getContext().spectatorConnectionId === connId) {
        this.machine.dispatch({
          type: 'WATCH_TARGET_DISPATCHED',
          requestId,
          connectionId: connId,
        });
      }

      return requestId;
    } catch (err: any) {
      console.error('Failed to send WATCH_TARGET IPC:', err);
      if (this.lastWatchTargetKey !== targetKey || this.machine.getContext().spectatorConnectionId !== connId) return null;
      this.machine.dispatch({
        type: 'WATCH_RESPONSE_RECEIVED',
        response: {
          status: 'WATCH_JOIN_REJECTED',
          requestId: 'ipc-error',
          reason: err?.message || String(err),
        },
      });
      return null;
    } finally {
      this.sendingWatchTarget = false;
    }
  }

  /**
   * Retries auto-follow progression from failure state.
   */
  public async retry(): Promise<void> {
    this.lastWatchTargetKey = null;
    const res = this.machine.dispatch({ type: 'RETRY_AUTO_JOIN' });
    if (res.actionRequired === 'LAUNCH_CLIENT') {
      await this.launchSpectatorClient();
    } else if (res.actionRequired === 'SEND_WATCH_TARGET') {
      await this.sendWatchTargetCommand();
    }
  }

  /**
   * Handles user manual spectator code entry fallback.
   */
  public async manualJoin(watchCode: string, roomId?: number): Promise<void> {
    this.lastWatchTargetKey = null;
    const res = this.machine.dispatch({
      type: 'MANUAL_FALLBACK_CODE_ENTERED',
      watchCode,
      roomId,
    });

    if (res.actionRequired === 'SEND_WATCH_TARGET') {
      await this.sendWatchTargetCommand();
    }
  }

  /**
   * Resets auto-follow state machine back to IDLE.
   */
  public resetToIdle(): void {
    this.lastWatchTargetKey = null;
    this.machine.dispatch({ type: 'RESET_TO_IDLE' });
  }

  /**
   * Ingests a raw WatchTargetResponse (used by tests or direct push).
   */
  public handleWatchResponse(response: WatchTargetResponse): void {
    this.machine.dispatch({
      type: 'WATCH_RESPONSE_RECEIVED',
      response,
    });
  }

  /**
   * Ingests hand snapshot confirmation.
   */
  public handleHandSnapshot(roomId: number, epoch?: number, sequence?: number): void {
    this.machine.dispatch({
      type: 'HAND_SNAPSHOT_RECEIVED',
      roomId,
      epoch,
      sequence,
    });
  }

  public destroy(): void {
    for (const fn of this.unlistenFns) {
      try {
        fn();
      } catch {}
    }
    this.unlistenFns = [];
    this.isListening = false;
  }
}

/**
 * Singleton instance of AutoFollowService.
 */
export const autoFollowService = new AutoFollowService();
