/**
 * Project XingShu — Runtime Session Resolver Service
 * Phase: HS4-P1.5
 * Lane 4: Account-to-Runtime Routing Specialist
 *
 * Implements deterministic routing resolution between active runtime sessions and
 * user game account bindings.
 *
 * CANONICAL BINDING ARCHITECTURE:
 * - MAIN: Configured via user explicit binding (e.g. Platform 'STEAM_CN')
 * - USER_SPECTATOR: Configured via user explicit binding (e.g. Platform 'TAPTAP')
 *
 * RESOLUTION PRIORITY RULES:
 * 1. Exact Match:
 *    session.playerId === binding.playerId && session.platformId === binding.platformId
 *    -> Assigned role (MAIN or USER_SPECTATOR).
 * 2. Platform Heuristic (when session.playerId is pending/null):
 *    If session.platformId === binding.platformId:
 *      -> Assigned tentative role (PENDING_SPECTATOR for TapTap, PENDING_MAIN for Steam CN).
 * 3. Exact Promotion:
 *    Once live PlayerId arrives, immediately promoted to exact role or marked UNBOUND.
 * 4. Unmatched:
 *    Marked UNBOUND (or UNBOUND_ACCOUNT).
 *
 * SPECTATOR EXECUTION ROUTING INVARIANTS:
 * - In "我的观战账号" (USER_SPECTATOR) mode, the resolver MUST select the TapTap Runtime,
 *   NEVER the Steam MAIN Runtime!
 * - Never resolve routing based solely on "last connected Runtime".
 * - If only Steam CN is connected, prompt user:
 *   "专用观战账号尚未启动。请启动 TapTap 客户端并登录观战账号"
 *   STRICTLY PROHIBIT telling the user to log out of their Steam MAIN account.
 */

import {
  type RuntimeSession,
  type ResolvedRuntimeRole,
  type ResolvedRuntimeSession,
  type RuntimeResolutionResult,
  RUNTIME_ROLE_LABELS_ZH,
  RUNTIME_ROLE_LABELS_EN,
} from '../types/runtimeSession.ts';

import {
  type GameAccountBinding,
  GameAccountRole,
} from '../types/gameAccount.ts';

import { gameAccountService } from './gameAccountService.ts';
import { livePlayerId } from './runtimeTruth.ts';

export class RuntimeSessionResolver {
  private bindings: GameAccountBinding[];

  constructor(customBindings?: GameAccountBinding[]) {
    if (customBindings) {
      this.bindings = [...customBindings];
    } else {
      let serviceBindings: GameAccountBinding[] = [];
      try {
        if (typeof gameAccountService !== 'undefined' && typeof gameAccountService.getBindings === 'function') {
          serviceBindings = gameAccountService.getBindings();
        }
      } catch {
        serviceBindings = [];
      }
      this.bindings = [...serviceBindings];
    }
  }

  /**
   * Replaces current bindings in memory.
   */
  public setBindings(bindings: GameAccountBinding[]): void {
    this.bindings = [...bindings];
  }

  /**
   * Retrieves active bindings in memory.
   */
  public getBindings(): GameAccountBinding[] {
    return [...this.bindings];
  }

  /**
   * Resolves a single RuntimeSession against configured bindings according to the 4 priority rules.
   */
  public resolveSession(session: RuntimeSession): ResolvedRuntimeSession {
    const isDisconnected = session.status === 'DISCONNECTED' || session.connectionState === 'DISCONNECTED';
    if (isDisconnected) {
      return {
        connectionId: session.connectionId,
        platformId: session.platformId,
        processId: session.processId ?? session.pid ?? session.gamePid ?? 0,
        playerId: null,
        displayName: session.displayName ?? null,
        role: 'DISCONNECTED',
        resolvedRole: 'DISCONNECTED',
        matchedBinding: null,
        isSpectatorExecutionRuntime: false,
        status: 'DISCONNECTED',
        roleLabelZh: RUNTIME_ROLE_LABELS_ZH.DISCONNECTED,
        roleLabelEn: RUNTIME_ROLE_LABELS_EN.DISCONNECTED,
        session: { ...session },
      };
    }

    const effectivePlayerId = livePlayerId(session);
    const effectiveProcessId = session.processId ?? session.pid ?? session.gamePid ?? 0;
    const hasValidPlayerId = typeof effectivePlayerId === 'number' && effectivePlayerId > 0;

    let assignedRole: ResolvedRuntimeRole = 'UNBOUND';
    let matchedBinding: GameAccountBinding | null = null;

    if (hasValidPlayerId) {
      // Rule 1: Exact Match: session.playerId === binding.playerId && session.platformId === binding.platformId
      const exactMatch = this.bindings.find(
        (b) => b.playerId === effectivePlayerId && b.platformId === session.platformId
      );

      if (exactMatch) {
        assignedRole = exactMatch.role === GameAccountRole.MAIN ? 'MAIN' : 'USER_SPECTATOR';
        matchedBinding = exactMatch;
      } else if (!this.bindings.some((b) => b.platformId === session.platformId) &&
        effectivePlayerId === session.expectedBoundPlayerId &&
        (session.boundRole === 'MAIN' || session.boundRole === 'USER_SPECTATOR')) {
        assignedRole = session.boundRole as 'MAIN' | 'USER_SPECTATOR';
      } else {
        // Rule 3 / Rule 4: PlayerId packet has arrived, but does not match any binding for this platform
        assignedRole = 'UNBOUND';
        matchedBinding = null;
      }
    } else {
      // Rule 2: Platform Heuristic (when session.playerId is pending or null)
      // If session.platformId === binding.platformId, tentatively match role as PENDING_ROLE
      const platformMatch = this.bindings.find((b) => b.platformId === session.platformId);

      if (platformMatch) {
        if (platformMatch.role === GameAccountRole.USER_SPECTATOR) {
          assignedRole = 'PENDING_SPECTATOR';
        } else if (platformMatch.role === GameAccountRole.MAIN) {
          assignedRole = 'PENDING_MAIN';
        } else {
          assignedRole = 'PENDING_ROLE';
        }
        matchedBinding = platformMatch;
      } else if (session.expectedBoundPlayerId && session.platformId === 'STEAM_CN') {
        assignedRole = 'PENDING_MAIN';
      } else if (session.expectedBoundPlayerId && session.platformId === 'TAPTAP') {
        assignedRole = 'PENDING_SPECTATOR';
      } else {
        // Rule 4: Unmatched platform
        assignedRole = 'UNBOUND';
        matchedBinding = null;
      }
    }

    const roleLabelZh = RUNTIME_ROLE_LABELS_ZH[assignedRole] || '未绑定账号';
    const roleLabelEn = RUNTIME_ROLE_LABELS_EN[assignedRole] || 'Unbound Account';

    return {
      connectionId: session.connectionId,
      platformId: session.platformId,
      processId: effectiveProcessId,
      playerId: effectivePlayerId,
      displayName: session.displayName ?? matchedBinding?.displayName ?? null,
      role: assignedRole,
      resolvedRole: assignedRole,
      matchedBinding: matchedBinding ? { ...matchedBinding } : null,
      isSpectatorExecutionRuntime: false,
      status: 'CONNECTED',
      roleLabelZh,
      roleLabelEn,
      session: {
        ...session,
        processId: effectiveProcessId,
        playerId: effectivePlayerId,
      },
    };
  }

  /**
   * Resolves multiple active runtime sessions and determines the active Spectator Execution Runtime.
   *
   * STRICT SELECTION RULES:
   * 1. In USER_SPECTATOR (or USER_OWNED) mode:
   *    - The resolver MUST select the TapTap Runtime (platformId === 'TAPTAP'), NOT the Steam MAIN Runtime!
   *    - Resolution order NEVER depends on connection arrival order (anti-last-connected bias).
   * 2. If only Steam CN (MAIN) is connected:
   *    - Returns prompt: "专用观战账号尚未启动。请启动 TapTap 客户端并登录观战账号"
   *    - STRICTLY PROHIBITS telling the user to log out of their Steam MAIN account!
   */
  public resolve(
    sessions: RuntimeSession[],
    accountMode: string | null = 'USER_SPECTATOR'
  ): RuntimeResolutionResult {
    const isUserSpectatorMode =
      accountMode === 'USER_SPECTATOR' || accountMode === 'USER_OWNED';

    // 1. Resolve all sessions individually
    const resolvedList: ResolvedRuntimeSession[] = sessions.map((s) => this.resolveSession(s));

    // 2. Identify authoritative MAIN session
    const mainBinding = this.bindings.find((b) => b.role === GameAccountRole.MAIN);
    const mainSession = mainBinding || sessions.some((s) => s.platformId === 'STEAM_CN' && s.expectedBoundPlayerId)
      ? (resolvedList.find((s) => s.role === 'MAIN' && s.status === 'CONNECTED') ||
         resolvedList.find((s) => s.role === 'PENDING_MAIN' && s.status === 'CONNECTED') ||
         null)
      : null;

    // 3. Identify authoritative USER_SPECTATOR session
    // Priority: Exact match on USER_SPECTATOR (TapTap) > Pending match on PENDING_SPECTATOR (TapTap)
    // STRICT: Never pick Steam CN as the spectator session!
    const spectatorBinding = this.bindings.find((b) => b.role === GameAccountRole.USER_SPECTATOR);
    const spectatorSession = spectatorBinding || sessions.some((s) => s.platformId === 'TAPTAP' && s.expectedBoundPlayerId)
      ? (resolvedList.find(
          (s) => s.role === 'USER_SPECTATOR' && (!spectatorBinding || s.platformId === spectatorBinding.platformId) && s.status === 'CONNECTED'
        ) ||
        resolvedList.find(
          (s) => s.role === 'PENDING_SPECTATOR' && (!spectatorBinding || s.platformId === spectatorBinding.platformId) && s.status === 'CONNECTED'
        ) ||
        null)
      : null;

    // 4. Spectator Execution Runtime Selection
    let selectedSpectatorSession: ResolvedRuntimeSession | null = null;
    let selectedSpectatorConnectionId: string | null = null;

    if (isUserSpectatorMode) {
      if (spectatorSession) {
        selectedSpectatorSession = spectatorSession;
        selectedSpectatorConnectionId = spectatorSession.connectionId;
        // Mark the selected session
        spectatorSession.isSpectatorExecutionRuntime = true;
      }
    }

    // 5. Evaluate Prompts & Warnings
    let spectatorPrompt: string | null = null;
    let hasRoleMismatch = false;

    if (isUserSpectatorMode) {
      if (!spectatorBinding && !spectatorSession) {
        spectatorPrompt = '专用观战账号未配置。请在账号中心绑定观战账号和平台';
      } else if (!spectatorSession) {
        // Dedicated TapTap spectator runtime is offline/unstarted
        const platformNames: Record<string, string> = { TAPTAP: 'TapTap', STEAM_CN: 'Steam CN', STEAM_GLOBAL: 'Steam Global', BILIBILI: 'Bilibili', WEGAME: 'WeGame' };
        spectatorPrompt = `专用观战账号尚未启动。请启动 ${platformNames[spectatorBinding?.platformId ?? ''] ?? '已绑定平台'} 客户端并登录绑定账号 ${spectatorBinding?.playerId ?? ''}`;
      } else if (spectatorSession.role === 'PENDING_SPECTATOR') {
        spectatorPrompt = '观战客户端已连接，等待登录账号识别';
      }
    }

    const isSpectatorReady = selectedSpectatorSession?.role === 'USER_SPECTATOR' && selectedSpectatorSession.status === 'CONNECTED';

    return {
      allSessions: resolvedList,
      mainSession,
      spectatorSession,
      selectedSpectatorSession,
      selectedSpectatorConnectionId,
      spectatorPrompt,
      hasRoleMismatch,
      isSpectatorReady,
    };
  }

  /**
   * Helper to directly select the execution session for spectator streaming.
   */
  public selectSpectatorExecutionSession(
    sessions: RuntimeSession[],
    accountMode: string | null = 'USER_SPECTATOR'
  ): ResolvedRuntimeSession | null {
    const result = this.resolve(sessions, accountMode);
    return result.selectedSpectatorSession;
  }

  /**
   * Helper to retrieve prompt message for current runtime state.
   */
  public getSpectatorPrompt(
    sessions: RuntimeSession[],
    accountMode: string | null = 'USER_SPECTATOR'
  ): string | null {
    const result = this.resolve(sessions, accountMode);
    return result.spectatorPrompt;
  }
}

/**
 * Singleton instance of RuntimeSessionResolver.
 */
export const runtimeSessionResolver = new RuntimeSessionResolver();
