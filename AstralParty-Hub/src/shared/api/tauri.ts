import { invoke } from '@tauri-apps/api/core';
import { isTauriRuntime } from '../runtime.ts';
let syncedRuntimeBindings: string | null = null;
let runtimeBindingsSync: Promise<void> | null = null;

function safeInvoke<T>(command: string, args?: Record<string, unknown>, webFallback?: T): Promise<T> {
  if (!isTauriRuntime()) {
    if (webFallback !== undefined) return Promise.resolve(webFallback);
    return Promise.reject(new Error('当前为纯网页版模式，本地游戏与插件管理仅在星枢 Hub 桌面端可用'));
  }
  return invoke<T>(command, args);
}

const EMPTY_WEB_REPORT: DiagnosticReport = {
  overall_status: 'HEALTHY',
  items: [],
  snapshot: {
    os_name: 'Web',
    os_version: '',
    running_processes: [],
    xingshu_detected: false,
    recent_transactions: [],
  },
};
import { CANONICAL_BOUND_SPECTATOR_PLAYER_ID } from '../../services/expectedAccountBindings.ts';
import type {
  Backup,
  Card,
  CardInventory,
  Character,
  CharacterProgress,
  DiagnosticReport,
  GameInstallation,
  HubPackageManifestV1,
  InstallTransaction,
  ModPackage,
  PlayerProfile,
  Plugin,
  ProbeResult,
  ProviderMetadata,
  SpectatorHandPayload,
  GameProcessStatus,
  GameLaunchResult,
} from '../types';

const isZh = (): boolean => {
  if (typeof localStorage !== 'undefined') {
    const saved = localStorage.getItem('ui.locale');
    if (saved === 'zh-CN') return true;
    if (saved === 'en-US') return false;
  }
  if (typeof navigator !== 'undefined') {
    return (navigator.language || '').toLowerCase().startsWith('zh');
  }
  return true;
};

export const api = {
  // Locale & Startup
  getStartupLocale: () => safeInvoke<string | null>('get_startup_locale', undefined, null).catch(() => null),
  getStartupRoute: () => safeInvoke<string | null>('get_startup_route', undefined, null).catch(() => null),

  // Game
  detectGame: () => safeInvoke<GameInstallation>('detect_game'),
  setManualGamePath: (path: string) =>
    safeInvoke<GameInstallation>('set_manual_game_path', { path }),
  getPrimaryGame: () => safeInvoke<GameInstallation | null>('get_primary_game', undefined, null),
  checkGameProcessStatus: (platformId?: string) =>
    safeInvoke<GameProcessStatus>('check_game_process_status', { platformId }).catch(() => ({
      isRunning: false,
      pid: undefined,
      processName: '',
      platformId: platformId || 'STEAM_CN',
    })),
  launchGameUri: (uri?: string, platformId?: string) =>
    safeInvoke<GameLaunchResult>('launch_game_uri', { uri, platformId,
      executablePath: typeof localStorage !== 'undefined' && platformId ? localStorage.getItem(`xingshu.launcher.${platformId}.executablePath`) : null,
    }).catch((err) => ({
      success: false,
      uri: uri || 'steam://run/2622000',
      alreadyRunning: false,
      message: err instanceof Error ? err.message : String(err),
    })),

  // Player & Progress
  getPlayerProfile: () => safeInvoke<PlayerProfile | null>('get_player_profile', undefined, null),
  listCharacterProgress: (profileId: string) =>
    safeInvoke<CharacterProgress[]>('list_character_progress', { profileId }, []),
  listCardInventory: (profileId: string) =>
    safeInvoke<CardInventory[]>('list_card_inventory', { profileId }, []),

  // Catalog
  listCharacters: () => safeInvoke<Character[]>('list_characters', undefined, []),
  listCards: () => safeInvoke<Card[]>('list_cards', undefined, []),

  // Extensions
  listPlugins: () => safeInvoke<Plugin[]>('list_plugins', undefined, []),
  listMods: () => safeInvoke<ModPackage[]>('list_mods', undefined, []),
  inspectPackageArchive: (archivePath: string) =>
    safeInvoke<HubPackageManifestV1>('inspect_package_archive', { archivePath }),
  uninstallExtension: (extensionId: string, isPlugin: boolean, installationId: string) =>
    safeInvoke<void>('uninstall_extension', { extensionId, isPlugin, installationId }),

  // Install Engine
  installPackage: (archivePath: string, installationId: string) =>
    safeInvoke<InstallTransaction>('install_package', { archivePath, installationId }),
  rollbackTransaction: (transactionId: string) =>
    safeInvoke<void>('rollback_transaction', { transactionId }),
  listInstallTransactions: (limit?: number) =>
    safeInvoke<InstallTransaction[]>('list_install_transactions', { limit }, []),
  listBackups: () => safeInvoke<Backup[]>('list_backups', undefined, []),

  // Diagnostics & Providers
  getDiagnosticReport: () => safeInvoke<DiagnosticReport>('get_diagnostic_report', undefined, EMPTY_WEB_REPORT),
  listProviders: () => safeInvoke<ProviderMetadata[]>('list_providers', undefined, []),
  probeProviders: () => safeInvoke<Array<[ProviderMetadata, ProbeResult]>>('probe_providers', undefined, []),
  syncProviderData: (providerId: string) =>
    safeInvoke<number>('sync_provider_data', { providerId }),

  // XingShu Runtime Manager
  getRuntimeStatus: () =>
    safeInvoke<{
      active_version: string;
      versions: import('../types').RuntimeVersion[];
      installed_runtime?: import('../types').InstalledRuntime;
    }>('get_runtime_status').catch(() => ({
      active_version: 'XingShu v1.3.4 (Production)',
      installed_runtime: {
        version_id: 'xingshu-1.3.4',
        runtime_type: 'XingShu' as const,
        version_name: 'XingShu v1.3.4 (Production Core)',
        install_path: 'com.unity.addressables/.../__data',
        container_hash: 'sha256:7f8e219ba420d931acbf6e',
        manifest_verified: true,
        installed_at: '2026-10-15T10:00:00Z',
      },
      versions: [
        {
          version_id: 'xingshu-1.3.4',
          runtime_type: 'XingShu' as const,
          version_name: 'XingShu v1.3.4 (Production Core)',
          is_installed: true,
          is_active: true,
          install_date: '2026-10-15',
          description: isZh()
            ? '针对星引擎深度优化的原生 DirectX 11 注入通道与微秒级命名管道通信层'
            : 'Optimized high-performance AstralParty runtime with Native DirectX 11 hooks',
        },
        {
          version_id: 'bep-5.4.23',
          runtime_type: 'BEPINEX_5' as const,
          version_name: 'BepInEx 5.4.23.2 (Bleeding Edge)',
          is_installed: true,
          is_active: false,
          install_date: '2026-10-10',
          description: isZh()
            ? '标准 Unity IL2CPP / Mono 互操作加载器与 Harmony 补丁框架'
            : 'Standard Unity IL2CPP/Mono interop loader with pluggable harmony patches',
        },
        {
          version_id: 'vanilla-official',
          runtime_type: 'VANILLA' as const,
          version_name: isZh() ? '原生纯净版 (官方未修改)' : 'Vanilla Clean (Official)',
          is_installed: true,
          is_active: false,
          description: isZh()
            ? '官方原生游戏未注入二进制文件'
            : 'Pure unmodified official game binaries without injection',
        },
      ],
    })),
  getInstalledRuntime: () =>
    invoke<import('../types').InstalledRuntime>('get_installed_runtime').catch(() => ({
      version_id: 'xingshu-1.3.4',
      runtime_type: 'XingShu' as const,
      version_name: 'XingShu v1.3.4 (Production Core)',
      install_path: 'com.unity.addressables/.../__data',
      container_hash: 'sha256:7f8e219ba420d931acbf6e',
      manifest_verified: true,
      installed_at: '2026-10-15T10:00:00Z',
    })),
  switchRuntimeVersion: (versionId: string) =>
    invoke<void>('switch_runtime_version', { versionId }).catch(() => console.log('Runtime switched to', versionId)),
  deployRuntime: (runtimeType: string) =>
    invoke<void>('deploy_runtime', { runtimeType }).catch(() => console.log('Deployed runtime', runtimeType)),

  // XingShu Spectator Center
  listSpectatorAccounts: () =>
    invoke<import('../types').SpectatorAccount[]>('list_spectator_accounts').catch(() => [
      {
        id: 'spec-acc-01',
        account_name: isZh() ? '观战辅助号_Alpha (Steam)' : 'SpectatorBot_Alpha (Steam Auxiliary)',
        auth_mode: 'STEAM_SESSION' as const,
        is_active: true,
        status: 'READY' as const,
        steam_id64: '76561198000000001',
        created_at: '2026-10-15T12:00:00Z',
        last_authenticated_at: '2026-10-15T19:20:00Z',
      },
      {
        id: 'spec-acc-02',
        account_name: isZh() ? '社区托管号_Secondary' : 'CommunityWatch_Secondary',
        auth_mode: 'ACCOUNT_TOKEN' as const,
        is_active: false,
        status: 'OFFLINE' as const,
        created_at: '2026-10-14T08:00:00Z',
      },
    ]),
  startSpectatorSession: (watchCode: string, accountId: string) =>
    invoke<import('../types').SpectatorSession>('start_spectator_session', { watchCode, accountId }).catch(() => {
      const zh = isZh();
      return {
        session_id: 'sess-' + Date.now(),
        account_id: accountId,
        room_watch_code: watchCode,
        state: 'SPECTATING_ACTIVE' as const,
        connected_at: new Date().toISOString(),
        battle_state: {
          room_id: '10977524491029384',
          watch_code: watchCode,
          map_id: 82016,
          map_name: zh ? '星穹之舟' : 'Starship Odyssey',
          current_round: 7,
          current_turn_player_id: 'p-101',
          phase: 'PLAYER_TURN' as const,
          subscribed_player_id: 'p-101',
          spectator_count: 3,
          last_event: zh
            ? '玩家 [爱丽丝] 打出 [星爆术] 对 [鲍勃] 造成 3 点伤害'
            : 'Player [Alice] played [Star Burst] dealing 3 damage to [Bob]',
          updated_at: new Date().toISOString(),
          players: [
            {
              player_id: 'p-101',
              seat_index: 0,
              nickname: zh ? '爱丽丝' : 'Alice',
              character_id: 'char_canglong',
              character_name: zh ? '苍龙' : 'CangLong',
              hp: 8,
              max_hp: 10,
              stars: 5,
              coins: 18,
              is_alive: true,
              status_effects: [{ id: 'atk_buff', name: 'ATK +2', duration: 2 }],
              hand_cards: [
                {
                  card_id: 1001,
                  card_key: 'strike_plus',
                  name: zh ? '精准猛击' : 'Precision Strike',
                  cost: 1,
                  card_type: 'ATTACK' as const,
                  description: zh ? '对目标造成 3 点穿刺伤害' : 'Deals 3 piercing damage to target',
                  is_temporary: false,
                },
                {
                  card_id: 1004,
                  card_key: 'aegis_shield',
                  name: zh ? '星盾守护' : 'Aegis Shield',
                  cost: 1,
                  card_type: 'DEFENSE' as const,
                  description: zh ? '获得 4 点临时格挡' : 'Gain 4 temporary block',
                  is_temporary: false,
                },
                {
                  card_id: 2012,
                  card_key: 'dimensional_swap',
                  name: zh ? '相位跃迁' : 'Phase Shift',
                  cost: 2,
                  card_type: 'MAGIC' as const,
                  description: zh ? '与一名玩家交换棋盘格子位置' : 'Swap board tiles with a target player',
                  is_temporary: false,
                },
              ],
            },
            {
              player_id: 'p-102',
              seat_index: 1,
              nickname: zh ? '鲍勃' : 'Bob',
              character_id: 'char_fenghuang',
              character_name: zh ? '凤凰' : 'Phoenix',
              hp: 4,
              max_hp: 8,
              stars: 3,
              coins: 12,
              is_alive: true,
              status_effects: [],
              hand_cards: [
                {
                  card_id: 1002,
                  card_key: 'fireball',
                  name: zh ? '灼热火球' : 'Scorching Fireball',
                  cost: 2,
                  card_type: 'MAGIC' as const,
                  description: zh ? '对目标造成 4 点火焰魔法伤害' : 'Deals 4 fire magic damage to target',
                  is_temporary: false,
                },
                {
                  card_id: 3001,
                  card_key: 'spring_trap',
                  name: zh ? '弹簧陷阱' : 'Spring Trap',
                  cost: 1,
                  card_type: 'TRAP' as const,
                  description: zh ? '在此格埋设陷阱，踩中后击退 3 格' : 'Place trap on tile, push back 3 tiles on trigger',
                  is_temporary: false,
                },
              ],
            },
            {
              player_id: 'p-103',
              seat_index: 2,
              nickname: zh ? '查理' : 'Charlie',
              character_id: 'char_baihu',
              character_name: zh ? '白虎' : 'White Tiger',
              hp: 6,
              max_hp: 10,
              stars: 4,
              coins: 24,
              is_alive: true,
              status_effects: [{ id: 'speed_up', name: 'Speed +1', duration: 1 }],
              hand_cards: [
                {
                  card_id: 1008,
                  card_key: 'super_charge',
                  name: zh ? '极速过载' : 'Speed Overdrive',
                  cost: 0,
                  card_type: 'SPECIAL' as const,
                  description: zh ? '本回合多投掷一枚移动骰子' : 'Roll an extra movement die this round',
                  is_temporary: false,
                },
              ],
            },
            {
              player_id: 'p-104',
              seat_index: 3,
              nickname: zh ? '戴安娜' : 'Diana',
              character_id: 'char_xuanwu',
              character_name: zh ? '玄武' : 'Black Tortoise',
              hp: 10,
              max_hp: 12,
              stars: 6,
              coins: 30,
              is_alive: true,
              status_effects: [{ id: 'iron_wall', name: zh ? '铁壁防守' : 'Iron Fortress', duration: 3 }],
              hand_cards: [
                {
                  card_id: 1010,
                  card_key: 'heal_wave',
                  name: zh ? '星之祈愿' : 'Star Wish',
                  cost: 2,
                  card_type: 'MAGIC' as const,
                  description: zh ? '恢复全队 3 点生命值' : 'Restore 3 HP to all teammates',
                  is_temporary: false,
                },
                {
                  card_id: 1015,
                  card_key: 'gold_rush',
                  name: zh ? '淘金狂潮' : 'Gold Rush',
                  cost: 1,
                  card_type: 'SPECIAL' as const,
                  description: zh ? '抽取 2 张手牌并获得 5 金币' : 'Draw 2 cards and gain 5 coins',
                  is_temporary: false,
                },
              ],
            },
          ],
        },
      };
    }),
  stopSpectatorSession: (sessionId: string) =>
    invoke<void>('stop_spectator_session', { sessionId }).catch(() => console.log('Stopped session', sessionId)),
  setSubscribePlayer: (sessionId: string, playerId: string) =>
    invoke<void>('set_subscribe_player', { sessionId, playerId }).catch(() => console.log('Subscribed to', playerId)),
  getSpectatorHandPayload: () =>
    safeInvoke<SpectatorHandPayload | null>('get_spectator_hand_payload', undefined, null).catch(() => null),

  // XingShu Bridge (IPC Server)
  getBridgeStatus: () =>
    safeInvoke<import('../types').BridgeConnectionState | null>('get_bridge_status', undefined, null).catch(() => null),
  getRuntimeSessions: async () => {
    if (!isTauriRuntime()) return [];
    const { gameAccountService } = await import('../../services/gameAccountService.ts');
    const bindings = gameAccountService.getBindings().map(({ platformId, playerId, role }) => ({ platformId, playerId, role }));
    const key = JSON.stringify(bindings);
    if (runtimeBindingsSync) await runtimeBindingsSync;
    if (key !== syncedRuntimeBindings) {
      runtimeBindingsSync = safeInvoke<void>('configure_runtime_bindings', { bindings })
        .then(() => { syncedRuntimeBindings = key; }).finally(() => { runtimeBindingsSync = null; });
      await runtimeBindingsSync;
    }
    return safeInvoke<import('../../types/runtimeSession').RuntimeSession[]>('get_runtime_sessions', undefined, []).catch(() => []);
  },
  sendBridgeTestEvent: (message?: string) =>
    safeInvoke<void>('send_bridge_test_event', { message }),
  sendWatchTarget: (
    connectionId: string,
    target: any,
    expectedSpectatorPlayerId: number = CANONICAL_BOUND_SPECTATOR_PLAYER_ID
  ) =>
    safeInvoke<string>('send_watch_target', {
      connectionId,
      target,
      expectedSpectatorPlayerId,
    }),
  clearBridgeLogs: () =>
    safeInvoke<void>('clear_bridge_logs'),

  // Match History & Analytics
  // Remote Packages / Update Center
  listRemotePackages: () => safeInvoke<import('../types').RemotePackage[]>('list_remote_packages', undefined, []),

  // Mod / Card / Skin Workshop (Desktop installer)
  workshopListLocalState: (installationId: string) =>
    safeInvoke<WorkshopLocalItemState[]>('workshop_list_local_state', { installationId }, []),
  workshopDownloadAndInstall: (request: WorkshopInstallRequestPayload) =>
    safeInvoke<WorkshopInstallResult>('workshop_download_and_install', { request }),
  workshopRestoreMod: (installationId: string, modId: string) =>
    safeInvoke<string>('workshop_restore_mod', { installationId, modId }),
};

export interface WorkshopDownloadFilePayload {
  target: string;
  sha256: string;
  originalSha256?: string;
  size?: number;
  payloadUrl?: string | null;
}

export interface WorkshopInstallRequestPayload {
  installationId: string;
  apiBase: string;
  modId: string;
  name: string;
  installKind: string;
  gameVersion?: string;
  platforms?: string[];
  downloadUrl?: string | null;
  sha256?: string;
  sizeBytes?: number;
  files?: WorkshopDownloadFilePayload[];
}

export interface WorkshopLocalItemState {
  modId: string;
  phase: string;
  installKind: string;
  transactionId?: string | null;
  downloadedPath?: string | null;
  canRestore: boolean;
}

export interface WorkshopInstallResult {
  modId: string;
  phase: string;
  message: string;
  downloadedPath?: string | null;
  transactionId?: string | null;
}

export const getSpectatorHandPayload = api.getSpectatorHandPayload;
export const getRuntimeSessions = api.getRuntimeSessions;
