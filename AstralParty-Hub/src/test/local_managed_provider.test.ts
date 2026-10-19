// @ts-ignore Node types are not part of the frontend compilation.
import assert from 'node:assert/strict';
import { LocalManagedSpectatorAccountProvider } from '../services/localManagedAccountProvider.ts';
import { DefaultManagedSpectatorAccountProvider } from '../services/managedAccountProvider.ts';
import type { ManagedAccountInfo } from '../types/spectatorAccount.ts';

const allocated: ManagedAccountInfo = { executionMode: 'LOCAL_VALIDATION', status: 'ALLOCATING',
  sessionId: 'lease-one', requestId: 'official-request', currentRoomId: 639893,
  connectionId: 'actual-worker', message: 'awaiting official snapshot' };
let cases = 0;
{
  const calls: Array<{ command: string; args: any }> = [];
  let resolve!: (v: ManagedAccountInfo) => void;
  const pending = new Promise<ManagedAccountInfo>(done => { resolve = done; });
  const provider = new LocalManagedSpectatorAccountProvider((async (command, args) => {
    calls.push({ command, args });
    return command === 'managed_local_allocate' ? pending : { ...allocated, status: 'IN_USE' };
  }) as any);
  const a = provider.allocateSession(639893, 'C140A15');
  const b = provider.allocateSession(639893, 'C140A15');
  assert.equal(a, b); assert.equal(calls.length, 1);
  resolve(allocated); assert.equal((await a).status, 'ALLOCATING');
  await provider.getStatus(); assert.equal(calls[1].args.sessionId, 'lease-one');
  await provider.releaseCurrent(); assert.equal(calls[2].command, 'managed_local_release');
  await provider.getStatus(); assert.equal(calls[3].args.sessionId, null); cases++;
}
{
  let resolve!: (v: ManagedAccountInfo) => void;
  const pending = new Promise<ManagedAccountInfo>(done => { resolve = done; });
  const released: any[] = [];
  const provider = new LocalManagedSpectatorAccountProvider((async (command, args) => {
    if (command === 'managed_local_allocate') return pending;
    released.push(args); return undefined;
  }) as any);
  const late = provider.allocateSession(639893, 'C140A15');
  await provider.releaseCurrent(); resolve(allocated);
  await assert.rejects(late, /已取消/); assert.equal(released[0].sessionId, 'lease-one'); cases++;
}
{
  const statuses = [{}, { status: 'UNKNOWN' }, { status: 'AVAILABLE', availableCapacity: 0 }];
  for (const data of statuses) {
    const provider = new DefaultManagedSpectatorAccountProvider({ serverEndpoint: 'https://pool.invalid',
      fetchFn: async () => new Response(JSON.stringify(data)) });
    assert.equal((await provider.getStatus()).status, 'ERROR'); cases++;
  }
}
{
  // @ts-ignore
  const old = process.env.XINGSHU_SPECTATOR_SERVER_URL;
  // @ts-ignore
  process.env.XINGSHU_SPECTATOR_SERVER_URL = 'https://environment.invalid';
  const provider = new DefaultManagedSpectatorAccountProvider();
  provider.setServerEndpoint(null); assert.equal(provider.getServerEndpoint(), null);
  // @ts-ignore
  if (old === undefined) delete process.env.XINGSHU_SPECTATOR_SERVER_URL;
  // @ts-ignore
  else process.env.XINGSHU_SPECTATOR_SERVER_URL = old;
  for (const endpoint of ['javascript:alert(1)', 'http://remote.invalid', 'https://user:pass@example.com', 'https://example.com/?secret=value']) {
    assert.throws(() => new DefaultManagedSpectatorAccountProvider({ serverEndpoint: endpoint }));
  }
  cases++;
}
{
  const provider = new DefaultManagedSpectatorAccountProvider({ serverEndpoint: 'https://pool.invalid', timeoutMs: 25,
    fetchFn: async () => ({ ok: true, json: () => new Promise(() => {}) }) as any });
  assert.equal((await provider.getStatus()).status, 'ERROR'); cases++;
}
{
  const provider = new DefaultManagedSpectatorAccountProvider({ serverEndpoint: 'https://pool.invalid',
    fetchFn: async () => new Response(JSON.stringify({ status: 'READY' })) });
  await assert.rejects(provider.allocateSession(639893), /真实房间号和观战码/);
  await assert.rejects(provider.allocateSession(639893, 'C140A15'), /有效且未过期/);
  await assert.rejects(provider.releaseSession('some-lease'), /未确认/); cases++;
}
console.log(`PASS: ${cases} local lease lifecycle and remote truth checks`);
