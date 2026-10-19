/**
 * Project XingShu — Spectator Account Type Definitions
 * Phase: HS4-P1.2
 *
 * Defines domain models and lifecycle states for spectator accounts,
 * covering both User-Owned ('USER_OWNED') and XingShu-Managed ('XINGSHU_MANAGED') sources.
 *
 * SAFETY / SECURITY GUARANTEE:
 * Prohibits storing passwords, secrets, tokens, or auth cookies.
 */

/**
 * Origin source of spectator account.
 */
export type SpectatorAccountSource = 'USER_OWNED' | 'XINGSHU_MANAGED';

export const SpectatorAccountSource = {
  USER_OWNED: 'USER_OWNED' as SpectatorAccountSource,
  XINGSHU_MANAGED: 'XINGSHU_MANAGED' as SpectatorAccountSource,
} as const;

/**
 * Fine-grained operational status for spectator accounts across lifecycle.
 */
export type SpectatorAccountStatus =
  | 'UNCONFIGURED'
  | 'AVAILABLE'
  | 'LOGIN_REQUIRED'
  | 'WAITING_RUNTIME'
  | 'IDENTITY_PENDING'
  | 'ALLOCATING'
  | 'READY'
  | 'IN_USE'
  | 'OFFLINE'
  | 'ERROR'
  | 'SERVICE_NOT_CONFIGURED'
  | 'NO_ACCOUNT_AVAILABLE'
  | 'EXPIRED';

export const SpectatorAccountStatus = {
  UNCONFIGURED: 'UNCONFIGURED' as SpectatorAccountStatus,
  AVAILABLE: 'AVAILABLE' as SpectatorAccountStatus,
  LOGIN_REQUIRED: 'LOGIN_REQUIRED' as SpectatorAccountStatus,
  WAITING_RUNTIME: 'WAITING_RUNTIME' as SpectatorAccountStatus,
  IDENTITY_PENDING: 'IDENTITY_PENDING' as SpectatorAccountStatus,
  ALLOCATING: 'ALLOCATING' as SpectatorAccountStatus,
  READY: 'READY' as SpectatorAccountStatus,
  IN_USE: 'IN_USE' as SpectatorAccountStatus,
  OFFLINE: 'OFFLINE' as SpectatorAccountStatus,
  ERROR: 'ERROR' as SpectatorAccountStatus,
  SERVICE_NOT_CONFIGURED: 'SERVICE_NOT_CONFIGURED' as SpectatorAccountStatus,
  NO_ACCOUNT_AVAILABLE: 'NO_ACCOUNT_AVAILABLE' as SpectatorAccountStatus,
  EXPIRED: 'EXPIRED' as SpectatorAccountStatus,
} as const;

/**
 * Lifecycle and health status of the Managed Spectator Account Provider.
 */
export type ManagedProviderStatus = SpectatorAccountStatus;

export const ManagedProviderStatus = SpectatorAccountStatus;

/**
 * Information model for User-Owned Spectator Accounts.
 * Zero-credential policy: only non-sensitive runtime state and public IDs are stored.
 */
export interface UserOwnedAccountInfo {
  /** Target game platform identifier, e.g. 'STEAM_CN', 'STEAM_GLOBAL' */
  platformId: string;
  /** Human-readable localized platform name, e.g. 'Steam 国服' */
  platformName: string;
  /**
   * Authoritative player ID of the current active session.
   * Strictly null when runtime disconnected or before identity packet is received in this session.
   */
  currentSessionPlayerId: number | null;
  /**
   * Alias for currentSessionPlayerId (backward compatibility).
   * STRICT INVARIANT: Always equals currentSessionPlayerId, NEVER falls back to lastKnownPlayerId.
   */
  playerId: number | null;
  /**
   * Historical last recognized player ID persisted in storage.
   * NEVER elevated to currentSessionPlayerId automatically.
   */
  lastKnownPlayerId: number | null;
  /** Local client or community display name */
  displayName: string | null;
  /** ISO-8601 timestamp when this account was last observed */
  lastSeenAt: string | null;
  /** Whether the underlying game process is currently running */
  gameRunning: boolean;
  /** Whether the XingShu runtime TCP bridge is actively connected */
  runtimeConnected: boolean;
  /** Current derived operational status */
  status: SpectatorAccountStatus;
}

/**
 * Information model for XingShu-Managed / Dedicated Cloud Spectator Accounts.
 */
export interface ManagedAccountInfo {
  executionMode?: 'LOCAL_VALIDATION' | 'REMOTE';
  connectionId?: string | null;
  requestId?: string | null;
  /** Managed account pool identifier */
  accountId?: string | null;
  /** Display name of the managed bot/service instance */
  accountName?: string | null;
  /** Current operational status in the managed account pool */
  status: ManagedProviderStatus;
  /** Server endpoint */
  serverEndpoint?: string | null;
  /** Session ID */
  sessionId?: string | null;
  /** Active session lease identifier if currently allocated */
  allocatedSessionId?: string | null;
  /** Allocated player ID */
  allocatedPlayerId?: number | null;
  /** Allocation timestamp */
  allocatedAt?: string | null;
  /** Expiration timestamp of the current allocation lease */
  expiresAt?: string | null;
  /** Active room ID if spectating */
  currentRoomId?: number | null;
  /** Human-readable status or informational message */
  message: string;
}

/**
 * Formal interface for Managed Spectator Account Providers.
 */
export interface IManagedSpectatorAccountProvider {
  readonly executionMode?: 'LOCAL_VALIDATION' | 'REMOTE';
  getStatus(): Promise<ManagedAccountInfo>;
  allocateSession(roomId?: number, watchCode?: string): Promise<ManagedAccountInfo>;
  releaseSession(sessionId: string): Promise<void>;
}

/**
 * Top-level selection and synchronization state for spectator accounts.
 */
export interface AccountSelectionState {
  /** Currently selected account source */
  selectedSource: SpectatorAccountSource;
  /** State of user's own spectator account */
  userAccount: UserOwnedAccountInfo;
  /** State of XingShu managed spectator account */
  managedAccount: ManagedAccountInfo;
}

/**
 * Metadata stored in localStorage for persisting user account preferences.
 * STRICT SECURITY INVARIANT: Zero credential, secret, token, or password storage.
 */
export interface UserOwnedAccountMetadata {
  platformId: string;
  lastKnownPlayerId: number | null;
  /** Legacy alias for lastKnownPlayerId for backward-compatibility in stored JSON */
  playerId?: number | null;
  displayName: string | null;
  lastSeenAt: string | null;
}

// -------------------------------------------------------------------------
// Remote HTTP API Contracts (Phase 2 Cloud Watcher Pool Specification)
// -------------------------------------------------------------------------

export const MANAGED_SPECTATOR_API = {
  STATUS: '/api/v1/spectator/status',
  ALLOCATE: '/api/v1/spectator/session/allocate',
  RELEASE: '/api/v1/spectator/session/release',
} as const;

export interface ManagedSessionAllocateRequest {
  roomId?: number;
  watchCode?: string;
  clientVersion?: string;
  requestId?: string;
}

export interface ManagedSessionAllocateResponse {
  sessionId: string;
  allocatedPlayerId: number;
  allocatedAt: string;
  expiresAt: string;
  status: ManagedProviderStatus;
  message?: string;
}

export interface ManagedSessionReleaseRequest {
  sessionId: string;
}

export interface ManagedSessionReleaseResponse {
  success: boolean;
  message?: string;
}

export interface ManagedServerStatusResponse {
  status: ManagedProviderStatus;
  version?: string;
  serverEndpoint?: string;
  availableCapacity?: number;
  activeSessions?: number;
  message: string;
}

// -------------------------------------------------------------------------
// Standard Informational Messages and Fallbacks
// -------------------------------------------------------------------------

export const OFFICIAL_WATCHER_UNCONFIGURED_MESSAGE =
  '星枢托管观战服务尚未配置。可先开启本机托管验证，或使用【我的观战账号】。独立服务器账号池尚未上线。';

export const OFFICIAL_WATCHER_UNCONFIGURED_ERROR =
  'Official Watcher Account service is not configured.';
