import { webApiBaseUrl } from './runtime.ts';

export type WorkshopCategory = 'all' | 'optimization' | 'card' | 'skin' | 'voice_vfx';

export interface WorkshopModFile {
  target: string;
  sha256: string;
  originalSha256?: string;
  size: number;
  assets: string[];
  payloadUrl?: string | null;
}

export interface WorkshopModItem {
  id: string;
  name: string;
  author: string;
  category: Exclude<WorkshopCategory, 'all'> | string;
  categoryLabel: string;
  installKind: 'bundle_mod' | 'xspkg' | 'external_archive' | string;
  status: string;
  isSeed: boolean;
  gameVersion: string;
  platforms: string[];
  sizeBytes: number;
  sha256: string;
  tags: string[];
  description: string;
  targetInfo: string;
  assets: string[];
  targets: string[];
  files: WorkshopModFile[];
  downloadable: boolean;
  downloadUrl?: string | null;
  externalUrl?: string | null;
  previewUrl?: string | null;
  submittedAt?: string | null;
}

export interface WorkshopCategoryCount {
  id: Exclude<WorkshopCategory, 'all'>;
  label: string;
  count: number;
}

export interface WorkshopListResponse {
  items: WorkshopModItem[];
  total: number;
  counts: Record<string, number>;
  categories: WorkshopCategoryCount[];
}

export interface WorkshopSubmitInput {
  name: string;
  author: string;
  category: Exclude<WorkshopCategory, 'all'>;
  description: string;
  tags: string;
  targetInfo: string;
  externalUrl: string;
  gameVersion?: string;
  archiveFile?: File | null;
  previewFile?: File | null;
}

export interface WorkshopSubmitResponse {
  ok: boolean;
  id: string;
  status: string;
  sha256: string;
  installKind: string;
  message: string;
  item: WorkshopModItem;
}

export const WORKSHOP_CATEGORIES: Array<{ id: WorkshopCategory; labelZh: string; labelEn: string }> = [
  { id: 'all', labelZh: '全部', labelEn: 'All' },
  { id: 'optimization', labelZh: '优化 Mod', labelEn: 'Optimization' },
  { id: 'card', labelZh: '卡牌替换', labelEn: 'Cards' },
  { id: 'skin', labelZh: '角色皮肤', labelEn: 'Skins' },
  { id: 'voice_vfx', labelZh: '语音·特效', labelEn: 'Voice & VFX' },
];

export const MAX_WORKSHOP_UPLOAD_BYTES = 24 * 1024 * 1024;
export const MAX_WORKSHOP_PREVIEW_BYTES = 2 * 1024 * 1024;

export function formatModBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return '—';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function installKindLabel(kind: string): string {
  switch (kind) {
    case 'xspkg':
      return '.xspkg 扩展包';
    case 'bundle_mod':
      return 'Bundle 替换';
    case 'external_archive':
      return '外部整包';
    default:
      return kind || '模组包';
  }
}

export function workshopAssetUrl(pathOrUrl?: string | null): string {
  if (!pathOrUrl) return '';
  if (/^https?:\/\//i.test(pathOrUrl)) return pathOrUrl;
  const base = webApiBaseUrl();
  const clean = pathOrUrl.startsWith('/') ? pathOrUrl : `/${pathOrUrl}`;
  return `${base}${clean}`;
}

export async function fetchWorkshopMods(
  category: WorkshopCategory = 'all',
  query = '',
): Promise<WorkshopListResponse> {
  const base = webApiBaseUrl();
  const params = new URLSearchParams();
  if (category && category !== 'all') params.set('category', category);
  if (query.trim()) params.set('q', query.trim());
  const qs = params.toString();
  const url = `${base}/api/workshop/mods${qs ? `?${qs}` : ''}`;

  let res: Response;
  let text: string;
  try {
    res = await fetch(url);
    text = await res.text();
  } catch {
    throw new Error('暂时无法连接模组工坊，请检查网络后点击刷新列表。');
  }
  let parsed: Record<string, unknown> = {};
  try {
    parsed = text ? (JSON.parse(text) as Record<string, unknown>) : {};
  } catch {
    throw new Error(`工坊数据解析失败（HTTP ${res.status}）`);
  }
  if (!res.ok) {
    throw new Error(
      (typeof parsed.error === 'string' && parsed.error) || `加载工坊失败（HTTP ${res.status}）`,
    );
  }
  return parsed as unknown as WorkshopListResponse;
}

export function validateWorkshopSubmission(input: WorkshopSubmitInput): string | null {
  if (input.name.trim().length < 2) return '模组名称至少需要 2 个字符';
  if (input.author.trim().length < 1) return '请填写作者署名';
  if (input.description.trim().length < 4) return '模组说明至少需要 4 个字符';
  const ext = input.externalUrl.trim();
  if (!input.archiveFile && !ext) {
    return '请上传 .zip / .xspkg 模组压缩包，或填写安全的 https:// 发布页/网盘链接';
  }
  if (ext && !/^https:\/\/[^/\s]+\.[^/\s]+/i.test(ext)) {
    return '外部下载链接必须使用安全的 https:// 地址';
  }
  if (input.archiveFile) {
    const lowerName = input.archiveFile.name.toLowerCase();
    if (!lowerName.endsWith('.zip') && !lowerName.endsWith('.xspkg')) {
      return '在线投稿压缩包仅支持 .zip 或 .xspkg 格式';
    }
    if (input.archiveFile.size <= 0 || input.archiveFile.size > MAX_WORKSHOP_UPLOAD_BYTES) {
      return '上传压缩包大小需在 1B ~ 24MB 之间';
    }
  }
  if (input.previewFile) {
    const mime = (input.previewFile.type || '').toLowerCase();
    const pname = input.previewFile.name.toLowerCase();
    const okType =
      mime === 'image/png' ||
      mime === 'image/jpeg' ||
      mime === 'image/webp' ||
      /\.(png|jpe?g|webp)$/i.test(pname);
    if (!okType) return '预览图仅支持 PNG、JPG 或 WebP 格式';
    if (input.previewFile.size <= 0 || input.previewFile.size > MAX_WORKSHOP_PREVIEW_BYTES) {
      return '预览图大小不能超过 2MB';
    }
  }
  return null;
}

export async function submitWorkshopMod(input: WorkshopSubmitInput): Promise<WorkshopSubmitResponse> {
  const errMsg = validateWorkshopSubmission(input);
  if (errMsg) throw new Error(errMsg);

  const form = new FormData();
  form.set('name', input.name.trim());
  form.set('author', input.author.trim());
  form.set('category', input.category);
  form.set('description', input.description.trim());
  if (input.tags.trim()) form.set('tags', input.tags.trim());
  if (input.targetInfo.trim()) form.set('targetInfo', input.targetInfo.trim());
  if (input.externalUrl.trim()) form.set('externalUrl', input.externalUrl.trim());
  if (input.gameVersion?.trim()) form.set('gameVersion', input.gameVersion.trim());
  if (input.archiveFile) form.set('archive', input.archiveFile);
  if (input.previewFile) form.set('preview', input.previewFile);

  const res = await fetch(`${webApiBaseUrl()}/api/workshop/submit`, {
    method: 'POST',
    body: form,
  });
  const text = await res.text();
  let parsed: Record<string, unknown> = {};
  try {
    parsed = text ? (JSON.parse(text) as Record<string, unknown>) : {};
  } catch {
    throw new Error(`投稿提交失败（HTTP ${res.status}）`);
  }
  if (!res.ok) {
    throw new Error(
      (typeof parsed.error === 'string' && parsed.error) || `投稿提交失败（HTTP ${res.status}）`,
    );
  }
  return parsed as unknown as WorkshopSubmitResponse;
}
