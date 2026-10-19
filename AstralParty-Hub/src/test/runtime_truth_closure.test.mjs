import assert from 'node:assert/strict';
import { randomBytes, randomInt } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

const here = path.dirname(fileURLToPath(import.meta.url));
const sourceRoot = path.resolve(here, '..');
const sent = [];
let watchSendBarrier = null;
const modules = new Map();
// Compile the actual TSX component and services in memory, preserving their real imports.
function load(file) {
  file = path.resolve(file);
  if (modules.has(file)) return modules.get(file).exports;
  if (file === path.join(sourceRoot, 'shared/api/tauri.ts')) return { api: {
    sendWatchTarget: async (connectionId, target, expectedSpectatorPlayerId) => {
      sent.push({ connectionId, target, expectedSpectatorPlayerId });
      const requestId = `request-${sent.length}`;
      if (watchSendBarrier) await watchSendBarrier;
      return requestId;
    },
    launchGameUri: async () => { throw new Error('Unexpected client launch'); },
  } };
  const nativeRequire = createRequire(file);
  const requireSource = (specifier) => {
    if (!specifier.startsWith('.')) return nativeRequire(specifier);
    const base = path.resolve(path.dirname(file), specifier);
    const resolved = [base, `${base}.ts`, `${base}.tsx`].find(p => fs.existsSync(p) && fs.statSync(p).isFile());
    if (!resolved) throw new Error(`Cannot resolve ${specifier} from ${file}`);
    return load(resolved);
  };
  const code = ts.transpileModule(fs.readFileSync(file, 'utf8'), { compilerOptions: {
    target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS,
    jsx: ts.JsxEmit.ReactJSX, esModuleInterop: true,
  }, fileName: file }).outputText;
  const module = { exports: {} };
  modules.set(file, module);
  vm.runInThisContext(`(function(require,module,exports){${code}\n})`, { filename: file })(requireSource, module, module.exports);
  return module.exports;
}
const { AutoFollowStatusPanel } = load(path.join(sourceRoot, 'pages/spectator/components/AutoFollowStatusPanel.tsx'));
const { RuntimeSessionResolver } = load(path.join(sourceRoot, 'services/runtimeSessionResolver.ts'));
const { AutoFollowService } = load(path.join(sourceRoot, 'services/autoFollowService.ts'));
const { createInitialAutoFollowContext, processAutoFollowTransition, verifyWatchTargetSecurity } = load(path.join(sourceRoot, 'services/autoFollowStateMachine.ts'));
const resolver = new RuntimeSessionResolver([]);
function session(platformId, playerId, role) {
  return { connectionId: randomBytes(8).toString('hex'), platformId, processId: randomInt(1000, 90000),
    playerId, livePlayerId: playerId, expectedBoundPlayerId: playerId, boundRole: role, status: 'CONNECTED' };
}
function render(main, spectator, context = createInitialAutoFollowContext()) {
  return renderToStaticMarkup(React.createElement(AutoFollowStatusPanel, {
    autoFollowState: context.currentState, autoFollowContext: context,
    mainSession: main ? resolver.resolveSession(main) : null,
    spectatorSession: spectator ? resolver.resolveSession(spectator) : null,
    onLaunchSpectatorClient() {}, onRetryAutoFollow() {}, onManualJoin() {}, onResetAutoFollow() {}, isZh: false,
  }));
}
for (let i = 0; i < 24; i++) {
  const main = session('STEAM_CN', randomInt(4000000, 7000000), 'MAIN');
  const spectator = session('TAPTAP', randomInt(7000000, 9000000), 'USER_SPECTATOR');
  const roomId = randomInt(100000, 900000);
  const code = randomBytes(5).toString('hex').toUpperCase();
  main.matchContext = { connectionId: main.connectionId, platformId: main.platformId,
    processId: main.processId, localPlayerId: main.playerId, matchState: 'MAIN_IN_MATCH',
    matchKind: 'PVE', internalRoomId: roomId, spectatorCode: code, playerCount: 4,
    rosterPlayerIds: [main.playerId, ...Array.from({ length: 3 }, () => randomInt(100, 999999))],
    sequence: 1, timestamp: Date.now() };
  const html = render(main, spectator);
  assert.match(html, new RegExp(`Live PlayerId: </span><strong[^>]*>${main.playerId}</strong>`));
  assert.match(html, new RegExp(`Live PlayerId: </span><strong[^>]*>${spectator.playerId}</strong>`));
  assert.ok(html.includes(`>${code}</strong>`));
  assert.ok(html.includes(`>#${roomId}</span>`));
  const service = new AutoFollowService({ mainPlayerId: main.playerId, expectedSpectatorPlayerId: spectator.playerId });
  const before = sent.length;
  service.updateRuntimeSessions([spectator, main]);
  await new Promise(setImmediate);
  assert.equal(sent.length, before + 1);
  assert.deepEqual(sent.at(-1), { connectionId: spectator.connectionId,
    target: { roomId, internalRoomId: roomId, spectatorCode: code, watchCode: code,
      matchKind: 'PVE', playerCount: 4, rosterPlayerIds: main.matchContext.rosterPlayerIds },
    expectedSpectatorPlayerId: spectator.playerId });
  service.handleHandSnapshot(roomId + 1);
  assert.equal(service.getState(), 'AUTO_JOINING', 'Other room must not confirm spectating');
  service.handleWatchResponse({ status: 'WATCH_JOIN_REJECTED', requestId: 'stale-request' });
  assert.equal(service.getState(), 'AUTO_JOINING', 'Old response must not change current join');
  service.handleHandSnapshot(roomId);
  assert.equal(service.getState(), 'SPECTATING');
  service.updateRuntimeSessions([main, spectator]);
  await new Promise(setImmediate);
  assert.equal(sent.length, before + 1, 'Polling must not repeatedly join the same room');
  main.matchContext = { ...main.matchContext, sequence: 2, matchState: 'LOBBY', inMatch: false };
  service.updateRuntimeSessions([main, spectator]);
  assert.equal(service.getState(), 'MAIN_MATCH_ENDED');
  assert.equal(service.getContext().activeTarget, null);
  service.destroy();
}
{
  const main = session('STEAM_CN', randomInt(4000000, 7000000), 'MAIN');
  const spectator = session('TAPTAP', randomInt(7000000, 9000000), 'USER_SPECTATOR');
  const roomId = randomInt(100000, 900000);
  const code = randomBytes(5).toString('hex').toUpperCase();
  main.matchContext = { connectionId: main.connectionId, platformId: main.platformId,
    processId: main.processId, localPlayerId: main.playerId, matchState: 'MAIN_IN_MATCH',
    matchKind: 'PVE', internalRoomId: roomId, spectatorCode: code, playerCount: 4,
    rosterPlayerIds: [main.playerId], sequence: 2, timestamp: Date.now() };
  const service = new AutoFollowService({ mainPlayerId: main.playerId, expectedSpectatorPlayerId: spectator.playerId });
  let releaseSend;
  watchSendBarrier = new Promise(resolve => { releaseSend = resolve; });
  const before = sent.length;
  service.updateRuntimeSessions([main, spectator]);
  service.updateRuntimeSessions([{ ...main, matchContext: { ...main.matchContext, sequence: 1, matchState: 'LOBBY' } }, spectator]);
  service.updateRuntimeSessions([main, spectator]);
  assert.equal(sent.length, before + 1, 'Concurrent and stale polls must share one watch request');
  releaseSend();
  await new Promise(setImmediate);
  watchSendBarrier = null;
  assert.equal(service.getState(), 'AUTO_JOINING');
  await service.sendWatchTargetCommand();
  assert.equal(sent.length, before + 1, 'Same target stays deduplicated after IPC returns');
  service.handleWatchResponse({ status: 'WATCH_JOIN_REJECTED', requestId: service.getContext().lastRequestId });
  await service.retry();
  assert.equal(sent.length, before + 2, 'Explicit retry must allow the same target again');
  service.destroy();
}
const pendingMain = session('STEAM_CN', null, 'UNBOUND');
{
  const main = session('STEAM_CN', randomInt(4000000, 7000000), 'MAIN');
  const spec = session('TAPTAP', randomInt(7000000, 9000000), 'USER_SPECTATOR');
  main.matchContext = { connectionId: main.connectionId, platformId: main.platformId, processId: main.processId,
    localPlayerId: main.playerId, matchState: 'MAIN_IN_MATCH', matchKind: 'PVE', internalRoomId: randomInt(10000, 90000),
    spectatorCode: 'C140A15', playerCount: 4, rosterPlayerIds: [main.playerId], sequence: 1, timestamp: Date.now() };
  const service = new AutoFollowService({ mainPlayerId: main.playerId, expectedSpectatorPlayerId: spec.playerId });
  const before = sent.length;
  let allocations = 0;
  service.setExecutionMode('LOCAL_MANAGED', async target => {
    allocations++; assert.equal(target.watchCode, 'C140A15'); return 'managed-request';
  });
  service.updateRuntimeSessions([main, spec]); await new Promise(setImmediate);
  service.updateRuntimeSessions([main, spec]); await new Promise(setImmediate);
  assert.equal(allocations, 1); assert.equal(sent.length, before, 'Managed mode must use lease dispatcher, not direct local command');
  let remountedAllocations = 0;
  service.setExecutionMode('LOCAL_MANAGED', async () => {
    remountedAllocations++; return 'remounted-managed-request';
  });
  service.resetToIdle(); service.updateRuntimeSessions([main, spec]); await new Promise(setImmediate);
  assert.equal(remountedAllocations, 1, 'Returning to the page must replace the old hook dispatcher');
  assert.equal(allocations, 1, 'Unmounted hook must not receive new allocations');
  service.setExecutionMode('PAUSED'); service.updateRuntimeSessions([main, spec]);
  await service.launchSpectatorClient(); await service.sendWatchTargetCommand();
  assert.equal(allocations, 1); assert.equal(service.getContext().activeTarget, null);
  service.destroy();
}
pendingMain.expectedBoundPlayerId = 1556514;
const pendingSpectator = session('TAPTAP', null, 'UNBOUND');
pendingSpectator.expectedBoundPlayerId = 3045259;
const missingHtml = render(pendingMain, pendingSpectator);
assert.equal((missingHtml.match(/Live PlayerId: <\/span><strong[^>]*>(None|Pending)<\/strong>/g) || []).length, 2);
assert.ok(missingHtml.includes('>UNKNOWN</strong>'));
assert.ok(!missingHtml.includes('C05FE7B'));
assert.ok(!missingHtml.includes('#351045'));
assert.equal(verifyWatchTargetSecurity({ connectionId: 'tap', platformId: 'TAPTAP', playerId: null }).allowed, false);
const lateCode = createInitialAutoFollowContext();
const waiting = processAutoFollowTransition(lateCode, { type: 'MAIN_MATCH_DETECTED', target: { roomId: randomInt(10000, 90000) } });
assert.equal(waiting.nextState, 'MAIN_IN_MATCH');
assert.equal(waiting.actionRequired, 'NONE');
for (const file of ['pages/spectator/components/AutoFollowStatusPanel.tsx', 'pages/spectator/useSpectatorState.ts',
  'pages/spectator/SpectatorPage.tsx', 'services/autoFollowService.ts', 'services/runtimeTruth.ts']) {
  assert.doesNotMatch(fs.readFileSync(path.join(sourceRoot, file), 'utf8'), /C05FE7B|351045|1556514|3045259/);
}
console.log('PASS: 24 randomized rendered UI + auto-follow flows; pending identity; late code; stale response; room isolation; no production acceptance fallbacks.');
