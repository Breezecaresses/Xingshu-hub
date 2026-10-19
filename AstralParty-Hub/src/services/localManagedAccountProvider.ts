import type { IManagedSpectatorAccountProvider, ManagedAccountInfo } from '../types/spectatorAccount.ts';
import { invoke } from '@tauri-apps/api/core';

type Invoke = <T>(command: string, args?: Record<string, unknown>) => Promise<T>;

/** Real local worker proof. All account, target and lease checks live in Rust. */
export class LocalManagedSpectatorAccountProvider implements IManagedSpectatorAccountProvider {
  readonly executionMode = 'LOCAL_VALIDATION' as const;
  private sessionId: string | null = null;
  private revision = 0;
  private pendingAllocation: Promise<ManagedAccountInfo> | null = null;
  private readonly call: Invoke;
  constructor(call: Invoke = invoke) { this.call = call; }

  async getStatus(): Promise<ManagedAccountInfo> {
    const rev = this.revision;
    const info = await this.call<ManagedAccountInfo>('managed_local_status', { sessionId: this.sessionId });
    if (rev === this.revision && info.status === 'EXPIRED') this.sessionId = null;
    return info;
  }

  allocateSession(roomId?: number, watchCode?: string): Promise<ManagedAccountInfo> {
    if (this.pendingAllocation) return this.pendingAllocation;
    const rev = this.revision;
    const task = this.call<ManagedAccountInfo>('managed_local_allocate', {
      roomId: roomId ?? null, watchCode: watchCode ?? null, sessionId: this.sessionId,
    }).then(async info => {
      if (rev !== this.revision) {
        // A mode switch can finish before IPC. Release the late allocation rather than orphan it.
        if (info.sessionId) await this.call('managed_local_release', { sessionId: info.sessionId });
        throw new Error('本机托管分配已取消');
      }
      this.sessionId = info.sessionId ?? null;
      return info;
    }).finally(() => { if (this.pendingAllocation === task) this.pendingAllocation = null; });
    this.pendingAllocation = task;
    return task;
  }

  async releaseSession(sessionId: string): Promise<void> {
    this.revision++;
    await this.call('managed_local_release', { sessionId });
    if (this.sessionId === sessionId) this.sessionId = null;
  }

  async releaseCurrent(): Promise<void> {
    this.revision++;
    if (this.sessionId) await this.releaseSession(this.sessionId);
  }
}

export const localManagedSpectatorAccountProvider = new LocalManagedSpectatorAccountProvider();
