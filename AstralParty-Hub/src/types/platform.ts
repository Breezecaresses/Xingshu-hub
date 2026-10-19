/**
 * Platform Identifier enum / union type for XingShu platform profile schema.
 */
export type PlatformId =
  | 'STEAM_CN'
  | 'STEAM_GLOBAL'
  | 'TAPTAP'
  | 'BILIBILI'
  | 'WEGAME';

export const PlatformId = {
  STEAM_CN: 'STEAM_CN' as PlatformId,
  STEAM_GLOBAL: 'STEAM_GLOBAL' as PlatformId,
  TAPTAP: 'TAPTAP' as PlatformId,
  BILIBILI: 'BILIBILI' as PlatformId,
  WEGAME: 'WEGAME' as PlatformId,
} as const;

/**
 * Distribution channel codes.
 */
export type PlatformChannel =
  | 'CN_STEAM'
  | 'INT_STEAM'
  | 'CN_TAPTAP'
  | 'CN_BILIBILI'
  | 'CN_WEGAME';

export const PlatformChannel = {
  CN_STEAM: 'CN_STEAM' as PlatformChannel,
  INT_STEAM: 'INT_STEAM' as PlatformChannel,
  CN_TAPTAP: 'CN_TAPTAP' as PlatformChannel,
  CN_BILIBILI: 'CN_BILIBILI' as PlatformChannel,
  CN_WEGAME: 'CN_WEGAME' as PlatformChannel,
} as const;

/**
 * Geographic release region.
 */
export type PlatformRegion = 'CN' | 'GLOBAL';

export const PlatformRegion = {
  CN: 'CN' as PlatformRegion,
  GLOBAL: 'GLOBAL' as PlatformRegion,
} as const;

/**
 * Audit compatibility status.
 */
export type CompatibilityStatus =
  | 'VERIFIED'
  | 'PARTIALLY_AUDITED'
  | 'NOT_YET_AUDITED'
  | 'INCOMPATIBLE';

export const CompatibilityStatus = {
  VERIFIED: 'VERIFIED' as CompatibilityStatus,
  PARTIALLY_AUDITED: 'PARTIALLY_AUDITED' as CompatibilityStatus,
  NOT_YET_AUDITED: 'NOT_YET_AUDITED' as CompatibilityStatus,
  INCOMPATIBLE: 'INCOMPATIBLE' as CompatibilityStatus,
} as const;

/**
 * Formal PlatformProfile interface containing the 17 fields.
 */
export interface PlatformProfile {
  /** 1. Platform identifier */
  platformId: PlatformId;
  /** 2. Distribution channel code */
  channel: PlatformChannel;
  /** 3. Geographic region */
  region: PlatformRegion;
  /** Steam Application ID */
  steamAppId?: number | null;
  /** 4. Game version string */
  gameVersion: string | null;
  /** 5. Game build identifier */
  gameBuild: string | null;
  /** 6. Executable filename */
  processExecutable: string | null;
  /** 7. Installation root path if detected / configured */
  installRoot: string | null;
  /** 8. Path to primary runtime DLL */
  gameRuntimeDllPath: string | null;
  /** 9. SHA-256 hash of original GameAssembly.dll */
  gameRuntimeDllSha256: string | null;
  /** 10. SHA-256 hash of injected XingShu runtime DLL */
  xingshuRuntimeDllSha256: string | null;
  /** 11. Relative path to container asset bundle */
  bundleRelativePath: string | null;
  /** 12. Asset bundle GUID / Addressables 32-hex identifier */
  bundleId: string | null;
  /** 13. SHA-256 hash of bundle container */
  containerSha256: string | null;
  /** 14. Injected TextAsset PathID in Unity bundle */
  hookTextAssetPathId: number | null;
  /** 15. Signatures of injected hook routines */
  hookSignatures: string[];
  /** 16. Audit compatibility status */
  compatibilityStatus: CompatibilityStatus;
  /** 17. ISO-8601 timestamp of last successful verification */
  lastVerifiedAt: string | null;
}

import rawPlatformProfiles from '../../src-tauri/src/xing_shu/platform/platform_profiles.json' with { type: 'json' };

/**
 * Default official platform profiles matching platform_profiles.json.
 */
export const DEFAULT_PLATFORM_PROFILES: readonly PlatformProfile[] = rawPlatformProfiles as unknown as PlatformProfile[];


/**
 * Retrieves a default profile by PlatformId.
 */
export function getDefaultPlatformProfile(id: PlatformId): PlatformProfile | undefined {
  return DEFAULT_PLATFORM_PROFILES.find((p) => p.platformId === id);
}

export const getPlatformProfile = getDefaultPlatformProfile;

/**
 * Helper to check for disallowed placeholder values.
 */
function checkPlaceholder(val: string | null | undefined, name: string): { valid: boolean; error?: string } {
  if (val == null) return { valid: true };
  const trimmed = val.trim();
  if (trimmed.length === 0) {
    return { valid: false, error: `${name} cannot be empty or whitespace if present` };
  }
  const lower = trimmed.toLowerCase();
  if (['todo', 'placeholder', 'fake', 'n/a', 'null', 'undefined'].includes(lower)) {
    return { valid: false, error: `${name} contains prohibited placeholder value: '${val}'` };
  }
  return { valid: true };
}

/**
 * Validates a PlatformProfile against schema constraints.
 * Note: Baseline governance (only STEAM_CN is VERIFIED) is decoupled into `verifyHs4ABaselineGovernance`.
 */
export function validatePlatformProfile(profile: PlatformProfile): { valid: boolean; error?: string } {
  // 1. Fail-closed platformId validation
  const validPlatformIds: PlatformId[] = ['STEAM_CN', 'STEAM_GLOBAL', 'TAPTAP', 'BILIBILI', 'WEGAME'];
  if (!profile.platformId || !validPlatformIds.includes(profile.platformId)) {
    return { valid: false, error: `Invalid or unknown platformId: '${profile.platformId}'` };
  }

  // 2. Compatibility status validation
  const validStatuses: CompatibilityStatus[] = ['VERIFIED', 'PARTIALLY_AUDITED', 'NOT_YET_AUDITED', 'INCOMPATIBLE'];
  if (!profile.compatibilityStatus || !validStatuses.includes(profile.compatibilityStatus)) {
    return { valid: false, error: `Invalid or unknown compatibilityStatus: '${profile.compatibilityStatus}'` };
  }

  // 3. Channel and region consistency
  switch (profile.platformId) {
    case 'STEAM_CN':
      if (profile.channel !== 'CN_STEAM') return { valid: false, error: 'STEAM_CN must have channel CN_STEAM' };
      if (profile.region !== 'CN') return { valid: false, error: 'STEAM_CN must have region CN' };
      break;
    case 'STEAM_GLOBAL':
      if (profile.channel !== 'INT_STEAM') return { valid: false, error: 'STEAM_GLOBAL must have channel INT_STEAM' };
      if (profile.region !== 'GLOBAL') return { valid: false, error: 'STEAM_GLOBAL must have region GLOBAL' };
      break;
    case 'TAPTAP':
      if (profile.channel !== 'CN_TAPTAP') return { valid: false, error: 'TAPTAP must have channel CN_TAPTAP' };
      if (profile.region !== 'CN') return { valid: false, error: 'TAPTAP must have region CN' };
      break;
    case 'BILIBILI':
      if (profile.channel !== 'CN_BILIBILI') return { valid: false, error: 'BILIBILI must have channel CN_BILIBILI' };
      if (profile.region !== 'CN') return { valid: false, error: 'BILIBILI must have region CN' };
      break;
    case 'WEGAME':
      if (profile.channel !== 'CN_WEGAME') return { valid: false, error: 'WEGAME must have channel CN_WEGAME' };
      if (profile.region !== 'CN') return { valid: false, error: 'WEGAME must have region CN' };
      break;
  }

  // 4. Prohibit placeholder / empty strings on present fields
  for (const [key, val] of [
    ['gameVersion', profile.gameVersion],
    ['gameBuild', profile.gameBuild],
    ['processExecutable', profile.processExecutable],
    ['installRoot', profile.installRoot],
    ['gameRuntimeDllPath', profile.gameRuntimeDllPath],
    ['bundleRelativePath', profile.bundleRelativePath],
  ] as const) {
    const res = checkPlaceholder(val, key);
    if (!res.valid) return res;
  }

  // 5. Addressables 32-char hexadecimal bundle identifier validation
  const hex32Regex = /^[0-9a-fA-F]{32}$/;
  if (profile.bundleId) {
    if (!hex32Regex.test(profile.bundleId)) {
      return {
        valid: false,
        error: `bundleId '${profile.bundleId}' is not a valid 32-character hex Addressables bundle identifier`,
      };
    }
  }

  // 6. SHA-256 format checks if present
  const hex64Regex = /^[0-9a-fA-F]{64}$/;
  if (profile.gameRuntimeDllSha256 && !hex64Regex.test(profile.gameRuntimeDllSha256)) {
    return { valid: false, error: 'gameRuntimeDllSha256 must be a 64-character hex string' };
  }
  if (profile.xingshuRuntimeDllSha256 && !hex64Regex.test(profile.xingshuRuntimeDllSha256)) {
    return { valid: false, error: 'xingshuRuntimeDllSha256 must be a 64-character hex string' };
  }
  if (profile.containerSha256 && !hex64Regex.test(profile.containerSha256)) {
    return { valid: false, error: 'containerSha256 must be a 64-character hex string' };
  }

  // 7. ISO-8601 timestamp check if present
  if (profile.lastVerifiedAt) {
    if (isNaN(Date.parse(profile.lastVerifiedAt))) {
      return { valid: false, error: `lastVerifiedAt '${profile.lastVerifiedAt}' is not a valid ISO-8601 string` };
    }
  }

  // 8. VERIFIED status integrity requirements
  if (profile.compatibilityStatus === 'VERIFIED') {
    if (!profile.installRoot?.trim()) return { valid: false, error: 'installRoot must be specified for VERIFIED profile' };
    if (!profile.gameVersion?.trim()) return { valid: false, error: 'gameVersion must be specified for VERIFIED profile' };
    if (!profile.gameBuild?.trim()) return { valid: false, error: 'gameBuild must be specified for VERIFIED profile' };
    if (!profile.processExecutable?.trim()) return { valid: false, error: 'processExecutable must be specified for VERIFIED profile' };
    if (!profile.gameRuntimeDllPath?.trim()) return { valid: false, error: 'gameRuntimeDllPath must be specified for VERIFIED profile' };
    if (!profile.gameRuntimeDllSha256) return { valid: false, error: 'gameRuntimeDllSha256 must be specified for VERIFIED profile' };
    if (!profile.xingshuRuntimeDllSha256) return { valid: false, error: 'xingshuRuntimeDllSha256 must be specified for VERIFIED profile' };
    if (!profile.bundleRelativePath?.trim()) return { valid: false, error: 'bundleRelativePath must be specified for VERIFIED profile' };
    if (!profile.bundleId) return { valid: false, error: 'bundleId must be specified for VERIFIED profile' };
    if (!profile.containerSha256) return { valid: false, error: 'containerSha256 must be specified for VERIFIED profile' };
    if (profile.hookTextAssetPathId == null) return { valid: false, error: 'hookTextAssetPathId must be set for VERIFIED profile' };
    if (!profile.hookSignatures || profile.hookSignatures.length === 0) return { valid: false, error: 'hookSignatures cannot be empty for VERIFIED profile' };
    if (!profile.lastVerifiedAt?.trim()) return { valid: false, error: 'lastVerifiedAt must be specified for VERIFIED profile' };
  }

  return { valid: true };
}

/**
 * Enforces HS4-A baseline governance: in the current baseline, ONLY STEAM_CN is allowed to be VERIFIED,
 * and all other profiles must NOT be VERIFIED.
 */
export function verifyHs4ABaselineGovernance(profiles: readonly PlatformProfile[]): { valid: boolean; error?: string } {
  for (const profile of profiles) {
    if (profile.compatibilityStatus === 'VERIFIED' && profile.platformId !== 'STEAM_CN') {
      return {
        valid: false,
        error: `HS4-A baseline governance violation: platform '${profile.platformId}' cannot be marked VERIFIED in current baseline; only STEAM_CN is permitted`,
      };
    }
  }
  return { valid: true };
}
