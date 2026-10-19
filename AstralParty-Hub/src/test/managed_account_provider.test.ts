/**
 * Project XingShu — HS4-P1.2 Managed Spectator Account Provider Test Suite
 * Conformance, Error Handling, Fallback Guidance, and Credential Security Assertions.
 */

// @ts-ignore
import fs from 'node:fs';
// @ts-ignore
import path from 'node:path';
// @ts-ignore
import { fileURLToPath } from 'node:url';

import {
  DefaultManagedSpectatorAccountProvider,
  defaultManagedSpectatorAccountProvider,
} from '../services/managedAccountProvider.ts';
import {
  MANAGED_SPECTATOR_API,
  OFFICIAL_WATCHER_UNCONFIGURED_MESSAGE,
  OFFICIAL_WATCHER_UNCONFIGURED_ERROR,
} from '../types/spectatorAccount.ts';

declare const process: {
  exit(code?: number): never;
  env: Record<string, string | undefined>;
};

console.log('================================================================================');
console.log('PROJECT XINGSHU HS4-P1.2: MANAGED SPECTATOR ACCOUNT PROVIDER TEST SUITE');
console.log('================================================================================');

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

function assertStrictEqual<T>(actual: T, expected: T, scenario: string): void {
  if (actual === expected) {
    console.log(`[PASS] ${scenario}`);
    passCount++;
  } else {
    console.error(`[FAIL] ${scenario}: Expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
    failCount++;
    throw new Error(`Assertion failed in ${scenario}: Expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
  }
}

async function runAsyncTest(name: string, fn: () => Promise<void>): Promise<void> {
  try {
    await fn();
  } catch (err: any) {
    console.error(`[FAIL] Uncaught in ${name}:`, err.message);
    failCount++;
  }
}

// Ensure clean environment baseline
delete process.env.XINGSHU_SPECTATOR_SERVER_URL;

async function main() {
  // ---------------------------------------------------------------------------
  // Test Group 1: Default Provider Unconfigured Status & Fallback Guidance
  // ---------------------------------------------------------------------------
  console.log('\n--- Test Group 1: Unconfigured Status & Fallback Guidance ---');

  await runAsyncTest('Group 1: Default instance returns SERVICE_NOT_CONFIGURED', async () => {
    const provider = new DefaultManagedSpectatorAccountProvider();
    const info = await provider.getStatus();

    assertStrictEqual(info.status, 'SERVICE_NOT_CONFIGURED', 'Default status is SERVICE_NOT_CONFIGURED');
    assertStrictEqual(info.serverEndpoint, null, 'serverEndpoint is null');
    assertStrictEqual(info.sessionId, null, 'sessionId is null');
    assertStrictEqual(info.allocatedPlayerId, null, 'allocatedPlayerId is null');
    assertStrictEqual(info.allocatedAt, null, 'allocatedAt is null');
    assertStrictEqual(info.expiresAt, null, 'expiresAt is null');
    assertStrictEqual(
      info.message,
      OFFICIAL_WATCHER_UNCONFIGURED_MESSAGE,
      'Message matches official unconfigured notification exactly'
    );
    assert(
      info.message.includes('【我的观战账号】'),
      'Message directs user to local client mode: 【我的观战账号】'
    );
  });

  await runAsyncTest('Group 1: Singleton exported instance returns SERVICE_NOT_CONFIGURED', async () => {
    const info = await defaultManagedSpectatorAccountProvider.getStatus();
    assertStrictEqual(info.status, 'SERVICE_NOT_CONFIGURED', 'Singleton status is SERVICE_NOT_CONFIGURED');
  });

  await runAsyncTest('Group 1: allocateSession() rejects with clean explanatory error', async () => {
    const provider = new DefaultManagedSpectatorAccountProvider();
    let rejected = false;
    let errorMessage = '';

    try {
      await provider.allocateSession();
    } catch (err: any) {
      rejected = true;
      errorMessage = err.message;
    }

    assert(rejected, 'allocateSession() was rejected');
    assertStrictEqual(
      errorMessage,
      OFFICIAL_WATCHER_UNCONFIGURED_ERROR,
      'allocateSession error matches "Official Watcher Account service is not configured."'
    );
  });

  await runAsyncTest('Group 1: allocateSession(roomId) with specific room also rejects', async () => {
    const provider = new DefaultManagedSpectatorAccountProvider();
    let rejected = false;
    let errorMessage = '';

    try {
      await provider.allocateSession(665945);
    } catch (err: any) {
      rejected = true;
      errorMessage = err.message;
    }

    assert(rejected, 'allocateSession(665945) was rejected');
    assertStrictEqual(
      errorMessage,
      OFFICIAL_WATCHER_UNCONFIGURED_ERROR,
      'allocateSession(roomId) error matches official unconfigured error'
    );
  });

  await runAsyncTest('Group 1: releaseSession() rejects when unconfigured', async () => {
    const provider = new DefaultManagedSpectatorAccountProvider();
    let rejected = false;
    let errorMessage = '';

    try {
      await provider.releaseSession('dummy_session_123');
    } catch (err: any) {
      rejected = true;
      errorMessage = err.message;
    }

    assert(rejected, 'releaseSession() was rejected when unconfigured');
    assertStrictEqual(
      errorMessage,
      OFFICIAL_WATCHER_UNCONFIGURED_ERROR,
      'releaseSession error matches "Official Watcher Account service is not configured."'
    );
  });

  await runAsyncTest('Group 1: Whitespace or empty endpoint treated as unconfigured', async () => {
    const provider1 = new DefaultManagedSpectatorAccountProvider({ serverEndpoint: '   ' });
    const info1 = await provider1.getStatus();
    assertStrictEqual(info1.status, 'SERVICE_NOT_CONFIGURED', 'Whitespace endpoint resolves to SERVICE_NOT_CONFIGURED');

    const provider2 = new DefaultManagedSpectatorAccountProvider();
    provider2.setServerEndpoint('');
    const info2 = await provider2.getStatus();
    assertStrictEqual(info2.status, 'SERVICE_NOT_CONFIGURED', 'Empty endpoint resolves to SERVICE_NOT_CONFIGURED');
  });

  // ---------------------------------------------------------------------------
  // Test Group 2: Environment Variable & Endpoint Resolution
  // ---------------------------------------------------------------------------
  console.log('\n--- Test Group 2: Environment Variable & Endpoint Resolution ---');

  await runAsyncTest('Group 2: Resolves endpoint from XINGSHU_SPECTATOR_SERVER_URL', async () => {
    process.env.XINGSHU_SPECTATOR_SERVER_URL = 'https://spectator-pool.xingshu.internal';
    const provider = new DefaultManagedSpectatorAccountProvider();

    assertStrictEqual(
      provider.getServerEndpoint(),
      'https://spectator-pool.xingshu.internal',
      'Detected environment variable endpoint'
    );

    // Clean up
    delete process.env.XINGSHU_SPECTATOR_SERVER_URL;
  });

  await runAsyncTest('Group 2: Trims trailing slashes from endpoint', async () => {
    const provider = new DefaultManagedSpectatorAccountProvider({
      serverEndpoint: 'https://spectator.xingshu.internal///',
    });
    assertStrictEqual(
      provider.getServerEndpoint(),
      'https://spectator.xingshu.internal',
      'Sanitized trailing slashes'
    );
  });

  await runAsyncTest('Group 2: Constructor option takes precedence over env var', async () => {
    process.env.XINGSHU_SPECTATOR_SERVER_URL = 'https://env-endpoint.internal';
    const provider = new DefaultManagedSpectatorAccountProvider({
      serverEndpoint: 'https://custom-endpoint.internal',
    });

    assertStrictEqual(
      provider.getServerEndpoint(),
      'https://custom-endpoint.internal',
      'Constructor endpoint overrides environment variable'
    );

    // Clean up
    delete process.env.XINGSHU_SPECTATOR_SERVER_URL;
  });

  await runAsyncTest('Group 2: setServerEndpoint(null) reverts to unconfigured', async () => {
    const provider = new DefaultManagedSpectatorAccountProvider({
      serverEndpoint: 'https://custom-endpoint.internal',
    });
    assertStrictEqual(
      provider.getServerEndpoint(),
      'https://custom-endpoint.internal',
      'Initial endpoint set'
    );

    provider.setServerEndpoint(null);
    assertStrictEqual(provider.getServerEndpoint(), null, 'Reverted to null');

    const info = await provider.getStatus();
    assertStrictEqual(info.status, 'SERVICE_NOT_CONFIGURED', 'Status is unconfigured after clearing endpoint');
  });

  // ---------------------------------------------------------------------------
  // Test Group 3: HTTP API Contracts (Phase 2 Remote Service Integration)
  // ---------------------------------------------------------------------------
  console.log('\n--- Test Group 3: HTTP API Contracts ---');

  await runAsyncTest('Group 3: getStatus() contacts remote status endpoint', async () => {
    let capturedUrl = '';
    let capturedMethod = '';

    const mockFetch: typeof fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
      capturedUrl = String(input);
      capturedMethod = init?.method ?? 'GET';
      return new Response(
        JSON.stringify({
          status: 'AVAILABLE',
          version: '1.2.0',
          availableCapacity: 8,
          activeSessions: 2,
          message: 'Official watcher cluster online and healthy.',
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } }
      );
    };

    const provider = new DefaultManagedSpectatorAccountProvider({
      serverEndpoint: 'https://watcher.xingshu.internal',
      fetchFn: mockFetch,
    });

    const info = await provider.getStatus();

    assertStrictEqual(
      capturedUrl,
      `https://watcher.xingshu.internal${MANAGED_SPECTATOR_API.STATUS}`,
      'Called correct status URL'
    );
    assertStrictEqual(capturedMethod, 'GET', 'Used GET method');
    assertStrictEqual(info.status, 'AVAILABLE', 'Status is AVAILABLE');
    assertStrictEqual(info.message, 'Official watcher cluster online and healthy.', 'Mapped status message');
  });

  await runAsyncTest('Group 3: getStatus() handles remote error gracefully', async () => {
    const mockFetch: typeof fetch = async () => {
      return new Response('Internal Server Error', {
        status: 500,
        statusText: 'Internal Server Error',
      });
    };

    const provider = new DefaultManagedSpectatorAccountProvider({
      serverEndpoint: 'https://watcher.xingshu.internal',
      fetchFn: mockFetch,
    });

    const info = await provider.getStatus();
    assertStrictEqual(info.status, 'ERROR', 'Status is ERROR on HTTP 500');
    assert(info.message.includes('HTTP 500'), 'Error message contains HTTP 500 status');
  });

  await runAsyncTest('Group 3: allocateSession() POSTs to allocate API', async () => {
    let capturedUrl = '';
    let capturedBody = '';
    const allocatedAt = new Date(Date.now() - 1000).toISOString();
    const expiresAt = new Date(Date.now() + 60000).toISOString();

    const mockFetch: typeof fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
      capturedUrl = String(input);
      capturedBody = String(init?.body);
      return new Response(
        JSON.stringify({
          status: 'READY',
          sessionId: 'session_cloud_987654',
          allocatedPlayerId: 778899,
          allocatedAt,
          expiresAt,
          message: 'Ephemeral spectator instance ready.',
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } }
      );
    };

    const provider = new DefaultManagedSpectatorAccountProvider({
      serverEndpoint: 'https://watcher.xingshu.internal',
      fetchFn: mockFetch,
    });

    const session = await provider.allocateSession(665945, 'C140A15');

    assertStrictEqual(
      capturedUrl,
      `https://watcher.xingshu.internal${MANAGED_SPECTATOR_API.ALLOCATE}`,
      'Called correct allocate URL'
    );
    assertStrictEqual(capturedBody, JSON.stringify({ roomId: 665945, watchCode: 'C140A15' }), 'Payload preserves real room and opaque watch code');
    assertStrictEqual(session.status, 'READY', 'Session status is READY');
    assertStrictEqual(session.sessionId, 'session_cloud_987654', 'Assigned sessionId');
    assertStrictEqual(session.allocatedPlayerId, 778899, 'Assigned allocatedPlayerId');
    assertStrictEqual(session.allocatedAt, allocatedAt, 'Assigned allocatedAt');
    assertStrictEqual(session.expiresAt, expiresAt, 'Assigned expiresAt');
  });

  await runAsyncTest('Group 3: allocateSession() rejects when server pool is exhausted', async () => {
    const mockFetch: typeof fetch = async () => {
      return new Response(
        JSON.stringify({
          status: 'NO_ACCOUNT_AVAILABLE',
          message: 'All cloud watcher slots are currently in use.',
        }),
        { status: 503, headers: { 'Content-Type': 'application/json' } }
      );
    };

    const provider = new DefaultManagedSpectatorAccountProvider({
      serverEndpoint: 'https://watcher.xingshu.internal',
      fetchFn: mockFetch,
    });

    let rejected = false;
    let errorMsg = '';
    try {
      await provider.allocateSession(665945, 'C140A15');
    } catch (err: any) {
      rejected = true;
      errorMsg = err.message;
    }

    assert(rejected, 'allocateSession() rejected on 503');
    assert(
      errorMsg.includes('All cloud watcher slots are currently in use'),
      'Error message preserved server error detail'
    );
  });

  await runAsyncTest('Group 3: releaseSession() POSTs to release API', async () => {
    let capturedUrl = '';
    let capturedBody = '';

    const mockFetch: typeof fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
      capturedUrl = String(input);
      capturedBody = String(init?.body);
      return new Response(
        JSON.stringify({
          success: true,
          message: 'Session released successfully.',
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } }
      );
    };

    const provider = new DefaultManagedSpectatorAccountProvider({
      serverEndpoint: 'https://watcher.xingshu.internal',
      fetchFn: mockFetch,
    });

    await provider.releaseSession('session_cloud_987654');

    assertStrictEqual(
      capturedUrl,
      `https://watcher.xingshu.internal${MANAGED_SPECTATOR_API.RELEASE}`,
      'Called correct release URL'
    );
    assertStrictEqual(
      capturedBody,
      JSON.stringify({ sessionId: 'session_cloud_987654' }),
      'Payload contains sessionId'
    );
  });

  await runAsyncTest('Group 3: releaseSession() rejects invalid empty sessionId', async () => {
    const provider = new DefaultManagedSpectatorAccountProvider({
      serverEndpoint: 'https://watcher.xingshu.internal',
      fetchFn: async () => new Response('{}', { status: 200 }),
    });

    let rejected = false;
    try {
      await provider.releaseSession('   ');
    } catch (err: any) {
      rejected = true;
      assert(err.message.includes('Invalid sessionId'), 'Rejects empty sessionId');
    }
    assert(rejected, 'releaseSession rejected empty sessionId');
  });

  // ---------------------------------------------------------------------------
  // Test Group 4: STRICT SECURITY & CREDENTIAL HYGIENE ASSERTIONS
  // ---------------------------------------------------------------------------
  console.log('\n--- Test Group 4: Strict Credential Security & Invariant Audit ---');

  await runAsyncTest('Group 4: Source code scan verifies zero hardcoded credentials', async () => {
    const __dirname = path.dirname(fileURLToPath(import.meta.url));
    const providerFile = path.resolve(__dirname, '../services/managedAccountProvider.ts');
    const typesFile = path.resolve(__dirname, '../types/spectatorAccount.ts');

    assert(fs.existsSync(providerFile), 'managedAccountProvider.ts exists');
    assert(fs.existsSync(typesFile), 'spectatorAccount.ts exists');

    const providerContent = fs.readFileSync(providerFile, 'utf-8');
    const typesContent = fs.readFileSync(typesFile, 'utf-8');
    const combinedCode = providerContent + '\n' + typesContent;

    // 1. Prohibited plain-text credential patterns
    const credentialPatterns = [
      /password\s*[:=]\s*['"`][^'"`]+['"`]/i,
      /secret\s*[:=]\s*['"`][^'"`]+['"`]/i,
      /steam_password/i,
      /dummy_password/i,
      /fake_password/i,
      /mock_password/i,
      /auth_token\s*[:=]\s*['"`][^'"`]+['"`]/i,
      /private_key\s*[:=]/i,
    ];

    for (const pattern of credentialPatterns) {
      const match = combinedCode.match(pattern);
      assert(
        match === null,
        `Zero hardcoded credential pattern: ${pattern.toString()}`,
        `Found forbidden pattern: ${match?.[0]}`
      );
    }

    // 2. Prohibited dummy accounts/passwords
    const prohibitedSubstrings = [
      '123456',
      'password123',
      'admin123',
      'fake_steam_account',
      'mock_bot_account',
    ];

    for (const substr of prohibitedSubstrings) {
      assert(
        !combinedCode.includes(substr),
        `Source code does not contain prohibited dummy string '${substr}'`
      );
    }
  });

  await runAsyncTest('Group 4: Runtime objects contain zero password/secret fields', async () => {
    const provider = new DefaultManagedSpectatorAccountProvider();
    const info = await provider.getStatus();

    const prohibitedKeys = [
      'password',
      'secret',
      'token',
      'steamPassword',
      'steamTicket',
      'credential',
      'credentials',
    ];

    for (const key of prohibitedKeys) {
      assert(!(key in (info as any)), `ManagedAccountInfo does not expose '${key}'`);
    }

    const providerKeys = Object.keys(provider);
    for (const key of providerKeys) {
      assert(!key.toLowerCase().includes('password'), `Provider instance does not store password in '${key}'`);
      assert(!key.toLowerCase().includes('credential'), `Provider instance does not store credential in '${key}'`);
    }
  });

  await runAsyncTest('Group 4: Provider never fakes working status when unconfigured', async () => {
    const provider = new DefaultManagedSpectatorAccountProvider();
    const info = await provider.getStatus();

    assert(
      info.status !== 'AVAILABLE' && info.status !== 'READY',
      'Unconfigured provider NEVER reports AVAILABLE or READY'
    );
    assertStrictEqual(
      info.status,
      'SERVICE_NOT_CONFIGURED',
      'Provider honestly reports SERVICE_NOT_CONFIGURED'
    );
  });

  // ---------------------------------------------------------------------------
  // Summary Report
  // ---------------------------------------------------------------------------
  console.log('\n================================================================================');
  console.log(`MANAGED ACCOUNT PROVIDER RESULTS: ${passCount} PASSED, ${failCount} FAILED`);
  console.log('================================================================================');

  if (failCount > 0) {
    process.exit(1);
  }
}

main().catch((err) => {
  console.error('Fatal test runner failure:', err);
  process.exit(1);
});
