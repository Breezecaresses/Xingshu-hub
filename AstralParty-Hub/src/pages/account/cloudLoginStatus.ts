export type CloudAccount = {
  uid: string;
  nick: string;
  qqAuthorized?: boolean;
  qqAuthorizedExpiresAt?: string | null;
};

function formatShortExpiry(iso: string | null | undefined, isZh: boolean): string {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  const formatted = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
  return isZh ? ` · 有效期至 ${formatted}` : ` · expires ${formatted}`;
}

/**
 * Derives durable QQ Bot authorization notice text from persisted /auth/me state
 * and session-local pairQq results (cleared on sign-out or matching account expiry).
 */
export function deriveQqAuthorizedBanner(
  account: CloudAccount | null,
  pairedAccount: CloudAccount | null,
  isZh: boolean,
): string | null {
  const active =
    account?.qqAuthorized
      ? account
      : pairedAccount && (!account || pairedAccount.uid !== account.uid)
        ? pairedAccount
        : null;
  if (!active || !active.uid) return null;
  const label = active.nick ? `${active.nick}（UID ${active.uid}）` : `UID ${active.uid}`;
  const expiry = formatShortExpiry(active.qqAuthorizedExpiresAt, isZh);
  return isZh ? `已授权给 QQ Bot：${label}${expiry}` : `Authorized QQ Bot: ${label}${expiry}`;
}
