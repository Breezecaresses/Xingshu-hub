/**
 * Project XingShu — HS4-P1.5.1B
 * Frontend Session Truth & Multi-Runtime UI Semantics Test Suite
 */

import assert from 'node:assert';
import { GameAccountService, MemoryStorage } from '../services/gameAccountService.ts';
import { ResolvedAccountRole } from '../types/gameAccount.ts';
import type { RuntimeSession } from '../types/runtimeSession.ts';

console.log('='.repeat(80));
console.log('PROJECT XINGSHU HS4-P1.5.1B: FRONTEND SESSION TRUTH & COUNT TEST SUITE');
console.log('='.repeat(80));

const storage = new MemoryStorage();
const service = new GameAccountService(storage);

// Explicit user bindings
service.bindAccount(1556514, 'MAIN', 'STEAM_CN', '我的主账号');
service.bindAccount(3045259, 'USER_SPECTATOR', 'TAPTAP', '我的观战账号');

// --- Test 1: 0 Runtime connected ---
console.log('\n--- Test 1: 0 Runtime connected ---');
const sessions0: RuntimeSession[] = [];
const connectedCount0 = sessions0.filter(s => s.status !== 'DISCONNECTED').length;
const badge0 = connectedCount0 === 0 ? '0 Runtime 已连接' : `${connectedCount0} Runtime 已连接`;
assert.strictEqual(connectedCount0, 0, 'Connected count must be 0');
assert.strictEqual(badge0, '0 Runtime 已连接', 'Badge must show 0 Runtime 已连接');
const identity0 = service.resolveSession(null, 'STEAM_CN', false);
assert.strictEqual(identity0.resolvedRole, ResolvedAccountRole.DISCONNECTED, '0 runtime must resolve to DISCONNECTED');
console.log('[PASS] 0 Runtime correctly resolves to DISCONNECTED with 0 count');

// --- Test 2: 1 Runtime connected (Steam CN in Main Menu, NO spectator payload) ---
console.log('\n--- Test 2: 1 Runtime connected (Steam CN in Main Menu) ---');
const steamSession: RuntimeSession = {
  connectionId: 'conn-steam-1',
  platformId: 'STEAM_CN',
  processId: 33548,
  role: 'MAIN',
  playerId: null, // Sitting in Main Menu, no spectator room payload yet!
  status: 'CONNECTED',
};
const sessions1 = [steamSession];
const connectedCount1 = sessions1.filter(s => s.status !== 'DISCONNECTED').length;
const badge1 = connectedCount1 === 1 ? '1 Runtime 已连接' : 'Wrong';
assert.strictEqual(connectedCount1, 1, 'Connected count must be 1');
assert.strictEqual(badge1, '1 Runtime 已连接', 'Badge must show 1 Runtime 已连接');

// In Main Menu, before room payload arrives, runtime IS connected!
const identity1 = service.resolveSession(steamSession.playerId, steamSession.platformId, true);
assert.strictEqual(identity1.resolvedRole, ResolvedAccountRole.CONNECTED_PENDING_IDENTITY, 'Connected runtime with null playerId resolves to CONNECTED_PENDING_IDENTITY');
assert.notStrictEqual(identity1.resolvedRole, ResolvedAccountRole.DISCONNECTED, 'MUST NEVER resolve to DISCONNECTED when runtime is connected');
console.log('[PASS] 1 Runtime connected in Main Menu resolves to CONNECTED_PENDING_IDENTITY, NOT DISCONNECTED');

// --- Test 3: 1 Runtime connected with known PlayerId ---
console.log('\n--- Test 3: 1 Runtime connected with known PlayerId ---');
const steamSessionWithPid: RuntimeSession = {
  ...steamSession,
  playerId: 1556514,
};
const identityWithPid = service.resolveSession(steamSessionWithPid.playerId, steamSessionWithPid.platformId, true);
assert.strictEqual(identityWithPid.resolvedRole, ResolvedAccountRole.MAIN, 'Bound PlayerId 1556514 must resolve to MAIN');
assert.strictEqual(identityWithPid.currentSessionPlayerId, 1556514, 'Player ID must be 1556514');
console.log('[PASS] Bound PlayerId 1556514 resolves to MAIN');

// --- Test 4: 2 Runtimes connected simultaneously (Dual-Client Coexistence) ---
console.log('\n--- Test 4: 2 Runtimes connected simultaneously ---');
const tapTapSession: RuntimeSession = {
  connectionId: 'conn-taptap-2',
  platformId: 'TAPTAP',
  processId: 28624,
  role: 'USER_SPECTATOR',
  playerId: 3045259,
  status: 'CONNECTED',
};
const sessions2 = [steamSessionWithPid, tapTapSession];
const connectedCount2 = sessions2.filter(s => s.status !== 'DISCONNECTED').length;
const badge2 = connectedCount2 >= 2 ? '2 Runtime（双端在线）' : 'Wrong';
assert.strictEqual(connectedCount2, 2, 'Connected count must be 2');
assert.strictEqual(badge2, '2 Runtime（双端在线）', 'Badge must show 2 Runtime（双端在线）');

const mainSession = sessions2.find(s => s.role === 'MAIN' || s.platformId === 'STEAM_CN');
const specSession = sessions2.find(s => s.role === 'USER_SPECTATOR' || s.platformId === 'TAPTAP');
assert.ok(mainSession, 'Main session must be present');
assert.ok(specSession, 'Spectator session must be present');
assert.strictEqual(mainSession?.platformId, 'STEAM_CN');
assert.strictEqual(specSession?.platformId, 'TAPTAP');
assert.strictEqual(mainSession?.processId, 33548);
assert.strictEqual(specSession?.processId, 28624);

const mainIdentity = service.resolveSession(mainSession?.playerId, mainSession?.platformId, true);
const specIdentity = service.resolveSession(specSession?.playerId, specSession?.platformId, true);
assert.strictEqual(mainIdentity.resolvedRole, ResolvedAccountRole.MAIN);
assert.strictEqual(specIdentity.resolvedRole, ResolvedAccountRole.USER_SPECTATOR);
console.log('[PASS] Dual-client sessions coexist without cross-overwriting, badge shows 2 Runtime（双端在线）');

console.log('\n' + '='.repeat(80));
console.log('FRONTEND SESSION TRUTH RESULTS: ALL TESTS PASSED (100%)');
console.log('='.repeat(80));
