import {
  PlatformId,
  type PlatformProfile,
  DEFAULT_PLATFORM_PROFILES,
  getDefaultPlatformProfile,
  validatePlatformProfile,
  verifyHs4ABaselineGovernance,
} from '../types/platform.ts';
import rawPlatformProfiles from '../../src-tauri/src/xing_shu/platform/platform_profiles.json' with { type: 'json' };

declare const process: { exit(code?: number): never };

let passCount = 0;
let failCount = 0;

function assert(condition: boolean, scenario: string, detail?: string): void {
  if (condition) {
    console.log(`[PASS] ${scenario}`);
    passCount++;
  } else {
    console.error(`[FAIL] ${scenario}: ${detail ?? 'assertion failed'}`);
    failCount++;
    throw new Error(`Assertion failed in ${scenario}: ${detail ?? ''}`);
  }
}

console.log('================================================================================');
console.log('PROJECT XINGSHU HS4-A.2: TYPESCRIPT PLATFORMPROFILE CONFORMANCE TEST SUITE');
console.log('================================================================================');

try {
  // Scenario 1: Resource 026 selects the new cache without inheriting previous runtime verification
  {
    const p = getDefaultPlatformProfile(PlatformId.STEAM_CN);
    assert(p !== undefined, 'Scenario 1: STEAM_CN profile exists');
    if (!p) throw new Error('Unreachable');

    assert(p.platformId === 'STEAM_CN', 'Scenario 1: platformId === STEAM_CN');
    assert(p.channel === 'CN_STEAM', 'Scenario 1: channel === CN_STEAM');
    assert(p.region === 'CN', 'Scenario 1: region === CN');
    assert(p.steamAppId === 2622000, 'Scenario 1: steamAppId === 2622000');
    assert(p.gameVersion === '3.3.0', 'Scenario 1: gameVersion === 3.3.0');
    assert(p.gameBuild === '3.3.0', 'Scenario 1: gameBuild === 3.3.0');
    assert(p.processExecutable === 'AstralParty_CN.exe', 'Scenario 1: processExecutable === AstralParty_CN.exe');
    assert(p.installRoot !== null && p.installRoot.length > 0, 'Scenario 1: installRoot is set');
    assert(p.gameRuntimeDllPath === 'GameAssembly.dll', 'Scenario 1: gameRuntimeDllPath === GameAssembly.dll');
    assert(
      p.gameRuntimeDllSha256 === '6279B4A1814E9B0DEF32779EBC2DFD5810373F112F0B12EE02F5C97CAA4F46FA',
      'Scenario 1: gameRuntimeDllSha256 matches frozen hash'
    );
    assert(
      p.xingshuRuntimeDllSha256 === null,
      'Scenario 1: old runtime hash cleared'
    );
    assert(p.bundleRelativePath !== null && p.bundleRelativePath.length > 0, 'Scenario 1: bundleRelativePath is set');
    assert(p.bundleId === '3486f99276eba77677d4880fd8d32918', 'Scenario 1: bundleId matches real 32-hex hash');
    assert(
      p.containerSha256 === '420FBB1DEE1CEFE7298213EBF5F292B4722D0CB08A2A5E3F96EE94AB4AABDDEF',
      'Scenario 1: containerSha256 matches baseline'
    );
    assert(p.hookTextAssetPathId === 99992000, 'Scenario 1: hookTextAssetPathId === 99992000');
    assert(p.hookSignatures !== null && p.hookSignatures.length === 4, 'Scenario 1: hookSignatures has 4 signatures');
    assert(p.compatibilityStatus === 'PARTIALLY_AUDITED', 'Scenario 1: updated runtime awaits live validation');
    assert(p.lastVerifiedAt === null, 'Scenario 1: old validation timestamp cleared');

    const validationResult = validatePlatformProfile(p);
    assert(validationResult.valid, 'Scenario 1: STEAM_CN passes validatePlatformProfile', validationResult.error);
  }

  // Scenario 2: VERIFIED missing hash -> reject: set gameRuntimeDllSha256 = null on VERIFIED profile, assert error
  {
    const p: PlatformProfile = {
      ...getDefaultPlatformProfile(PlatformId.STEAM_CN)!,
      compatibilityStatus: 'VERIFIED',
      gameRuntimeDllSha256: null,
    };
    const res = validatePlatformProfile(p);
    assert(!res.valid, 'Scenario 2: Setting gameRuntimeDllSha256 to null on VERIFIED profile must fail');
    assert(
      res.error?.includes('gameRuntimeDllSha256') ?? false,
      'Scenario 2: Error message must specify gameRuntimeDllSha256',
      res.error
    );
  }

  // Scenario 3: NOT_YET_AUDITED null unknown fields -> PASS: Bilibili / WeGame with null fields pass validation
  {
    const unverifiedIds: PlatformId[] = [PlatformId.BILIBILI, PlatformId.WEGAME];
    for (const id of unverifiedIds) {
      const p = getDefaultPlatformProfile(id);
      assert(p !== undefined, `Scenario 3: ${id} profile exists`);
      if (!p) throw new Error('Unreachable');

      assert(p.compatibilityStatus === 'NOT_YET_AUDITED', `Scenario 3: ${id} status === NOT_YET_AUDITED`);
      assert(p.steamAppId === null, `Scenario 3: ${id} steamAppId is null`);
      assert(p.installRoot === null, `Scenario 3: ${id} installRoot is null`);
      assert(p.gameRuntimeDllSha256 === null, `Scenario 3: ${id} gameRuntimeDllSha256 is null`);
      assert(p.xingshuRuntimeDllSha256 === null, `Scenario 3: ${id} xingshuRuntimeDllSha256 is null`);
      assert(p.bundleRelativePath === null, `Scenario 3: ${id} bundleRelativePath is null`);
      assert(p.bundleId === null, `Scenario 3: ${id} bundleId is null`);
      assert(p.containerSha256 === null, `Scenario 3: ${id} containerSha256 is null`);
      assert(p.hookTextAssetPathId === null, `Scenario 3: ${id} hookTextAssetPathId is null`);
      assert(p.hookSignatures === null || p.hookSignatures.length === 0, `Scenario 3: ${id} hookSignatures is empty`);
      assert(p.lastVerifiedAt === null, `Scenario 3: ${id} lastVerifiedAt is null`);

      const res = validatePlatformProfile(p);
      assert(res.valid, `Scenario 3: ${id} with null unknown fields passes validation`, res.error);
    }
  }

  // Scenario 4: PARTIALLY_AUDITED partial fields -> PASS: Steam Global & TapTap with audited fields pass validation
  {
    const p = getDefaultPlatformProfile(PlatformId.STEAM_GLOBAL);
    assert(p !== undefined, 'Scenario 4: STEAM_GLOBAL profile exists');
    if (!p) throw new Error('Unreachable');

    assert(p.compatibilityStatus === 'PARTIALLY_AUDITED', 'Scenario 4: status === PARTIALLY_AUDITED');
    assert(p.steamAppId === 2622000, 'Scenario 4: steamAppId === 2622000');
    assert(p.installRoot !== null && p.installRoot.length > 0, 'Scenario 4: installRoot is set for STEAM_GLOBAL');
    assert(
      p.gameRuntimeDllSha256 === '27A1215326741786D41E82A4335669727A3A6F6283EF2C025BF2C0A97002D7E2',
      'Scenario 4: gameRuntimeDllSha256 matches verified Steam Global hash'
    );
    assert(p.bundleId === '8cc684d4b3b979e86b5788b7f1814132', 'Scenario 4: bundleId is 8cc684d4b3b979e86b5788b7f1814132');
    assert(
      p.bundleRelativePath ===
        'AstralParty_INT_Data\\com.unity.addressables\\AssetBundles\\57a198703b66364d5420d39c0d251747\\8cc684d4b3b979e86b5788b7f1814132\\__data',
      'Scenario 4: bundleRelativePath matches official cache'
    );
    assert(
      p.containerSha256 === '20846D23A75E2F6CE78AE2FFFB661AA4CB02364C45AA27D99FA85FD4F6F3C103',
      'Scenario 4: containerSha256 matches official cache'
    );
    assert(p.hookTextAssetPathId === 99992000, 'Scenario 4: hookTextAssetPathId is 99992000');
    assert(p.hookSignatures !== null && p.hookSignatures.length === 4, 'Scenario 4: hookSignatures has 4 signatures');
    assert(p.xingshuRuntimeDllSha256 === null, 'Scenario 4: xingshuRuntimeDllSha256 is null');
    assert(p.lastVerifiedAt === null, 'Scenario 4: lastVerifiedAt is null');

    const res = validatePlatformProfile(p);
    assert(res.valid, 'Scenario 4: STEAM_GLOBAL with partial fields passes validation', res.error);

    // TapTap PARTIALLY_AUDITED validation
    const pt = getDefaultPlatformProfile(PlatformId.TAPTAP);
    assert(pt !== undefined, 'Scenario 4: TAPTAP profile exists');
    if (!pt) throw new Error('Unreachable');

    assert(pt.compatibilityStatus === 'PARTIALLY_AUDITED', 'Scenario 4: TAPTAP status === PARTIALLY_AUDITED');
    assert(pt.steamAppId === null, 'Scenario 4: TAPTAP steamAppId === null');
    assert(pt.installRoot === 'F:\\TapTap\\PC Games\\524022\\CN_TAPTAP_V3.3.0', 'Scenario 4: TAPTAP installRoot matches');
    assert(pt.processExecutable === '吉星派对.exe', 'Scenario 4: TAPTAP processExecutable === 吉星派对.exe');
    assert(pt.gameRuntimeDllSha256 === 'BAE3828CEF31104E4B77856115E7106FB8539FCC0A0A6098B57A34E9354044E6', 'Scenario 4: TAPTAP gameRuntimeDllSha256 matches');
    assert(pt.bundleId === '71c9197f5dd65c9b30bdf1e716719991', 'Scenario 4: TAPTAP bundleId is 71c9197f5dd65c9b30bdf1e716719991');
    assert(pt.containerSha256 === 'EA45C939DBC86ACA6950AC757FCABB773A86F9D14FEF6B3D8ED7C49F47D33598', 'Scenario 4: TAPTAP containerSha256 matches candidate');
    assert(pt.hookTextAssetPathId === 99992000, 'Scenario 4: TAPTAP hookTextAssetPathId is 99992000');
    assert(pt.hookSignatures !== null && pt.hookSignatures.length === 4, 'Scenario 4: TAPTAP hookSignatures has 4 signatures');
    const resTap = validatePlatformProfile(pt);
    assert(resTap.valid, 'Scenario 4: TAPTAP with partially audited fields passes validation', resTap.error);
  }

  // Scenario 5: empty PlatformId -> reject: "" fails closed
  {
    const p = {
      ...getDefaultPlatformProfile(PlatformId.STEAM_CN)!,
      platformId: '' as unknown as PlatformId,
    };
    const res = validatePlatformProfile(p);
    assert(!res.valid, 'Scenario 5: Empty platformId fails validation closed');
    assert(
      res.error?.includes('Invalid or unknown platformId') ?? false,
      'Scenario 5: Error specifies invalid platformId',
      res.error
    );
  }

  // Scenario 6: unknown PlatformId -> reject: "EPIC_GAMES" fails closed
  {
    const p = {
      ...getDefaultPlatformProfile(PlatformId.STEAM_CN)!,
      platformId: 'EPIC_GAMES' as unknown as PlatformId,
      compatibilityStatus: 'NOT_YET_AUDITED' as const,
    };
    const res = validatePlatformProfile(p);
    assert(!res.valid, 'Scenario 6: Unknown platformId EPIC_GAMES fails validation closed');
    assert(
      res.error?.includes('Invalid or unknown platformId') ?? false,
      'Scenario 6: Error specifies unknown platformId',
      res.error
    );
  }

  // Scenario 7: real 32-hex bundleId -> PASS: "6cc68f014947b66dea74130802768a37" passes
  {
    const p: PlatformProfile = {
      ...getDefaultPlatformProfile(PlatformId.STEAM_CN)!,
      bundleId: '6cc68f014947b66dea74130802768a37',
    };
    const res = validatePlatformProfile(p);
    assert(res.valid, 'Scenario 7: Real 32-hex bundleId passes validation', res.error);
  }

  // Scenario 8: fake UUID placeholder no longer needed: 32-hex accepted, non-32-hex rejected
  {
    const base = getDefaultPlatformProfile(PlatformId.STEAM_CN)!;

    // 32-hex accepted
    const p1: PlatformProfile = { ...base, bundleId: '6cc68f014947b66dea74130802768a37' };
    assert(validatePlatformProfile(p1).valid, 'Scenario 8: 32-hex accepted');

    // Fake UUID placeholder with hyphens (length 36) rejected
    const p2: PlatformProfile = { ...base, bundleId: 'c2a8f8e1-5b74-4b57-9d7a-18f4a9b6c001' };
    const res2 = validatePlatformProfile(p2);
    assert(!res2.valid, 'Scenario 8: Fake hyphenated UUID rejected');
    assert(res2.error?.includes('32-character hex') ?? false, 'Scenario 8: Error mentions 32-character hex', res2.error);

    // Arbitrary non-32-hex string rejected
    const p3: PlatformProfile = { ...base, bundleId: 'not-a-valid-hex-bundle-id' };
    assert(!validatePlatformProfile(p3).valid, 'Scenario 8: Arbitrary non-32-hex string rejected');

    // Short hex rejected
    const p4: PlatformProfile = { ...base, bundleId: '6cc68f014947' };
    assert(!validatePlatformProfile(p4).valid, 'Scenario 8: Short hex rejected');
  }

  // Scenario 9: Bilibili process = 吉星派对.exe (assert profile processExecutable === "吉星派对.exe")
  {
    const p = getDefaultPlatformProfile(PlatformId.BILIBILI);
    assert(p !== undefined, 'Scenario 9: BILIBILI profile exists');
    assert(
      p?.processExecutable === '吉星派对.exe',
      'Scenario 9: Bilibili process === 吉星派对.exe',
      p?.processExecutable ?? 'null'
    );
  }

  // Scenario 10: WeGame process = 吉星派对.exe (assert profile processExecutable === "吉星派对.exe")
  {
    const p = getDefaultPlatformProfile(PlatformId.WEGAME);
    assert(p !== undefined, 'Scenario 10: WEGAME profile exists');
    assert(
      p?.processExecutable === '吉星派对.exe',
      'Scenario 10: WeGame process === 吉星派对.exe',
      p?.processExecutable ?? 'null'
    );
  }

  // Scenario 11: STEAM_GLOBAL = PARTIALLY_AUDITED (assert compatibilityStatus === "PARTIALLY_AUDITED")
  {
    const p = getDefaultPlatformProfile(PlatformId.STEAM_GLOBAL);
    assert(p !== undefined, 'Scenario 11: STEAM_GLOBAL profile exists');
    assert(
      p?.compatibilityStatus === 'PARTIALLY_AUDITED',
      'Scenario 11: STEAM_GLOBAL compatibilityStatus === PARTIALLY_AUDITED',
      p?.compatibilityStatus
    );
  }

  // Scenario 12: Current baseline governance: verifyHs4ABaselineGovernance PASS
  // (only STEAM_CN is VERIFIED; if any other is promoted without audit, governance fails closed)
  {
    // Default profiles satisfy governance
    const govResult = verifyHs4ABaselineGovernance(DEFAULT_PLATFORM_PROFILES);
    assert(govResult.valid, 'Scenario 12: Default profiles pass HS4-A baseline governance', govResult.error);

    // Raw platform profiles json matches length 5
    assert(rawPlatformProfiles.length === 5, 'Scenario 12: platform_profiles.json has exactly 5 profiles');
    assert(DEFAULT_PLATFORM_PROFILES.length === 5, 'Scenario 12: DEFAULT_PLATFORM_PROFILES has exactly 5 profiles');
    assert(DEFAULT_PLATFORM_PROFILES[0].platformId === 'STEAM_CN', 'Scenario 12: Profile 0 is STEAM_CN');
    assert(DEFAULT_PLATFORM_PROFILES[1].platformId === 'STEAM_GLOBAL', 'Scenario 12: Profile 1 is STEAM_GLOBAL');
    assert(DEFAULT_PLATFORM_PROFILES[2].platformId === 'TAPTAP', 'Scenario 12: Profile 2 is TAPTAP');
    assert(DEFAULT_PLATFORM_PROFILES[3].platformId === 'BILIBILI', 'Scenario 12: Profile 3 is BILIBILI');
    assert(DEFAULT_PLATFORM_PROFILES[4].platformId === 'WEGAME', 'Scenario 12: Profile 4 is WEGAME');

    // Promoting any other platform to VERIFIED fails closed
    const unverifiedPromotions: PlatformId[] = ['STEAM_GLOBAL', 'TAPTAP', 'BILIBILI', 'WEGAME'];
    for (const targetId of unverifiedPromotions) {
      const tampered: PlatformProfile[] = DEFAULT_PLATFORM_PROFILES.map((prof) =>
        prof.platformId === targetId ? { ...prof, compatibilityStatus: 'VERIFIED' as const } : prof
      );
      const tamperedGov = verifyHs4ABaselineGovernance(tampered);
      assert(
        !tamperedGov.valid,
        `Scenario 12: Promoting ${targetId} fails governance`,
        tamperedGov.error
      );
      assert(
        tamperedGov.error?.includes('HS4-A baseline governance violation') ?? false,
        `Scenario 12: Governance error mentions violation for ${targetId}`,
        tamperedGov.error
      );
    }
  }

  // Scenario 13: Tri-Language Decoupling Conformance Test
  // Fully verified STEAM_GLOBAL passes validatePlatformProfile, but fails verifyHs4ABaselineGovernance
  {
    const promotedGlobal: PlatformProfile = {
      platformId: 'STEAM_GLOBAL',
      channel: 'INT_STEAM',
      region: 'GLOBAL',
      steamAppId: 2622000,
      gameVersion: '3.3.0',
      gameBuild: '3.3.0',
      processExecutable: 'AstralParty.exe',
      installRoot: 'C:\\Program Files (x86)\\Steam\\steamapps\\common\\AstralParty',
      gameRuntimeDllPath: 'GameAssembly.dll',
      gameRuntimeDllSha256: '27A1215326741786D41E82A4335669727A3A6F6283EF2C025BF2C0A97002D7E2',
      xingshuRuntimeDllSha256: 'B8CC3264CC36B33DA6B28930F7854223160B39B09991F17917DEDD6A41D7927F',
      bundleRelativePath: 'AstralParty_Data\\__data',
      bundleId: '6cc68f014947b66dea74130802768a37',
      containerSha256: '105E39705202C5DC125063D2A9412FB0573E61213906C731827F14660C996C10',
      hookTextAssetPathId: 99992000,
      hookSignatures: ['APXBootstrap.EnsureRuntimeHost', 'SpectatorEventInterceptor.Initialize'],
      compatibilityStatus: 'VERIFIED',
      lastVerifiedAt: '2026-10-17T00:00:00Z',
    };

    // 1. Generic Validation MUST PASS
    const valResult = validatePlatformProfile(promotedGlobal);
    assert(valResult.valid, 'Scenario 13: Generic Validation for fully verified STEAM_GLOBAL passes', valResult.error);

    // 2. Baseline Governance (standalone list) MUST FAIL
    const govResultSolo = verifyHs4ABaselineGovernance([promotedGlobal]);
    assert(
      !govResultSolo.valid,
      'Scenario 13: Standalone verified STEAM_GLOBAL fails baseline governance',
      govResultSolo.error
    );

    // 3. Baseline Governance (in default profiles collection) MUST FAIL
    const tamperedProfiles: PlatformProfile[] = DEFAULT_PLATFORM_PROFILES.map((p) =>
      p.platformId === 'STEAM_GLOBAL' ? promotedGlobal : p
    );
    const govResult = verifyHs4ABaselineGovernance(tamperedProfiles);
    assert(
      !govResult.valid,
      'Scenario 13: Baseline governance with verified STEAM_GLOBAL fails closed',
      govResult.error
    );
  }

  console.log('================================================================================');
  console.log(`TYPESCRIPT CONFORMANCE RESULTS: ${passCount} PASSED, ${failCount} FAILED (TOTAL 13 SCENARIOS)`);
  console.log('================================================================================');
  if (failCount > 0) process.exit(1);
} catch (err: unknown) {
  console.error('\n[FATAL ERROR]:', err instanceof Error ? err.message : err);
  console.log('================================================================================');
  console.log(`TYPESCRIPT CONFORMANCE RESULTS: ${passCount} PASSED, ${failCount + 1} FAILED`);
  console.log('================================================================================');
  process.exit(1);
}
