import assert from 'node:assert/strict';
import { summarizeMainGameRuntime } from '../pages/spectator/spectatorRuntimeSummary.ts';
import type { ResolvedRuntimeSession, RuntimeSession } from '../types/runtimeSession.ts';

function makeRawSession(overrides: Partial<RuntimeSession> = {}): RuntimeSession {
  return {
    connectionId: 'conn-1',
    platformId: 'STEAM_CN',
    processId: 1234,
    playerId: null,
    livePlayerId: null,
    connectionState: 'CONNECTED',
    ...overrides,
  };
}

function makeResolved(session: RuntimeSession): ResolvedRuntimeSession {
  return {
    connectionId: session.connectionId,
    platformId: session.platformId,
    processId: session.processId,
    playerId: session.playerId,
    displayName: null,
    role: 'MAIN',
    resolvedRole: 'MAIN',
    matchedBinding: null,
    isSpectatorExecutionRuntime: false,
    status: 'CONNECTED',
    roleLabelZh: '主账号',
    roleLabelEn: 'Main',
    session,
  };
}

const emptySummary = summarizeMainGameRuntime(null, [], null, true);
assert.equal(emptySummary.connected, false);
assert.equal(emptySummary.badgeText, '未连接');

const unboundRaw = makeRawSession({ platformId: 'STEAM_CN', livePlayerId: 1556514, playerId: 1556514 });
const unboundSummary = summarizeMainGameRuntime(null, [unboundRaw], null, true);
assert.equal(unboundSummary.connected, true);
assert.equal(unboundSummary.bound, false);
assert.equal(unboundSummary.badgeText, 'Steam 国服 · 1556514 (未绑定)');

const spectatorRaw = makeRawSession({ connectionId: 'spec-1', platformId: 'TAPTAP', playerId: 3045259 });
const spectatorResolved = { ...makeResolved(spectatorRaw), isSpectatorExecutionRuntime: true };
const onlySpectator = summarizeMainGameRuntime(null, [spectatorRaw], spectatorResolved, true);
assert.equal(onlySpectator.connected, false);

const boundMain = makeResolved(unboundRaw);
const boundSummary = summarizeMainGameRuntime(boundMain, [unboundRaw], null, true);
assert.equal(boundSummary.connected, true);
assert.equal(boundSummary.bound, true);
assert.equal(boundSummary.badgeText, 'Steam 国服 · 1556514');

console.log('spectator_runtime_summary tests passed');
