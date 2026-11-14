import { useSyncExternalStore } from 'react';
import { invoke, isTauri } from '@tauri-apps/api/core';
import {
  AppUpdateController,
  type AppUpdateCheckResult,
  type AppUpdatePhase,
  type AppUpdatePreferences,
  type AppUpdateRelease,
  type AppUpdateState,
} from './appUpdateCore.ts';

export type {
  AppUpdateCheckResult,
  AppUpdatePhase,
  AppUpdatePreferences,
  AppUpdateRelease,
  AppUpdateState,
};

function safeLocalStorage(): Storage | null {
  try {
    return typeof window !== 'undefined' ? window.localStorage : null;
  } catch {
    return null;
  }
}

export const appUpdateStore = new AppUpdateController({
  invoke: (command, args) => invoke(command, args),
  isDesktopRuntime: () => {
    try {
      return isTauri();
    } catch {
      return false;
    }
  },
  storage: safeLocalStorage(),
});

export function useAppUpdateState(): AppUpdateState {
  return useSyncExternalStore(
    appUpdateStore.subscribe,
    appUpdateStore.getSnapshot,
    appUpdateStore.getSnapshot,
  );
}
