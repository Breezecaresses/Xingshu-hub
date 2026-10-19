/**
 * Project XingShu — Platform Launch & Authentication Strategy Service
 * Phase: HS4-P1.4
 *
 * Defines explicit launch strategies and zero-credential authentication flows
 * across all 5 supported platforms: STEAM_CN, STEAM_GLOBAL, TAPTAP, BILIBILI, WEGAME.
 *
 * STRICT SAFETY & SECURITY RULES:
 * 1. STRICT NO-FALLBACK: Never fallback to Steam if platform has no steamAppId!
 *    TapTap, Bilibili, and WeGame MUST fail closed with LAUNCH_NOT_IMPLEMENTED.
 * 2. If steamAppId == null, MUST NEVER execute or return steam://run/2622000!
 * 3. ZERO CREDENTIAL COLLECTION: All user-owned accounts follow OFFICIAL_CLIENT_LOGIN.
 *    Hub never asks for, captures, or persists passwords, tokens, or auth cookies.
 * 4. XingShu Managed Accounts remain SERVICE_NOT_CONFIGURED in V1.
 */

import {
  PlatformId,
  getPlatformProfile,
} from '../types/platform.ts';

import {
  SpectatorAccountStatus,
  OFFICIAL_WATCHER_UNCONFIGURED_MESSAGE,
} from '../types/spectatorAccount.ts';

/**
 * List of the 5 canonical supported platforms in Project XingShu.
 */
export const SUPPORTED_PLATFORMS = [
  'STEAM_CN',
  'STEAM_GLOBAL',
  'TAPTAP',
  'BILIBILI',
  'WEGAME',
] as const;

export type SupportedPlatformId = (typeof SUPPORTED_PLATFORMS)[number];

/**
 * Platform display names for user-facing UI messages.
 */
export const PLATFORM_DISPLAY_NAMES: Record<string, string> = {
  STEAM_CN: 'Steam 国服',
  STEAM_GLOBAL: 'Steam 国际服',
  TAPTAP: 'TapTap 客户端',
  BILIBILI: 'Bilibili 客户端',
  WEGAME: 'WeGame 客户端',
};

/**
 * Canonical Astral Party Steam Application ID.
 */
export const CANONICAL_ASTRAL_PARTY_STEAM_APP_ID = 2622000;

/**
 * Launch strategy execution method.
 */
export type PlatformLaunchMethod =
  | 'LAUNCH_METHOD_STEAM_URI'
  | 'LAUNCH_NOT_IMPLEMENTED'
  | 'LAUNCH_UNSUPPORTED';

export const PlatformLaunchMethod = {
  LAUNCH_METHOD_STEAM_URI: 'LAUNCH_METHOD_STEAM_URI' as PlatformLaunchMethod,
  LAUNCH_NOT_IMPLEMENTED: 'LAUNCH_NOT_IMPLEMENTED' as PlatformLaunchMethod,
  LAUNCH_UNSUPPORTED: 'LAUNCH_UNSUPPORTED' as PlatformLaunchMethod,
} as const;

/**
 * Result of evaluating the launch strategy for a platform.
 */
export interface PlatformLaunchPlan {
  platformId: string;
  platformName: string;
  isSupported: boolean;
  canLaunch: boolean;
  launchMethod: PlatformLaunchMethod;
  action?: 'MANUAL_LAUNCH_REQUIRED' | 'LAUNCH_OFFICIAL_STEAM';
  uri: string | null;
  steamAppId: number | null;
  status: 'READY' | 'LAUNCH_NOT_IMPLEMENTED' | 'LAUNCH_UNSUPPORTED';
  reason: string | null;
  userMessage: string;
}

/**
 * Evaluates the launch strategy for a given platform identifier.
 * STRICT: Enforces fail-closed behavior for platforms without a valid steamAppId.
 */
export function resolvePlatformLaunchStrategy(platformId: string = 'STEAM_CN'): PlatformLaunchPlan {
  const normId = (platformId || '').trim();
  const platformName = PLATFORM_DISPLAY_NAMES[normId] || normId || '未知平台';

  // 1. Unknown / Unsupported platform
  if (!normId || !(SUPPORTED_PLATFORMS as readonly string[]).includes(normId)) {
    return {
      platformId: normId,
      platformName,
      isSupported: false,
      canLaunch: false,
      launchMethod: PlatformLaunchMethod.LAUNCH_UNSUPPORTED,
      action: 'MANUAL_LAUNCH_REQUIRED',
      uri: null,
      steamAppId: null,
      status: 'LAUNCH_UNSUPPORTED',
      reason: 'Unsupported platform',
      userMessage: `不支持的发行平台标识: "${normId}"`,
    };
  }

  // 2. Query canonical platform profile
  const profile = getPlatformProfile(normId as PlatformId);
  const steamAppId = profile?.steamAppId ?? null;

  // 3. Steam platforms (STEAM_CN, STEAM_GLOBAL) with verified steamAppId
  if (normId === 'STEAM_CN' || normId === 'STEAM_GLOBAL') {
    if (steamAppId === null || steamAppId === undefined || steamAppId <= 0) {
      // Invariant safeguard: even if named Steam, if steamAppId is missing, fail closed!
      return {
        platformId: normId,
        platformName,
        isSupported: true,
        canLaunch: false,
        launchMethod: PlatformLaunchMethod.LAUNCH_NOT_IMPLEMENTED,
        action: 'MANUAL_LAUNCH_REQUIRED',
        uri: null,
        steamAppId: null,
        status: 'LAUNCH_NOT_IMPLEMENTED',
        reason: 'Missing steamAppId',
        userMessage: `${platformName} 缺少有效的 Steam AppID，已阻断拉起`,
      };
    }

    const uri = `steam://run/${steamAppId}`;
    return {
      platformId: normId,
      platformName,
      isSupported: true,
      canLaunch: true,
      launchMethod: PlatformLaunchMethod.LAUNCH_METHOD_STEAM_URI,
      action: 'LAUNCH_OFFICIAL_STEAM',
      uri,
      steamAppId,
      status: 'READY',
      reason: null,
      userMessage: `通过 Steam 协议唤起官方客户端 (${uri})`,
    };
  }

  // 4. TapTap Platform: Audited launcher protocol, explicit MANUAL_LAUNCH_REQUIRED policy
  if (normId === 'TAPTAP') {
    return {
      platformId: normId,
      platformName,
      isSupported: true,
      canLaunch: false,
      launchMethod: PlatformLaunchMethod.LAUNCH_NOT_IMPLEMENTED,
      action: 'MANUAL_LAUNCH_REQUIRED',
      uri: null,
      steamAppId: null,
      status: 'LAUNCH_NOT_IMPLEMENTED',
      reason: 'Unaudited launcher',
      userMessage: `${platformName} 探针容器已完成受控部署。根据安全审计规范，星枢不直接拉起 TapTap 游戏进程，请通过 TapTap 客户端手动启动游戏。`,
    };
  }

  // 5. Non-Steam Platforms: BILIBILI, WEGAME
  // STRICT RULE: If steamAppId == null, MUST NEVER execute or fallback to steam://run/2622000!
  // MUST fail closed with LAUNCH_NOT_IMPLEMENTED.
  return {
    platformId: normId,
    platformName,
    isSupported: true,
    canLaunch: false,
    launchMethod: PlatformLaunchMethod.LAUNCH_NOT_IMPLEMENTED,
    action: 'MANUAL_LAUNCH_REQUIRED',
    uri: null,
    steamAppId: null,
    status: 'LAUNCH_NOT_IMPLEMENTED',
    reason: 'Unaudited launcher',
    userMessage: `${platformName} 官方启动器集成仍在合规审计中 (Unaudited launcher)，请直接打开官方客户端启动游戏。`,
  };
}

/**
 * Authentication policy type.
 */
export type PlatformAuthPolicy =
  | 'OFFICIAL_CLIENT_LOGIN'
  | 'SERVICE_NOT_CONFIGURED';

export const PlatformAuthPolicy = {
  OFFICIAL_CLIENT_LOGIN: 'OFFICIAL_CLIENT_LOGIN' as PlatformAuthPolicy,
  SERVICE_NOT_CONFIGURED: 'SERVICE_NOT_CONFIGURED' as PlatformAuthPolicy,
} as const;

/**
 * Platform Authentication Strategy Plan
 */
export interface PlatformAuthStrategyPlan {
  platformId: string;
  accountMode: 'USER_OWNED' | 'XINGSHU_MANAGED';
  authPolicy: PlatformAuthPolicy;
  requiresCredentials: false;
  status: SpectatorAccountStatus;
  userMessage: string;
  flowSteps: string[];
  securityAssurance: string;
}

/**
 * Resolves the authentication strategy for a platform and account mode.
 * STRICT PRIVACY GUARANTEE:
 * 1. Zero credential collection.
 * 2. Official client login exclusively for user accounts.
 * 3. XingShu Managed Account remains SERVICE_NOT_CONFIGURED in V1.
 */
export function resolvePlatformAuthStrategy(
  platformId: string = 'STEAM_CN',
  accountMode: 'USER_OWNED' | 'XINGSHU_MANAGED' = 'USER_OWNED'
): PlatformAuthStrategyPlan {
  const normId = (platformId || '').trim();
  const platformName = PLATFORM_DISPLAY_NAMES[normId] || normId || '未知平台';

  if (accountMode === 'XINGSHU_MANAGED') {
    return {
      platformId: normId,
      accountMode: 'XINGSHU_MANAGED',
      authPolicy: PlatformAuthPolicy.SERVICE_NOT_CONFIGURED,
      requiresCredentials: false,
      status: SpectatorAccountStatus.SERVICE_NOT_CONFIGURED,
      userMessage: OFFICIAL_WATCHER_UNCONFIGURED_MESSAGE,
      flowSteps: [
        'V1 阶段星枢官方专用观战账号集群尚未开放，服务未配置。',
        '严禁在本地伪造模拟账号或捕获用户凭据。',
        '用户可无缝切换至"自备观战账号"模式进行无感观战。',
      ],
      securityAssurance:
        '服务未配置时保持诚实状态暴露，杜绝任何虚假 Mock 登录与云端鉴权劫持。',
    };
  }

  // USER_OWNED Mode: OFFICIAL_CLIENT_LOGIN
  return {
    platformId: normId,
    accountMode: 'USER_OWNED',
    authPolicy: PlatformAuthPolicy.OFFICIAL_CLIENT_LOGIN,
    requiresCredentials: false,
    status: SpectatorAccountStatus.AVAILABLE,
    userMessage: `${platformName} 观战模式：请在官方客户端内登录并启动游戏，星枢将自动同步局内真值。`,
    flowSteps: [
      `1. 用户自主启动 ${platformName} 官方客户端`,
      '2. 在官方认证界面中完成安全登录（星枢不接触任何账号凭据）',
      '3. 游戏进程启动并加载 APX Runtime 探针',
      '4. 运行时通过本地回环 TCP (127.0.0.1:25890) 握手并同步当前真实 currentSessionPlayerId',
      '5. 星枢 Hub 自动识别对局与角色绑定关系，开始无损呈现对手全量手牌',
    ],
    securityAssurance:
      '零凭据收集政策：星枢绝不收集、请求或在本地存储任何密码、Steam Token、Cookie 或会话密钥。',
  };
}
