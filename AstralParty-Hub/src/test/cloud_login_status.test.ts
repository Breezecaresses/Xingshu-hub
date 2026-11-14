import assert from 'node:assert/strict';
import { deriveQqAuthorizedBanner } from '../pages/account/cloudLoginStatus.ts';

// 1. Durable server-side qqAuthorized survives refresh / tab navigation even with no transient pairedAccount
{
  const banner = deriveQqAuthorizedBanner(
    {
      uid: '172852',
      nick: '木之本樱',
      qqAuthorized: true,
      qqAuthorizedExpiresAt: '2026-12-14T01:14:44Z',
    },
    null,
    true,
  );
  assert.ok(banner?.includes('已授权给 QQ Bot：木之本樱（UID 172852）'));
  assert.ok(banner?.includes('2026-12-14'));
}

// 2. Immediately after pairQq when server says qqAuthorized=false/stale before refresh or with alt UID
{
  const banner = deriveQqAuthorizedBanner(
    { uid: '172852', nick: '木之本樱', qqAuthorized: true },
    { uid: '3045259', nick: '星枢' },
    true,
  );
  assert.ok(banner?.includes('172852'));

  const altOnly = deriveQqAuthorizedBanner(
    { uid: '172852', nick: '木之本樱', qqAuthorized: false },
    { uid: '3045259', nick: '星枢' },
    true,
  );
  assert.ok(altOnly?.includes('3045259'));
}

// 3. Same UID expired on server (qqAuthorized: false) stops showing stale pairedAccount
{
  const expired = deriveQqAuthorizedBanner(
    { uid: '172852', nick: '木之本樱', qqAuthorized: false },
    { uid: '172852', nick: '木之本樱' },
    true,
  );
  assert.equal(expired, null);
}

// 4. Signed out without pairedAccount renders nothing
assert.equal(deriveQqAuthorizedBanner(null, null, true), null);

console.log('PASS cloud_login_status.test.ts');
