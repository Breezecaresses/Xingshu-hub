export interface HubError {
  code: string;
  message: string;
  category: string;
  recoverable: boolean;
  user_action?: string;
  context?: any;
}

export interface GameInstallation {
  id: string;
  platform: string;
  platform_app_id?: string;
  install_path: string;
  executable_path?: string;
  detected_build_id?: string;
  detected_version?: string;
  detection_method: string;
  is_valid: boolean;
  is_primary: boolean;
  last_verified_at?: string;
  created_at: string;
  updated_at: string;
}

export interface DetectionEvidence {
  source: string;
  path: string;
  detail: string;
  passed: boolean;
}

export interface PlayerProfile {
  id: string;
  display_name?: string;
  avatar_uri?: string;
  created_at: string;
  updated_at: string;
}

export interface Character {
  character_key: string;
  name: string;
  rarity?: string;
  metadata_json?: any;
  first_seen_version?: string;
  last_seen_version?: string;
  created_at: string;
  updated_at: string;
}

export interface CharacterProgress {
  id: string;
  player_profile_id: string;
  character_key: string;
  owned: boolean;
  level?: number;
  star_level?: number;
  progress_json?: any;
  observed_at?: string;
  created_at: string;
  updated_at: string;
}

export interface Card {
  card_key: string;
  name: string;
  rarity?: string;
  card_type?: string;
  metadata_json?: any;
  first_seen_version?: string;
  last_seen_version?: string;
  created_at: string;
  updated_at: string;
}

export interface CardInventory {
  id: string;
  player_profile_id: string;
  card_key: string;
  owned: boolean;
  quantity?: number;
  upgrade_level?: number;
  inventory_json?: any;
  observed_at?: string;
  created_at: string;
  updated_at: string;
}

export interface Plugin {
  plugin_id: string;
  name: string;
  installed_version?: string;
  status: string;
  install_path?: string;
  manifest_json?: any;
  installed_at?: string;
  updated_at?: string;
}

export interface ModPackage {
  mod_id: string;
  name: string;
  installed_version?: string;
  status: string;
  install_path?: string;
  manifest_json?: any;
  installed_at?: string;
  updated_at?: string;
}

export interface HubPackageManifestV1 {
  schemaVersion: number;
  package: {
    id: string;
    type: 'apx-plugin' | 'mod' | 'resource-pack';
    name: string;
    version: string;
    publisher: string;
    description?: string;
  };
  compatibility: {
    game: {
      appId: string;
      minVersion?: string;
      maxVersion?: string;
    };
    apx?: {
      minVersion?: string;
      maxVersion?: string;
    };
  };
  install: {
    requiresGameClosed: boolean;
    requiresApxClosed: boolean;
    backup: boolean;
  };
  files: Array<{
    source: string;
    target: string;
    sha256: string;
    size: number;
  }>;
}

export interface InstallTransaction {
  id: string;
  package_id: string;
  package_version: string;
  operation: string;
  state: string;
  game_installation_id?: string;
  backup_id?: string;
  started_at: string;
  finished_at?: string;
  error_code?: string;
  error_message?: string;
}

export interface Backup {
  id: string;
  backup_type: string;
  target_id?: string;
  storage_path: string;
  manifest_json: any;
  created_at: string;
  verified_at?: string;
  status: string;
}

export interface DiagnosticItem {
  rule_id: string;
  title: string;
  severity: 'OK' | 'INFO' | 'WARNING' | 'ERROR';
  description: string;
  recommendation?: string;
}

export interface SystemDiagnosticSnapshot {
  os_name: string;
  os_version: string;
  primary_game?: GameInstallation;
  running_processes: string[];
  apx_detected: boolean;
  apx_path?: string;
  recent_transactions: InstallTransaction[];
}

export interface DiagnosticReport {
  overall_status: 'HEALTHY' | 'WARNING' | 'DEGRADED';
  items: DiagnosticItem[];
  snapshot: SystemDiagnosticSnapshot;
}

export interface ProviderMetadata {
  id: string;
  name: string;
  provider_type: string;
  version: string;
  description: string;
}

export interface ProbeResult {
  available: boolean;
  latency_ms?: number;
  status_message: string;
}

// ==========================================
// XingShu Runtime Manager Types
// ==========================================

export type GameChannel = 'STEAM_CN' | 'STEAM_INT' | 'DMM' | 'STANDALONE';

export interface RuntimeVersion {
  version_id: string;
  runtime_type: 'APX' | 'BEPINEX_5' | 'BEPINEX_6' | 'VANILLA';
  version_name: string;
  is_installed: boolean;
  is_active: boolean;
  install_date?: string;
  description?: string;
}

export interface SelectedRuntimePlan {
  version_id: string;
  runtime_type: 'APX' | 'BEPINEX_5' | 'BEPINEX_6' | 'VANILLA';
  version_name: string;
  description?: string;
}

export interface InstalledRuntime {
  version_id: string;
  runtime_type: 'APX' | 'BEPINEX_5' | 'BEPINEX_6' | 'VANILLA';
  version_name: string;
  install_path: string;
  container_hash?: string;
  manifest_verified: boolean;
  installed_at?: string;
}

export interface ActiveRuntimeSession {
  is_live_active: boolean; // Confirmed ONLY via live TCP Bridge handshake (HELLO_ACK accepted, gamePid, runtimeVersion)
  game_pid: number | null;
  game_running: boolean;
  runtime_version?: string;
  component: string; // Canonical host: XingShuRuntimeHost
  active_transport: string; // Loopback TCP (127.0.0.1:25890)
  handshake_status: 'HELLO_ACK' | 'HELLO_PENDING' | 'DISCONNECTED' | 'LEGACY_DETECTED';
  handshake_latency_ms?: number;
  last_heartbeat_timestamp?: number;
}

export type BridgeStateStatus = 'WAITING_CONNECTION' | 'CONNECTED' | 'PROTOCOL_READY';

export interface RuntimeTrichotomyState {
  selected_plan: SelectedRuntimePlan | null;
  installed_runtime: InstalledRuntime | null;
  active_session: ActiveRuntimeSession;
  bridge_state: BridgeStateStatus;
  legacy_detected: boolean;
  status_category: 'READY_NOT_RUNNING' | 'WAITING_RUNTIME_CONNECTION' | 'LIVE_SESSION_ACTIVE' | 'LEGACY_WARNING';
  overall_status_display: string;
}

export interface RuntimeDeployStatus {
  status: 'IDLE' | 'DOWNLOADING' | 'VERIFYING' | 'DEPLOYING' | 'ACTIVE' | 'ERROR';
  progress_pct: number;
  current_file?: string;
  error_message?: string;
}

export interface RuntimeManagerConfig {
  channel: GameChannel;
  active_version_id: string;
  auto_detect_on_startup: boolean;
  verify_integrity_on_launch: boolean;
  enable_runtime_console: boolean;
  bridge_pipe_name: string;
}

// ==========================================
// Mod / Resource Manager & .xspkg Types
// ==========================================

export type PackageCategory =
  | 'apx-plugin'
  | 'mod'
  | 'resource-pack'
  | 'voice-pack'
  | 'map-skin'
  | 'ui-theme';

export interface XspkgManifest {
  schemaVersion: number;
  package: {
    id: string;
    type: PackageCategory;
    name: string;
    version: string;
    publisher: string;
    description?: string;
    category?: PackageCategory;
    icon?: string;
    homepage?: string;
  };
  compatibility: {
    game: {
      appId: string;
      minVersion?: string;
      maxVersion?: string;
    };
    runtime?: {
      type: 'apx' | 'bepinex';
      minVersion?: string;
      maxVersion?: string;
    };
    dependencies?: Record<string, string>;
  };
  install: {
    requiresGameClosed: boolean;
    requiresRuntimeClosed?: boolean;
    backup: boolean;
    postDeployNotice?: string;
  };
  files: Array<{
    source: string;
    target: string;
    sha256: string;
    size: number;
  }>;
  signature?: {
    algorithm: 'Ed25519';
    publicKey: string;
    signature: string;
  };
}

// ==========================================
// Spectator Center Types
// ==========================================

export type SpectatorSessionState =
  | 'IDLE'
  | 'AUTHENTICATING'
  | 'READY'
  | 'LOOKING_UP_ROOM'
  | 'CONNECTING_ROOM'
  | 'SYNCING_INITIAL_STATE'
  | 'SPECTATING_ACTIVE'
  | 'PAUSED'
  | 'DISCONNECTING'
  | 'ERROR_BACKOFF';

export interface SpectatorAccount {
  id: string;
  account_name: string;
  auth_mode: 'STEAM_SESSION' | 'ACCOUNT_TOKEN' | 'MANUAL_COOKIES';
  is_active: boolean;
  status: 'READY' | 'IN_SESSION' | 'EXPIRED' | 'OFFLINE' | 'ERROR';
  steam_id64?: string;
  created_at: string;
  last_authenticated_at?: string;
}

export interface SpectatorHandCard {
  card_id: number; // Positive authoritative card ID
  card_key: string;
  name: string;
  cost: number;
  card_type: 'ATTACK' | 'DEFENSE' | 'MAGIC' | 'SPECIAL' | 'TRAP';
  description: string;
  is_temporary: boolean;
}

export interface SpectatorPlayerState {
  player_id: string;
  seat_index: number;
  nickname: string;
  character_id: string;
  character_name: string;
  hp: number;
  max_hp: number;
  stars: number;
  coins: number;
  is_alive: boolean;
  status_effects: Array<{ id: string; name: string; duration: number }>;
  hand_cards: SpectatorHandCard[];
}

export interface SpectatorBattleState {
  room_id: string;
  watch_code: string;
  map_id: number;
  map_name: string;
  current_round: number;
  current_turn_player_id: string;
  phase: 'ROUND_START' | 'PLAYER_TURN' | 'BATTLE_CLASH' | 'EVENT' | 'ROUND_END' | 'MATCH_FINISH';
  subscribed_player_id: string;
  players: SpectatorPlayerState[];
  spectator_count: number;
  last_event?: string;
  updated_at: string;
}

export interface SpectatorSession {
  session_id: string;
  account_id: string;
  room_watch_code: string;
  state: SpectatorSessionState;
  connected_at?: string;
  battle_state?: SpectatorBattleState;
  error_message?: string;
}

// HS3 Spectator Hand Snapshot Protocol Types (schemaVersion: 1.1.0)
export interface CardCountEntry {
  cardId: number;
  count: number;
}

export interface PlayerHandSummary {
  playerId: number;
  handSize: number;
  cardIds: number[];
  cards: CardCountEntry[];
}

export interface SpectatorHandPayload {
  schemaVersion: string;
  roomId: number;
  spectatorSessionEpoch: number;
  spectatorAccountPlayerId?: number;
  sequenceNumber: number;
  snapshotTimestamp?: string;
  playerId: number;
  cardIds?: number[];
  cards?: CardCountEntry[];
  handSize?: number;
  drawCountTotal?: number;
  playCountTotal?: number;
  roomPlayers: Record<string, PlayerHandSummary>;
  connectionId?: string;
  platformId?: string;
}

// ==========================================
// XingShu Bridge Types (Named Pipe & WebSocket IPC)
// ==========================================

export interface BridgeConfig {
  named_pipe_name: string;
  enable_named_pipe: boolean;
  ws_port: number;
  enable_websocket: boolean;
  auth_token: string;
  heartbeat_interval_ms: number;
}

export interface BridgeLogEntry {
  id: string;
  direction: 'IN' | 'OUT' | 'SYS';
  messageType: string;
  summary: string;
  rawJson: string;
  timestamp: number;
}

export interface BridgeConnectionState {
  isRunning: boolean;
  namedPipeConnected: boolean;
  namedPipe: string;
  localhostPort: number;
  runtimeConnected: boolean;
  protocolVersion: string;
  gamePid?: number;
  gameVersion?: string;
  runtimeVersion?: string;
  lastHeartbeatTimestamp?: number;
  lastHeartbeatLatencyMs?: number;
  connectedAt?: number;
  totalMessagesReceived: number;
  totalMessagesSent: number;
  recentMessages: BridgeLogEntry[];
  activeTransport?: 'LOOPBACK_TCP' | 'NAMED_PIPE_LEGACY' | 'DISCONNECTED';
  tcpConnected?: boolean;
  tcpAddr?: string;

  // Compatibility aliases
  active_ws_clients?: number;
  game_pid?: number;
  runtime_connected?: boolean;
  named_pipe_connected?: boolean;
  server_running?: boolean;
  last_heartbeat_at?: string;
}

export type BridgeMessageType = 'REQUEST' | 'RESPONSE' | 'EVENT' | 'HEARTBEAT';

export type BridgeTopic =
  | 'runtime:heartbeat'
  | 'runtime:state'
  | 'game:action_event'
  | 'spectator:hand_sync'
  | 'spectator:action'
  | 'overlay:toggle'
  | 'config:sync';

export interface BridgeEnvelope<T = any> {
  message_id: string;
  type: BridgeMessageType;
  topic: BridgeTopic | string;
  payload: T;
  timestamp_ms: number;
}

// ==========================================
// Match Center Analytics Types
// ==========================================

export interface MatchRecord {
  match_id: string;
  mode: string;
  map_name: string;
  duration_seconds: number;
  placement: number;
  character_used: string;
  final_stars: number;
  total_damage_dealt: number;
  total_cards_played: number;
  played_at: string;
}

export interface MatchPlayerDetail {
  player_name: string;
  character_name: string;
  placement: number;
  stars: number;
  coins: number;
  damage_dealt: number;
  mvp_badge?: string;
}

export interface MatchDetail {
  match: MatchRecord;
  players: MatchPlayerDetail[];
  round_events: Array<{ round: number; description: string; timestamp_ms: number }>;
}

export interface MatchAnalyticsSummary {
  total_matches: number;
  win_rate_pct: number;
  top2_rate_pct: number;
  favorite_character: string;
  average_placement: number;
}

// ==========================================
// Update / Package Center Types
// ==========================================

export interface RemotePackage {
  id: string;
  name: string;
  latest_version: string;
  category: PackageCategory;
  publisher: string;
  description: string;
  download_url: string;
  sha256: string;
  size_bytes: number;
  downloads_count: number;
  updated_at: string;
  installed_version?: string;
  has_update: boolean;
}

// ==========================================
// Process Detection & Launcher Types
// ==========================================

export interface GameProcessStatus {
  isRunning: boolean;
  pid?: number | null;
  processName: string;
  platformId: string;
}

export interface GameLaunchResult {
  success: boolean;
  uri: string;
  alreadyRunning: boolean;
  message: string;
}


