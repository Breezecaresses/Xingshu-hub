import { useEffect, useState } from 'react';

type Listener = (paths: string[]) => void;

let pendingPaths: string[] = [];
const listeners = new Set<Listener>();

export function enqueueDroppedModPaths(paths: string[]): void {
  const cleaned = paths.map((p) => p.trim()).filter(Boolean);
  if (!cleaned.length) return;
  pendingPaths = cleaned;
  for (const listener of listeners) {
    listener(pendingPaths);
  }
}

export function clearPendingDroppedModPaths(): void {
  pendingPaths = [];
  for (const listener of listeners) {
    listener(pendingPaths);
  }
}

export function usePendingDroppedModPaths(): [string[], () => void] {
  const [paths, setPaths] = useState<string[]>(() => pendingPaths);

  useEffect(() => {
    const handler: Listener = (next) => setPaths(next);
    listeners.add(handler);
    if (pendingPaths.length) {
      setPaths(pendingPaths);
    }
    return () => {
      listeners.delete(handler);
    };
  }, []);

  return [paths, clearPendingDroppedModPaths];
}
