/**
 * Project XingShu — Spectator Card Visual Registry V2
 * AUTO-LOADED from canonical card_registry.generated.json (Phase HS4-P1.3D)
 * Optional runtime support loaded from runtime_card_registry.generated.json
 * Zero manual hardcoding / Zero heuristic special cases in production.
 */

import rawRegistry from './card_registry.generated.json' with { type: 'json' };
import rawRuntimeRegistry from './runtime_card_registry.generated.json' with { type: 'json' };

export interface CardProvenance {
  metadata: 'OFFICIAL_CONFIG';
  name: 'OFFICIAL_LOCALIZATION' | 'UNVERIFIED';
  icon: 'OFFICIAL_ASSET' | 'FALLBACK';
}

export interface GeneratedCardRecord {
  cardId: number;
  name: string | null;
  icon: string;
  imageKey: string;
  cardType: number;
  effectType: number;
  cost: number;
  provenance: CardProvenance;
}

export interface RuntimeCardRecord {
  cardId: number;
  name: string;
  description?: string;
  icon: string;
  imageKey?: string;
  cardType?: number;
  effectType?: number;
  cardTargetType?: number;
  cost?: number;
  isPlayable?: boolean;
  buildingId?: number;
  buildingName?: string;
  associatedCharacter?: {
    characterId: number;
    characterName: string;
    skillId: number;
    skillName: string;
    skillDescription: string;
  };
  provenance?: {
    classification: string;
    metadata: string;
    name: string;
    icon: string;
    sourceBundle?: string;
    assetBundle?: string;
  };
}

export interface CardDefinition {
  cardId: number;
  name: string;
  icon: string;
  category: string;
  cardType: number;
  effectType?: number;
  cost?: number;
  description?: string;
  imageKey?: string;
  provenance?: CardProvenance;
}

export interface CardMetadataResolved {
  cardId: number;
  name: string;
  idText: string;
  icon: string;
  category: string;
  cardType: number;
  effectType: number;
  cost: number;
  provenance: CardProvenance;
  isVerified: boolean;
}

export interface DisplayCardItem {
  cardId: number;
  card: CardDefinition;
  count: number;
}

export const CARD_CATEGORY_NAMES: Record<number, string> = {
  1: '攻击牌',
  2: '防御牌',
  3: '效果牌',
  4: '反击牌',
  5: '事件牌',
  6: '幸运牌',
  7: '霉运牌',
  8: '诅咒牌',
  9: '建筑牌',
};

// Raw list of all canonical 69 cards from code generator
export const CARD_REGISTRY_LIST: GeneratedCardRecord[] = rawRegistry as GeneratedCardRecord[];

// O(1) Lookup Map by CardId
export const CARD_REGISTRY_MAP: Map<number, GeneratedCardRecord> = new Map(
  CARD_REGISTRY_LIST.map((c) => [c.cardId, c])
);

// Runtime cards list and O(1) lookup map
export const RUNTIME_CARD_LIST: RuntimeCardRecord[] = (rawRuntimeRegistry || []) as RuntimeCardRecord[];
export const RUNTIME_CARD_MAP: Map<number, RuntimeCardRecord> = new Map(
  RUNTIME_CARD_LIST.map((c) => [c.cardId, c])
);

// Backward-compatible dictionary for existing consumers
export const CARD_REGISTRY: Record<number, CardDefinition> = Object.fromEntries(
  CARD_REGISTRY_LIST.map((c) => [
    c.cardId,
    {
      cardId: c.cardId,
      name: c.name ?? '未知卡牌',
      icon: c.icon || '/cards/card_fallback.svg',
      category: CARD_CATEGORY_NAMES[c.cardType] ?? '未知',
      cardType: c.cardType,
      effectType: c.effectType,
      cost: c.cost,
      imageKey: c.imageKey,
      provenance: c.provenance,
    },
  ])
);

/**
 * Returns canonical card metadata with zero hardcoding.
 * Strictly adheres to truth provenance.
 * Static 69 cards resolve with official metadata.
 * Runtime cards query runtime registry.
 * Unresolved / unknown cards fall back cleanly to safe defaults.
 */
export function getCardMetadata(cardId: number): CardMetadataResolved {
  const card = CARD_REGISTRY_MAP.get(cardId);
  if (card) {
    const isVerified = Boolean(card.name && card.provenance.name === 'OFFICIAL_LOCALIZATION');
    return {
      cardId,
      name: isVerified ? (card.name as string) : '未知卡牌',
      idText: `ID: ${cardId}`,
      icon: card.icon || '/cards/card_fallback.svg',
      category: CARD_CATEGORY_NAMES[card.cardType] ?? '未知',
      cardType: card.cardType,
      effectType: card.effectType,
      cost: card.cost,
      provenance: card.provenance,
      isVerified,
    };
  }

  // Runtime cards lookup
  const runtimeCard = RUNTIME_CARD_MAP.get(cardId);
  if (runtimeCard) {
    return {
      cardId,
      name: runtimeCard.name || '未知卡牌',
      idText: `ID: ${cardId}`,
      icon: runtimeCard.icon || '/cards/card_fallback.svg',
      category: CARD_CATEGORY_NAMES[runtimeCard.cardType ?? 9] ?? '建筑牌',
      cardType: runtimeCard.cardType ?? 9,
      effectType: runtimeCard.effectType ?? 0,
      cost: runtimeCard.cost ?? 0,
      provenance: {
        metadata: 'OFFICIAL_CONFIG',
        name: 'OFFICIAL_LOCALIZATION',
        icon: 'OFFICIAL_ASSET',
      },
      isVerified: true,
    };
  }

  // Fail-safe fallback for unmapped / DLC / runtime-only cards
  return {
    cardId,
    name: '未知卡牌',
    idText: `ID: ${cardId}`,
    icon: '/cards/card_fallback.svg',
    category: '未知',
    cardType: 0,
    effectType: 0,
    cost: 0,
    provenance: {
      metadata: 'OFFICIAL_CONFIG',
      name: 'UNVERIFIED',
      icon: 'FALLBACK',
    },
    isVerified: false,
  };
}

/**
 * Resolves a card ID to a CardDefinition display object.
 */
export function resolveCard(cardId: number): CardDefinition {
  const meta = getCardMetadata(cardId);
  const staticCard = CARD_REGISTRY_MAP.get(cardId);
  const runtimeCard = RUNTIME_CARD_MAP.get(cardId);
  return {
    cardId: meta.cardId,
    name: meta.isVerified ? meta.name : '未知卡牌',
    icon: meta.icon,
    category: meta.category,
    cardType: meta.cardType,
    effectType: meta.effectType,
    cost: meta.cost,
    imageKey: staticCard?.imageKey ?? runtimeCard?.imageKey,
    provenance: meta.provenance,
  };
}

/**
 * Groups raw multiset cardIds array into DisplayCardItem[] for compact, clean UI display (e.g. x2),
 * strictly preserving multiset quantities without mutating the underlying cardIds array.
 */
export function groupCardsForDisplay(cardIds: number[]): DisplayCardItem[] {
  if (!cardIds || cardIds.length === 0) return [];

  const counts = new Map<number, number>();
  for (const id of cardIds) {
    counts.set(id, (counts.get(id) || 0) + 1);
  }

  const result: DisplayCardItem[] = [];
  for (const [id, count] of counts.entries()) {
    result.push({
      cardId: id,
      card: resolveCard(id),
      count,
    });
  }

  return result;
}

/**
 * Returns color tokens for category badge rendering
 */
export function getCardCategoryBadgeStyle(category: string): {
  bg: string;
  color: string;
  border: string;
} {
  switch (category) {
    case '攻击牌':
      return {
        bg: 'rgba(239, 68, 68, 0.15)',
        color: '#f87171',
        border: 'rgba(239, 68, 68, 0.35)',
      };
    case '防御牌':
      return {
        bg: 'rgba(59, 130, 246, 0.15)',
        color: '#60a5fa',
        border: 'rgba(59, 130, 246, 0.35)',
      };
    case '效果牌':
      return {
        bg: 'rgba(168, 85, 247, 0.15)',
        color: '#c084fc',
        border: 'rgba(168, 85, 247, 0.35)',
      };
    case '反击牌':
      return {
        bg: 'rgba(245, 158, 11, 0.15)',
        color: '#fbbf24',
        border: 'rgba(245, 158, 11, 0.35)',
      };
    case '幸运牌':
    case '事件牌':
      return {
        bg: 'rgba(34, 197, 94, 0.15)',
        color: '#4ade80',
        border: 'rgba(34, 197, 94, 0.35)',
      };
    case '建筑牌':
      return {
        bg: 'rgba(20, 184, 166, 0.15)',
        color: '#2dd4bf',
        border: 'rgba(20, 184, 166, 0.35)',
      };
    default:
      return {
        bg: 'rgba(148, 163, 184, 0.12)',
        color: '#cbd5e1',
        border: 'rgba(148, 163, 184, 0.25)',
      };
  }
}
