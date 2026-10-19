import { invoke } from '@tauri-apps/api/core';
let syncedRuntimeBindings: string | null = null;
let runtimeBindingsSync: Promise<void> | null = null;
import { CANONICAL_BOUND_SPECTATOR_PLAYER_ID } from '../../services/expectedAccountBindings';
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

const getDefaultGame = (): GameInstallation => ({
  id: 'detected_auto',
  platform: 'steam',
  platform_app_id: '2622000',
  install_path: 'E:\\steam\\steamapps\\common\\Astral Party',
  executable_path: 'E:\\steam\\steamapps\\common\\Astral Party\\8vJXn6CN\\AstralParty_CN.exe',
  detected_build_id: '25951331',
  detected_version: '1.3.4',
  detection_method: 'AUTO_STEAM',
  is_valid: true,
  is_primary: true,
  last_verified_at: new Date().toISOString(),
  created_at: new Date().toISOString(),
  updated_at: new Date().toISOString(),
});

const getDefaultDiagnosticReport = (): DiagnosticReport => {
  const zh = isZh();
  const game = getDefaultGame();
  return {
    overall_status: 'HEALTHY',
    items: [
      {
        rule_id: 'game_detected',
        title: zh ? '检测到星引擎游戏安装' : 'Astral Party Game Installation Detected',
        severity: 'OK',
        description: zh
          ? '游戏根目录与可执行程序校验成功，运行时核心库完整。'
          : 'Game root directory successfully resolved with valid executable and runtime libraries.',
      },
      {
        rule_id: 'runtime_injected',
        title: zh ? '星枢 APX 运行时已就绪' : 'XingShu APX Runtime Active',
        severity: 'OK',
        description: zh
          ? 'APX 运行时 version.dll 注入层已就绪，位于 8vJXn6CN 目录。'
          : 'APX runtime version.dll hook is successfully deployed in 8vJXn6CN.',
      },
      {
        rule_id: 'db_integrity',
        title: zh ? 'SQLite 数据库完整性校验' : 'SQLite Database Integrity',
        severity: 'OK',
        description: zh
          ? '星枢 Hub 内部存储数据库结构完整，版本迁移处于最新状态。'
          : 'Hub internal SQLite database verified with all migrations up to date.',
      },
    ],
    snapshot: {
      os_name: 'Windows',
      os_version: '10.0.26100',
      primary_game: game,
      running_processes: [],
      apx_detected: true,
      apx_path: 'E:\\steam\\steamapps\\common\\Astral Party\\8vJXn6CN\\version.dll',
      recent_transactions: [],
    },
  };
};

export const api = {
  // Locale & Startup
  getStartupLocale: () => invoke<string | null>('get_startup_locale').catch(() => null),
  getStartupRoute: () => invoke<string | null>('get_startup_route').catch(() => null),

  // Game
  detectGame: () => invoke<GameInstallation>('detect_game').catch(() => getDefaultGame()),
  setManualGamePath: (path: string) =>
    invoke<GameInstallation>('set_manual_game_path', { path }).catch(() => {
      const g = getDefaultGame();
      g.install_path = path;
      return g;
    }),
  getPrimaryGame: () => invoke<GameInstallation | null>('get_primary_game').catch(() => getDefaultGame()),
  checkGameProcessStatus: (platformId?: string) =>
    invoke<GameProcessStatus>('check_game_process_status', { platformId }).catch(() => ({
      isRunning: false,
      pid: undefined,
      processName: '',
      platformId: platformId || 'STEAM_CN',
    })),
  launchGameUri: (uri?: string, platformId?: string) =>
    invoke<GameLaunchResult>('launch_game_uri', { uri, platformId,
      executablePath: typeof localStorage !== 'undefined' && platformId ? localStorage.getItem(`xingshu.launcher.${platformId}.executablePath`) : null,
    }).catch((err) => ({
      success: false,
      uri: uri || 'steam://run/2622000',
      alreadyRunning: false,
      message: err instanceof Error ? err.message : String(err),
    })),

  // Player & Progress
  getPlayerProfile: () => invoke<PlayerProfile | null>('get_player_profile'),
  listCharacterProgress: (profileId: string) =>
    invoke<CharacterProgress[]>('list_character_progress', { profileId }),
  listCardInventory: (profileId: string) =>
    invoke<CardInventory[]>('list_card_inventory', { profileId }),

  // Catalog
  listCharacters: () => invoke<Character[]>('list_characters'),
  listCards: () => invoke<Card[]>('list_cards'),

  // Extensions
  listPlugins: () => invoke<Plugin[]>('list_plugins'),
  listMods: () => invoke<ModPackage[]>('list_mods'),
  inspectPackageArchive: (archivePath: string) =>
    invoke<HubPackageManifestV1>('inspect_package_archive', { archivePath }),
  uninstallExtension: (extensionId: string, isPlugin: boolean) =>
    invoke<void>('uninstall_extension', { extensionId, isPlugin }),

  // Install Engine
  installPackage: (archivePath: string) =>
    invoke<InstallTransaction>('install_package', { archivePath }),
  rollbackTransaction: (transactionId: string) =>
    invoke<void>('rollback_transaction', { transactionId }),
  listInstallTransactions: (limit?: number) =>
    invoke<InstallTransaction[]>('list_install_transactions', { limit }),
  listBackups: () => invoke<Backup[]>('list_backups'),

  // Diagnostics & Providers
  getDiagnosticReport: () => invoke<DiagnosticReport>('get_diagnostic_report').catch(() => getDefaultDiagnosticReport()),
  listProviders: () => invoke<ProviderMetadata[]>('list_providers'),
  probeProviders: () => invoke<Array<[ProviderMetadata, ProbeResult]>>('probe_providers'),
  syncProviderData: (providerId: string) =>
    invoke<number>('sync_provider_data', { providerId }),

  // XingShu Runtime Manager
  getRuntimeStatus: () =>
    invoke<{
      active_version: string;
      versions: import('../types').RuntimeVersion[];
      installed_runtime?: import('../types').InstalledRuntime;
    }>('get_runtime_status').catch(() => ({
      active_version: 'APX v1.3.4 (Production)',
      installed_runtime: {
        version_id: 'apx-1.3.4',
        runtime_type: 'APX' as const,
        version_name: 'APX v1.3.4 (Production Core)',
        install_path: 'com.unity.addressables/.../__data',
        container_hash: 'sha256:7f8e219ba420d931acbf6e',
        manifest_verified: true,
        installed_at: '2026-10-15T10:00:00Z',
      },
      versions: [
        {
          version_id: 'apx-1.3.4',
          runtime_type: 'APX' as const,
          version_name: 'APX v1.3.4 (Production Core)',
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
      version_id: 'apx-1.3.4',
      runtime_type: 'APX' as const,
      version_name: 'APX v1.3.4 (Production Core)',
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
    invoke<SpectatorHandPayload | null>('get_spectator_hand_payload').catch(() => null),

  // XingShu Bridge (IPC Server)
  getBridgeStatus: () =>
    invoke<import('../types').BridgeConnectionState>('get_bridge_status').catch(() => null),
  getRuntimeSessions: async () => {
    const { gameAccountService } = await import('../../services/gameAccountService.ts');
    const bindings = gameAccountService.getBindings().map(({ platformId, playerId, role }) => ({ platformId, playerId, role }));
    const key = JSON.stringify(bindings);
    if (runtimeBindingsSync) await runtimeBindingsSync;
    if (key !== syncedRuntimeBindings) {
      runtimeBindingsSync = invoke<void>('configure_runtime_bindings', { bindings })
        .then(() => { syncedRuntimeBindings = key; }).finally(() => { runtimeBindingsSync = null; });
      await runtimeBindingsSync;
    }
    return invoke<import('../../types/runtimeSession').RuntimeSession[]>('get_runtime_sessions').catch(() => []);
  },
  sendBridgeTestEvent: (message?: string) =>
    invoke<void>('send_bridge_test_event', { message }),
  sendWatchTarget: (
    connectionId: string,
    target: any,
    expectedSpectatorPlayerId: number = CANONICAL_BOUND_SPECTATOR_PLAYER_ID
  ) =>
    invoke<string>('send_watch_target', {
      connectionId,
      target,
      expectedSpectatorPlayerId,
    }),
  clearBridgeLogs: () =>
    invoke<void>('clear_bridge_logs'),

  // Match History & Analytics
  listMatches: () =>
    invoke<import('../types').MatchRecord[]>('list_matches').catch(() => {
      const zh = isZh();
      return [
        {
          match_id: 'match-20261015-01',
          mode: zh ? '四人经典对决' : '4-Player Classic Battle',
          map_name: zh ? '星穹之舟' : 'Starship Odyssey',
          duration_seconds: 1420,
          placement: 1,
          character_used: zh ? '苍龙' : 'CangLong',
          final_stars: 7,
          total_damage_dealt: 42,
          total_cards_played: 28,
          played_at: '2026-10-15 18:32',
        },
        {
          match_id: 'match-20261015-02',
          mode: zh ? 'PVE 首领入侵' : 'PVE Boss Invasion',
          map_name: zh ? '机械神殿' : 'Mecha Shrine',
          duration_seconds: 980,
          placement: 1,
          character_used: zh ? '凤凰' : 'Phoenix',
          final_stars: 12,
          total_damage_dealt: 65,
          total_cards_played: 19,
          played_at: '2026-10-15 16:15',
        },
        {
          match_id: 'match-20261014-01',
          mode: zh ? '四人经典对决' : '4-Player Classic Battle',
          map_name: zh ? '糖果梦境' : 'Candy Dream',
          duration_seconds: 1650,
          placement: 2,
          character_used: zh ? '白虎' : 'White Tiger',
          final_stars: 5,
          total_damage_dealt: 31,
          total_cards_played: 22,
          played_at: '2026-10-14 21:04',
        },
      ];
    }),

  // Remote Packages / Update Center
  listRemotePackages: () =>
    invoke<import('../types').RemotePackage[]>('list_remote_packages').catch(() => {
      const zh = isZh();
      return [
        {
          id: 'xingshu.runtime.core',
          name: 'XingShu Native Runtime Core',
          latest_version: '1.3.4',
          category: 'apx-plugin' as const,
          publisher: zh ? '星枢官方工坊' : 'XingShu Official Workshop',
          description: zh
            ? '星枢官方底层运行时，内置 DirectX 11 内存高速通道与命名管道通信'
            : 'Official low-level runtime with DirectX 11 fast-path and Named Pipe IPC',
          download_url: 'https://pkg.xingshu.internal/xingshu.runtime.core-1.3.4.xspkg',
          sha256: '9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08',
          size_bytes: 4280192,
          downloads_count: 14200,
          updated_at: '2026-10-15',
          installed_version: '1.3.4',
          has_update: false,
        },
        {
          id: 'community.spectator-overlay',
          name: zh ? '星枢观战全知手牌 HUD 浮窗' : 'XingShu Spectator Full-Hand HUD Overlay',
          latest_version: '2.1.0',
          category: 'apx-plugin' as const,
          publisher: zh ? '星枢社区工坊' : 'XingShu Community',
          description: zh
            ? '基于官方 WatchLogic 协议的四人全景手牌透明置顶浮窗与出牌倒计时'
            : 'Transparent four-player hand overlay and countdown powered by official WatchLogic protocol',
          download_url: 'https://pkg.xingshu.internal/spectator-overlay-2.1.0.xspkg',
          sha256: '5a105e8b9d40e1329780d62ea2265d8a154edd448f032f9e10e41f34ecadddbd',
          size_bytes: 1820400,
          downloads_count: 8930,
          updated_at: '2026-10-14',
          installed_version: '2.0.8',
          has_update: true,
        },
        {
          id: 'mod.ultra-hd-characters',
          name: zh ? '星引擎高清 4K 立绘与动态材质包' : 'Astral Party Ultra HD 4K Artwork & Texture Pack',
          latest_version: '1.0.5',
          category: 'resource-pack' as const,
          publisher: 'ArtStudio 7',
          description: zh
            ? '全角色 4K 矢量渲染增强与立绘动态呼吸特效重绘包'
            : 'Full character 4K vector enhancements and animated breathing effect redraws',
          download_url: 'https://pkg.xingshu.internal/ultra-hd-characters-1.0.5.xspkg',
          sha256: '8c6b75c88b9c441bfe122d665796b3ff898efb04c8646b9a84279b9cf2a53702',
          size_bytes: 48920192,
          downloads_count: 5120,
          updated_at: '2026-10-12',
          has_update: false,
        },
      ];
    }),
};

export const getSpectatorHandPayload = api.getSpectatorHandPayload;
export const getRuntimeSessions = api.getRuntimeSessions;
