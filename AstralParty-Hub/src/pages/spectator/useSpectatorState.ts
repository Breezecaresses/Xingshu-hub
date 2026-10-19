import { useState, useEffect, useCallback, useRef } from 'react';
import { useLocation } from 'react-router-dom';
import { listen } from '@tauri-apps/api/event';
import { api } from '../../shared/api/tauri';
import type { SpectatorHandPayload, BridgeConnectionState, GameProcessStatus } from '../../shared/types';
import {
  userOwnedSpectatorAccountService,
  type UserOwnedAccountInfo,
} from '../../services/spectatorAccountService';
import {
  defaultManagedSpectatorAccountProvider,
  type IManagedSpectatorAccountProvider,
  type ManagedAccountInfo,
} from '../../services/managedAccountProvider';
import {
  gameAccountService,
  type CurrentRuntimeIdentity,
} from '../../services/gameAccountService';
import {
  runtimeSessionResolver,
} from '../../services/runtimeSessionResolver';
import { livePlayerId } from '../../services/runtimeTruth';
import { localManagedSpectatorAccountProvider } from '../../services/localManagedAccountProvider';
import type {
  RuntimeSession,
  RuntimeResolutionResult,
} from '../../types/runtimeSession';

export {
  type SpectatorLifecycleState,
  type SpectatorRoomState,
  INITIAL_ROOM_STATE,
  type TransitionResult,
  processSpectatorTransition,
  formatSpectatorPlayerId,
} from './spectatorTransition';

import {
  type SpectatorLifecycleState,
  type SpectatorRoomState,
  INITIAL_ROOM_STATE,
  processSpectatorTransition,
} from './spectatorTransition';

export type { UserOwnedAccountInfo, ManagedAccountInfo, IManagedSpectatorAccountProvider };
export type SpectatorAccountMode = 'USER_OWNED' | 'XINGSHU_MANAGED' | null;

export {
  autoFollowService,
  type AutoFollowState,
  type AutoFollowContext,
  AUTO_FOLLOW_STATE_CONFIG_ZH,
} from '../../services/autoFollowService';
import {
  autoFollowService,
  type AutoFollowContext,
} from '../../services/autoFollowService';

const STALE_TIMEOUT_MS = 12000;

function initialManagedProvider(): IManagedSpectatorAccountProvider {
  try {
    if (localStorage.getItem('xingshu.spectator.managedHost') === 'local-validation') return localManagedSpectatorAccountProvider;
  } catch { /* Storage can be disabled. */ }
  return defaultManagedSpectatorAccountProvider;
}

export function useSpectatorState() {
  const location = useLocation();
  const [lifecycleState, setLifecycleState] = useState<SpectatorLifecycleState>('NOT_CONNECTED');
  const [roomState, setRoomState] = useState<SpectatorRoomState>(INITIAL_ROOM_STATE);
  const [bridgeStatus, setBridgeStatus] = useState<BridgeConnectionState | null>(null);
  const [gameProcess, setGameProcess] = useState<GameProcessStatus | null>(null);
  const [runtimeSessions, setRuntimeSessions] = useState<RuntimeSession[]>([]);
  const [isRefreshing, setIsRefreshing] = useState(false);

  // Auto-Follow Spectator State Machine context
  const [autoFollowContext, setAutoFollowContext] = useState<AutoFollowContext>(() =>
    autoFollowService.getContext()
  );

  useEffect(() => {
    autoFollowService.initListeners().catch(() => {});
    return autoFollowService.subscribe((ctx) => {
      setAutoFollowContext({ ...ctx });
    });
  }, []);

  // Managed Account Provider state and active provider reference
  const [managedProvider, setManagedProviderState] = useState<IManagedSpectatorAccountProvider>(
    initialManagedProvider
  );
  const managedProviderRef = useRef<IManagedSpectatorAccountProvider>(managedProvider);

  // Managed Account Info state
  const [managedAccountInfo, setManagedAccountInfo] = useState<ManagedAccountInfo>(() => ({
    status: 'SERVICE_NOT_CONFIGURED',
    serverEndpoint: null,
    sessionId: null,
    allocatedPlayerId: null,
    allocatedAt: null,
    expiresAt: null,
    message: 'Official watcher pool service is unconfigured.',
  }));
  const managedInfoRef = useRef(managedAccountInfo);

  // User-Owned Spectator Account info snapshot
  const [userAccountInfo, setUserAccountInfo] = useState<UserOwnedAccountInfo>(() =>
    userOwnedSpectatorAccountService.getAccountInfo()
  );

  // Setter allowing injection of custom managed account providers (for tests or remote clusters)
  const setManagedProvider = useCallback((provider: IManagedSpectatorAccountProvider) => {
    managedProviderRef.current = provider;
    setManagedProviderState(provider);
    provider
      .getStatus()
      .then((info) => {
        if (managedProviderRef.current !== provider) return;
        managedInfoRef.current = info;
        setManagedAccountInfo(info);
      })
      .catch((err) => {
        if (managedProviderRef.current !== provider) return;
        setManagedAccountInfo({
          status: 'ERROR',
          serverEndpoint: null,
          sessionId: null,
          allocatedPlayerId: null,
          allocatedAt: null,
          expiresAt: null,
          message: err instanceof Error ? err.message : String(err),
        });
      });
  }, []);

  // Account Mode state: 'USER_OWNED' | 'XINGSHU_MANAGED' | null
  // Persisted at application level across route navigation (/spectator -> /runtime -> /spectator)
  const [accountMode, setAccountModeState] = useState<SpectatorAccountMode>(() => {
    if (typeof window !== 'undefined') {
      const search = window.location.search || '';
      if (search.includes('mode=entry')) return null;
      if (search.includes('mode=managed') || search.includes('mode=XINGSHU_MANAGED')) return 'XINGSHU_MANAGED';
      if (search.includes('mode=ready') || search.includes('mode=user') || search.includes('mode=USER_OWNED')) return 'USER_OWNED';

      // Restore UI workflow selection from sessionStorage (or localStorage fallback)
      try {
        const sessionMode = sessionStorage.getItem('xingshu_spectator_selected_mode');
        if (sessionMode === 'USER_OWNED' || sessionMode === 'XINGSHU_MANAGED') {
          return sessionMode as SpectatorAccountMode;
        }
        const localMode = localStorage.getItem('xingshu.spectator.accountMode');
        if (localMode === 'USER_OWNED' || localMode === 'XINGSHU_MANAGED') {
          return localMode as SpectatorAccountMode;
        }
      } catch {
        // Fallback in restricted storage contexts
      }
    }
    return null;
  });
  const accountModeRef = useRef(accountMode);
  accountModeRef.current = accountMode;

  const setAccountMode = useCallback((mode: SpectatorAccountMode) => {
    accountModeRef.current = mode;
    setAccountModeState(mode);
    if (typeof window !== 'undefined') {
      try {
        if (mode) {
          sessionStorage.setItem('xingshu_spectator_selected_mode', mode);
          localStorage.setItem('xingshu.spectator.accountMode', mode);
        } else {
          sessionStorage.removeItem('xingshu_spectator_selected_mode');
          localStorage.removeItem('xingshu.spectator.accountMode');
        }
      } catch {
        // Fallback in restricted environments
      }
    }
  }, []);

  useEffect(() => {
    const search = location.search || (typeof window !== 'undefined' ? window.location.search : '');
    if (search.includes('mode=entry')) {
      setAccountMode(null);
    } else if (search.includes('mode=managed') || search.includes('mode=XINGSHU_MANAGED')) {
      setAccountMode('XINGSHU_MANAGED');
    } else if (search.includes('mode=ready') || search.includes('mode=user') || search.includes('mode=USER_OWNED')) {
      setAccountMode('USER_OWNED');
    }
  }, [location.search, setAccountMode]);

  useEffect(() => {
    if (accountMode !== 'XINGSHU_MANAGED') {
      autoFollowService.setExecutionMode('USER_OWNED');
      localManagedSpectatorAccountProvider.releaseCurrent().catch(console.warn);
    } else if (managedProvider.executionMode === 'LOCAL_VALIDATION') {
      autoFollowService.setExecutionMode('LOCAL_MANAGED', async target => {
        const info = await managedProvider.allocateSession(target.internalRoomId ?? target.roomId,
          target.watchCode || target.spectatorCode || undefined);
        if (!info.requestId) throw new Error('托管 Worker 未返回真实观战请求编号');
        if (managedProviderRef.current === managedProvider && accountModeRef.current === 'XINGSHU_MANAGED') {
          managedInfoRef.current = info;
          setManagedAccountInfo(info);
        }
        return info.requestId;
      });
    } else {
      // Remote allocation alone cannot impersonate a verified local Runtime/data stream.
      autoFollowService.setExecutionMode('PAUSED');
    }
  }, [accountMode, managedProvider]);

  const startLocalManagedValidation = useCallback(() => {
    try { localStorage.setItem('xingshu.spectator.managedHost', 'local-validation'); } catch { /* optional */ }
    setManagedProvider(localManagedSpectatorAccountProvider);
    setAccountMode('XINGSHU_MANAGED');
    autoFollowService.resetToIdle();
  }, [setManagedProvider, setAccountMode]);

  const retryManagedFollow = useCallback(async () => {
    if (accountModeRef.current === 'XINGSHU_MANAGED') await localManagedSpectatorAccountProvider.releaseCurrent();
    await autoFollowService.retry();
  }, []);


  // Mutable refs for synchronous freshness and monotonicity checks
  const roomStateRef = useRef<SpectatorRoomState>(INITIAL_ROOM_STATE);
  const lastEventTimeRef = useRef<number>(Date.now());
  const lifecycleRef = useRef<SpectatorLifecycleState>('NOT_CONNECTED');
  const spectatorSessionRef = useRef<RuntimeSession | null>(null);
  const retiredEpochRef = useRef(0);

  const updateLifecycle = useCallback((newState: SpectatorLifecycleState) => {
    lifecycleRef.current = newState;
    setLifecycleState(newState);
  }, []);

  // Changing the source starts a fresh display; old hands cannot stand in for a new lease.
  useEffect(() => {
    roomStateRef.current = INITIAL_ROOM_STATE;
    setRoomState(INITIAL_ROOM_STATE);
    updateLifecycle('NOT_CONNECTED');
  }, [accountMode, managedProvider, updateLifecycle]);

  /**
   * Evaluates incoming SpectatorHandPayload with strict Epoch & Sequence freshness gating.
   */
  const handleIncomingPayload = useCallback((payload: SpectatorHandPayload) => {
    if (payload.spectatorSessionEpoch <= retiredEpochRef.current) return;
    if (accountModeRef.current === 'XINGSHU_MANAGED') {
      const lease = managedInfoRef.current;
      if (managedProviderRef.current.executionMode !== 'LOCAL_VALIDATION' || lease.status !== 'IN_USE'
          || !lease.sessionId || lease.currentRoomId !== payload.roomId
          || lease.connectionId !== payload.connectionId) return;
    }
    const session = spectatorSessionRef.current;
    const targetRoomId = autoFollowService.getContext().activeTarget?.internalRoomId;
    if (!session || livePlayerId(session) === null ||
        payload.connectionId !== session.connectionId || payload.platformId !== session.platformId ||
        payload.spectatorAccountPlayerId !== livePlayerId(session) ||
        (targetRoomId && payload.roomId !== targetRoomId)) return;
    const current = roomStateRef.current;
    const now = Date.now();
    lastEventTimeRef.current = now;

    const result = processSpectatorTransition(current, payload, now);
    if (!result.accepted) {
      console.warn(`[SpectatorUX] ${result.reason}`);
      return;
    }

    if (result.isNewEpochOrRoom) {
      console.info(
        `[SpectatorUX] Session Epoch or Room changed: room ${current.roomId} -> ${payload.roomId}, epoch ${current.epoch} -> ${payload.spectatorSessionEpoch}`
      );
    }

    roomStateRef.current = result.nextState;
    setRoomState(result.nextState);
    updateLifecycle(result.nextLifecycle);

    // Auto-advance account mode to USER_OWNED when live room data is active
    if (result.nextState.roomId !== null && accountModeRef.current === null) {
      accountModeRef.current = 'USER_OWNED';
      setAccountModeState('USER_OWNED');
    }

    // Synchronize user account service with live payload
    const updatedUser = userOwnedSpectatorAccountService.syncAccountInfo({
      gameRunning: true,
      runtimeConnected: true,
      spectatorAccountPlayerId: payload.spectatorAccountPlayerId,
      roomId: payload.roomId,
    });
    setUserAccountInfo(updatedUser);

    // Notify auto-follow state machine of confirmed live spectator snapshot
    autoFollowService.handleHandSnapshot(
      payload.roomId,
      payload.spectatorSessionEpoch,
      payload.sequenceNumber
    );
  }, [updateLifecycle]);

  /**
   * Polls Bridge, Spectator status, and Managed Account Provider status
   */
  const pollStatus = useCallback(async () => {
    try {
      const provider = managedProviderRef.current;
      const [payload, bStatus, mInfo, procStatus, sessions] = await Promise.all([
        api.getSpectatorHandPayload().catch(() => null),
        api.getBridgeStatus().catch(() => null),
        provider.getStatus().catch((err) => ({
          status: 'ERROR' as const,
          serverEndpoint: null,
          sessionId: null,
          allocatedPlayerId: null,
          allocatedAt: null,
          expiresAt: null,
          message: err instanceof Error ? err.message : String(err),
        })),
        api.checkGameProcessStatus('STEAM_CN').catch(() => null),
        api.getRuntimeSessions().catch(() => []),
      ]);

      const activeSessions = (sessions || []).filter((s: RuntimeSession) => s.status !== 'DISCONNECTED');
      setRuntimeSessions(activeSessions);
      runtimeSessionResolver.setBindings(gameAccountService.getBindings());
      const spectator = runtimeSessionResolver.resolve(activeSessions, null).spectatorSession;
      spectatorSessionRef.current = spectator;
      autoFollowService.updateRuntimeSessions(activeSessions);

      if (procStatus) {
        setGameProcess(procStatus);
      }

      if (mInfo && managedProviderRef.current === provider) {
        managedInfoRef.current = mInfo;
        setManagedAccountInfo(mInfo);
        if (accountModeRef.current === 'XINGSHU_MANAGED' && mInfo.status !== 'IN_USE'
            && roomStateRef.current.roomId !== null) {
          roomStateRef.current = INITIAL_ROOM_STATE;
          setRoomState(INITIAL_ROOM_STATE);
          updateLifecycle('CONNECTED_NOT_SPECTATING');
        }
      }

      setBridgeStatus(bStatus);

      const isConnected = !!spectator && livePlayerId(spectator) !== null;
      const isGameRunning = !!spectator?.processId;
      const curRoomId = roomStateRef.current.roomId;
      const curSpectatorPlayerId = livePlayerId(spectator);

      // Synchronize User-Owned Spectator Account Service
      const updatedUser = userOwnedSpectatorAccountService.syncAccountInfo({
        platformId: spectator?.platformId ?? gameAccountService.getBindings().find(b => b.role === 'USER_SPECTATOR')?.platformId,
        gameRunning: isGameRunning,
        runtimeConnected: !!spectator,
        spectatorAccountPlayerId: curSpectatorPlayerId,
        roomId: curRoomId,
      });
      setUserAccountInfo(updatedUser);


      if (!isConnected) {
        if (lifecycleRef.current !== 'NOT_CONNECTED' && lifecycleRef.current !== 'DISCONNECTED') {
          updateLifecycle('DISCONNECTED');
        }
        return;
      }

      // If connected via TCP
      if (payload) {
        handleIncomingPayload(payload);
      } else {
        if (lifecycleRef.current === 'NOT_CONNECTED' || lifecycleRef.current === 'DISCONNECTED') {
          updateLifecycle('CONNECTED_NOT_SPECTATING');
        }
      }
    } catch (err) {
      console.error('[SpectatorUX] Poll error', err);
      updateLifecycle('DISCONNECTED');
    }
  }, [handleIncomingPayload, updateLifecycle]);

  /**
   * Manual refresh handler
   */
  const manualRefresh = useCallback(async () => {
    setIsRefreshing(true);
    await pollStatus();
    setIsRefreshing(false);
  }, [pollStatus]);

  // Initial poll & periodic status sync
  useEffect(() => {
    pollStatus();
    const interval = setInterval(pollStatus, 1500);
    return () => clearInterval(interval);
  }, [pollStatus]);

  // Provider status fetch on mount and on provider change
  useEffect(() => {
    let cancelled = false;
    managedProvider
      .getStatus()
      .then((info) => {
        if (!cancelled) {
          managedInfoRef.current = info;
          setManagedAccountInfo(info);
        }
      })
      .catch((err) => {
        if (!cancelled) {
          setManagedAccountInfo({
            status: 'ERROR',
            serverEndpoint: null,
            sessionId: null,
            allocatedPlayerId: null,
            allocatedAt: null,
            expiresAt: null,
            message: err instanceof Error ? err.message : String(err),
          });
        }
      });
    return () => {
      cancelled = true;
    };
  }, [managedProvider]);

  // Real-time Event Listener for spectator://hand-snapshot
  useEffect(() => {
    let disposed = false;
    let unlisten: (() => void) | undefined;
    listen<{ roomId: number }>('spectator://session-ended', ({ payload }) => {
      if (roomStateRef.current.roomId !== payload.roomId) return;
      retiredEpochRef.current = Math.max(retiredEpochRef.current, roomStateRef.current.epoch);
      roomStateRef.current = INITIAL_ROOM_STATE;
      setRoomState(INITIAL_ROOM_STATE);
      updateLifecycle('CONNECTED_NOT_SPECTATING');
    }).then(fn => { if (disposed) fn(); else unlisten = fn; });
    return () => { disposed = true; unlisten?.(); };
  }, [updateLifecycle]);

  useEffect(() => {
    const room = autoFollowContext.activeTarget?.internalRoomId ?? autoFollowContext.activeTarget?.roomId;
    if (roomStateRef.current.roomId !== null &&
        ((room && room !== roomStateRef.current.roomId) || autoFollowContext.currentState === 'MAIN_MATCH_ENDED')) {
      retiredEpochRef.current = Math.max(retiredEpochRef.current, roomStateRef.current.epoch);
      roomStateRef.current = INITIAL_ROOM_STATE;
      setRoomState(INITIAL_ROOM_STATE);
      updateLifecycle('CONNECTED_NOT_SPECTATING');
    }
  }, [autoFollowContext, updateLifecycle]);

  useEffect(() => {
    let unlisten: (() => void) | undefined;
    listen<SpectatorHandPayload>('spectator://hand-snapshot', (event) => {
      if (event.payload) {
        handleIncomingPayload(event.payload);
      }
    })
      .then((fn) => {
        unlisten = fn;
      })
      .catch((err) => {
        console.warn('[SpectatorUX] Failed to register listener', err);
      });

    return () => {
      if (unlisten) unlisten();
    };
  }, [handleIncomingPayload]);

  // Real-time Event Listener for runtime://session-changed and runtime://session-disconnected
  useEffect(() => {
    let unlistenChanged: (() => void) | undefined;
    let unlistenDisconnected: (() => void) | undefined;

    const handleSessionUpdate = async () => {
      try {
        const sessions = await api.getRuntimeSessions().catch(() => []);
        const activeSessions = (sessions || []).filter((s: RuntimeSession) => s.status !== 'DISCONNECTED');
        setRuntimeSessions(activeSessions);
        autoFollowService.updateRuntimeSessions(activeSessions);
        pollStatus().catch(() => {});
      } catch (err) {
        console.warn('[SpectatorUX] Failed to update sessions on runtime event', err);
      }
    };

    listen('runtime://session-changed', () => {
      handleSessionUpdate();
    })
      .then((fn) => {
        unlistenChanged = fn;
      })
      .catch((err) => {
        console.warn('[SpectatorUX] Failed to listen to runtime://session-changed', err);
      });

    listen('runtime://session-disconnected', () => {
      handleSessionUpdate();
    })
      .then((fn) => {
        unlistenDisconnected = fn;
      })
      .catch((err) => {
        console.warn('[SpectatorUX] Failed to listen to runtime://session-disconnected', err);
      });

    return () => {
      if (unlistenChanged) unlistenChanged();
      if (unlistenDisconnected) unlistenDisconnected();
    };
  }, [pollStatus]);

  // Stale Idle Heartbeat Monitor
  useEffect(() => {
    const idleCheck = setInterval(() => {
      if (lifecycleRef.current === 'STREAMING') {
        const elapsed = Date.now() - lastEventTimeRef.current;
        if (elapsed > STALE_TIMEOUT_MS) {
          updateLifecycle('SNAPSHOT_STALE_IDLE');
        }
      }
    }, 2000);

    return () => clearInterval(idleCheck);
  }, [updateLifecycle]);

  runtimeSessionResolver.setBindings(gameAccountService.getBindings());
  const resolutionResult: RuntimeResolutionResult = runtimeSessionResolver.resolve(runtimeSessions, accountMode);
  const effectiveMainSession = resolutionResult.mainSession;
  const effectiveSpectatorSession = resolutionResult.spectatorSession;
  const isConnected = !!effectiveSpectatorSession && livePlayerId(effectiveSpectatorSession) !== null;

  const isGameRunning = !!effectiveSpectatorSession?.processId;
  const gamePid = effectiveSpectatorSession?.processId;
  const rawSpectatorPlayerId = livePlayerId(effectiveSpectatorSession);

  // Subscribe to GameAccountService updates
  const [, setIdentityVersion] = useState(0);
  useEffect(() => {
    return gameAccountService.subscribe(() => {
      setIdentityVersion((v) => v + 1);
    });
  }, []);

  const launchGame = useCallback(async () => {
    try {
      const res = await userOwnedSpectatorAccountService.launchGame();
      setTimeout(() => {
        pollStatus();
      }, 500);
      return res;
    } catch (e) {
      console.warn('Launch game requested:', e);
      return { uri: userOwnedSpectatorAccountService.getLaunchUri(), launched: false };
    }
  }, [pollStatus]);

  // Derive authoritative runtime identity from live session
  const effectiveLivePid = isConnected ? rawSpectatorPlayerId : null;

  const currentIdentity: CurrentRuntimeIdentity = gameAccountService.resolveSession(
    effectiveLivePid,
    effectiveSpectatorSession?.platformId || 'UNKNOWN'
  );

  // ---------------------------------------------------------------------------
  // Project XingShu HS4-P1.5: Multi-Client Runtime Session Resolution
  // ---------------------------------------------------------------------------
  const activeRuntimeSessions = runtimeSessions;


  const launchSpectatorClient = useCallback(async () => {
    try {
      const binding = gameAccountService.getBindings().find(b => b.role === 'USER_SPECTATOR');
      if (!binding) throw new Error('请先在账号中心绑定观战客户端平台');
      return await api.launchGameUri(undefined, binding.platformId);
    } catch (e) {
      console.warn('Launch TapTap spectator client requested:', e);
    }
  }, []);

  // In HS4-P1.5 dual-routing architecture:
  // Steam CN is the legitimate MAIN client and does NOT cause a mismatch when running parallel to spectator!
  // Prompt directs user to launch TapTap if spectator is missing, strictly prohibiting logout instructions.
  const isRoleMismatch =
    accountMode === 'USER_OWNED' &&
    currentIdentity.resolvedRole === 'MAIN' &&
    activeRuntimeSessions.length === 1 &&
    false; // Suppressed: dual-client architecture does not require logout

  const roleMismatchWarning = resolutionResult.spectatorPrompt;

  const liveStreaming = !!isConnected && (lifecycleState === 'STREAMING' || lifecycleState === 'SNAPSHOT_STALE_IDLE');
  const isSnapshotStale = !isConnected && roomState.roomId !== null;

  return {
    lifecycleState,
    liveStreaming,
    isSnapshotStale,
    roomState,
    bridgeStatus,
    gameProcessStatus: gameProcess,
    isConnected: !!isConnected,
    isGameRunning,
    gamePid,
    spectatorPlayerId: effectiveLivePid,
    accountMode,
    setAccountMode,
    userAccountInfo,
    managedAccountInfo,
    startLocalManagedValidation,
    setManagedProvider,
    launchGame,

    // Project XingShu HS4-P1.4 & HS4-P1.5 Identity & Routing Truth
    currentIdentity,
    isRoleMismatch,
    roleMismatchWarning,

    activeRuntimeSessions,
    resolvedSessions: resolutionResult.allSessions,
    selectedSpectatorSession: resolutionResult.selectedSpectatorSession,
    selectedSpectatorConnectionId: resolutionResult.selectedSpectatorConnectionId,
    mainSession: effectiveMainSession,
    spectatorSession: effectiveSpectatorSession,
    spectatorPrompt: resolutionResult.spectatorPrompt,
    launchSpectatorClient,

    // Auto-Follow Spectator State Machine (HS4-P1.6)
    autoFollowState: autoFollowContext.currentState,
    autoFollowContext,
    retryAutoFollow: retryManagedFollow,
    manualJoinAutoFollow: (code: string, roomId?: number) => autoFollowService.manualJoin(code, roomId),
    resetAutoFollow: () => autoFollowService.resetToIdle(),
    triggerAutoFollowWatch: (target: any) => autoFollowService.onMainMatchDetected(target),

    isRefreshing,
    manualRefresh,
  };
}
