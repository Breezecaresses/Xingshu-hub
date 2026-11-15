import { useEffect, useSyncExternalStore } from 'react';
import { isTauriRuntime } from './runtime';
import {
  getWebCloudSnapshot,
  refreshWebCloudState,
  subscribeWebCloud,
  type WebCloudState,
} from './webCloudClient';

export function useWebCloudState(): WebCloudState {
  const snap = useSyncExternalStore(
    subscribeWebCloud,
    getWebCloudSnapshot,
    getWebCloudSnapshot,
  );

  useEffect(() => {
    if (!isTauriRuntime() && !snap.loaded) {
      void refreshWebCloudState();
    }
  }, [snap.loaded]);

  return snap;
}
