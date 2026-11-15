export interface DesktopFeatureMeta {
  id: string;
  titleZh: string;
  titleEn: string;
  reasonZh: string;
  reasonEn: string;
}

const DESKTOP_FEATURES: Record<string, DesktopFeatureMeta> = {
  spectator: {
    id: 'spectator',
    titleZh: '星枢实时观战与跟房控制',
    titleEn: 'Live Spectator & Auto-Follow',
    reasonZh: '实时手牌转发与自动跟房需要连接本地游戏进程及 127.0.0.1:25890 网桥，仅桌面版星枢 Hub 可用。',
    reasonEn: 'Live hand forwarding requires a local game process and the loopback bridge on desktop.',
  },
  bridge: {
    id: 'bridge',
    titleZh: '星枢本地网桥控制台',
    titleEn: 'XingShu Loopback Bridge',
    reasonZh: '本地命名管道与 Loopback TCP (127.0.0.1:25890) 受浏览器沙箱隔离，仅在桌面版提供。',
    reasonEn: 'Loopback TCP and Named Pipe IPC are only accessible from the desktop runtime.',
  },
  runtime: {
    id: 'runtime',
    titleZh: '游戏运行时与 DLL 状态检测',
    titleEn: 'Runtime & Process Inspector',
    reasonZh: '本地游戏目录扫描、DLL 哈希校验与进程探测仅限桌面客户端。',
    reasonEn: 'Process scanning and local file verification are desktop-only capabilities.',
  },
  extensions: {
    id: 'extensions',
    titleZh: '安装包 / 模组与安卓无线安装',
    titleEn: 'Packages, Mods & Android ADB',
    reasonZh: '读取或修改游戏安装目录、备份事务与执行 ADB 命令需要桌面端系统权限。',
    reasonEn: 'Modifying game directories and running ADB requires desktop filesystem/process access.',
  },
  maintenance: {
    id: 'maintenance',
    titleZh: '安装事务回滚与本地备份',
    titleEn: 'Transactions & Local Backups',
    reasonZh: '磁盘事务日志与模组备份快照保存在本机应用目录，仅桌面版支持管理。',
    reasonEn: 'Install transactions and file backups reside on the local machine.',
  },
  diagnostics: {
    id: 'diagnostics',
    titleZh: '本地环境与运行库诊断',
    titleEn: 'System Diagnostics',
    reasonZh: '系统环境与本地网桥规则检测仅限桌面版。网页版请使用「回放分析 / 玩家资料 / 对战记录 / 图鉴」。',
    reasonEn: 'System diagnostic probes require desktop OS access. Use Replay Analyzer or Player Data on the web.',
  },
};

/** Safe synchronous check for Tauri v2 desktop webview without touching window when absent. */
export function isTauriRuntime(): boolean {
  if (typeof globalThis === 'undefined') return false;
  const scope = globalThis as Record<string, unknown>;
  return Boolean(scope.__TAURI_INTERNALS__ || scope.isTauri);
}

/** Resolve VITE BASE_URL for public assets (/ -> /foo, /hub/ -> /hub/foo). */
export function assetUrl(path: string): string {
  if (!path) return '';
  if (/^(https?:|data:|blob:|#)/i.test(path)) return path;
  const base = (typeof import.meta !== 'undefined' && (import.meta as { env?: Record<string, string> }).env?.BASE_URL) || '/';
  const normalizedBase = base.endsWith('/') ? base : `${base}/`;
  const clean = path.startsWith('/') ? path.slice(1) : path;
  if (normalizedBase !== '/' && `/${clean}`.startsWith(normalizedBase)) {
    return `/${clean}`;
  }
  return `${normalizedBase}${clean}`;
}

/** Resolve router basename (no trailing slash unless root ''). */
export function routerBasename(): string | undefined {
  const base = (typeof import.meta !== 'undefined' && (import.meta as { env?: Record<string, string> }).env?.BASE_URL) || '/';
  const stripped = base.replace(/\/+$/, '');
  return stripped || undefined;
}

/** Base URL for XingShu-Server API endpoints in Pure-Web and Desktop modes. */
export function webApiBaseUrl(): string {
  try {
    if (typeof localStorage !== 'undefined') {
      const override = localStorage.getItem('xingshu.web.apiBase')?.trim();
      if (override) return override.replace(/\/+$/, '');
    }
  } catch {
    // Storage might be restricted.
  }
  const envBase =
    (typeof import.meta !== 'undefined' && (import.meta as { env?: Record<string, string> }).env?.VITE_XINGSHU_API_BASE) || '';
  if (envBase.trim()) return envBase.trim().replace(/\/+$/, '');
  if (isTauriRuntime()) return 'https://124.221.176.134';
  return '';
}

/** Open an external URL safely in either Tauri opener plugin or standard browser tab. */
export async function openExternalUrl(url: string): Promise<void> {
  if (!url) return;
  if (isTauriRuntime()) {
    const { openUrl } = await import('@tauri-apps/plugin-opener');
    await openUrl(url);
    return;
  }
  if (typeof window !== 'undefined' && typeof window.open === 'function') {
    window.open(url, '_blank', 'noopener,noreferrer');
  }
}

export function getDesktopFeatureMeta(featureKey: keyof typeof DESKTOP_ONLY_KEYS | string): DesktopFeatureMeta {
  return DESKTOP_FEATURES[featureKey] || DESKTOP_FEATURES.diagnostics;
}

const DESKTOP_ONLY_KEYS = DESKTOP_FEATURES;
