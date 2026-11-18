import React, { useMemo, useState } from 'react';
import {
  AlertTriangle,
  Check,
  Clock,
  Copy,
  Crown,
  DoorOpen,
  Eye,
  Lock,
  MessageSquare,
  Plus,
  RefreshCw,
  Server,
  Shield,
  Smartphone,
  Sparkles,
  Tag,
  UserPlus,
  Users,
  Globe,
} from 'lucide-react';
import { useTranslation } from '../../i18n';
import {
  ROOM_MAPS,
  ROOM_TAG_OPTIONS,
  ROOM_TITLE_PRESETS,
  SAMPLE_CHARACTERS,
  THINK_TIME_OPTIONS,
  ZERO_YIELD_NOTICE_EN,
  ZERO_YIELD_NOTICE_ZH,
  buildPaKaiFangCommand,
  buildSlotsForStatus,
  createDefaultDraft,
  createInitialRoomSnapshot,
  effectivePrivacy,
  getMapById,
  isSelfHostedZeroYield,
  sanitizeNumericPassword,
  type HostTarget,
  type RoomConfigDraft,
  type RoomMode,
  type RoomPreviewSnapshot,
  type RoomPreviewStatus,
  type RoomPrivacy,
} from './roomTemplateData';

function randomRoomCode(): string {
  return String(Math.floor(100000 + Math.random() * 900000));
}

export const RoomLobbyPage: React.FC = () => {
  const { locale } = useTranslation();
  const isZh = locale === 'zh-CN';

  const [draft, setDraft] = useState<RoomConfigDraft>(() => createDefaultDraft());
  const [room, setRoom] = useState<RoomPreviewSnapshot>(() => createInitialRoomSnapshot());
  const [copiedTarget, setCopiedTarget] = useState<string | null>(null);
  const [toast, setToast] = useState<string | null>(null);
  const [showQqPreview, setShowQqPreview] = useState(true);

  const filteredMaps = useMemo(
    () => ROOM_MAPS.filter((m) => m.mode === draft.mode),
    [draft.mode],
  );
  const selectedDraftMap = useMemo(() => getMapById(draft.mapId), [draft.mapId]);
  const activeRoomMap = useMemo(() => getMapById(room.config.mapId), [room.config.mapId]);
  const generatedCommand = useMemo(() => buildPaKaiFangCommand(draft), [draft]);
  const draftZeroYield = useMemo(() => isSelfHostedZeroYield(draft), [draft]);
  const activeZeroYield = useMemo(() => isSelfHostedZeroYield(room.config), [room.config]);
  const draftEffectivePrivacy = useMemo(() => effectivePrivacy(draft), [draft]);
  const activeEffectivePrivacy = useMemo(() => effectivePrivacy(room.config), [room.config]);

  const occupiedCount = useMemo(
    () => room.slots.filter((s) => s.seat <= room.config.maxPlayers && s.occupied).length,
    [room],
  );

  const flashToast = (msg: string) => {
    setToast(msg);
    window.setTimeout(() => setToast((prev) => (prev === msg ? null : prev)), 2600);
  };

  const copyText = async (key: string, text: string, label: string) => {
    try {
      await navigator.clipboard.writeText(text);
      setCopiedTarget(key);
      flashToast(isZh ? `已复制${label}` : `Copied ${label}`);
      window.setTimeout(() => setCopiedTarget((prev) => (prev === key ? null : prev)), 1800);
    } catch {
      flashToast(isZh ? '剪贴板不可用，请手动复制' : 'Clipboard unavailable');
    }
  };

  const handleHostTargetChange = (hostTarget: HostTarget) => {
    setDraft((prev) => {
      if (hostTarget === 'OFFICIAL') {
        const safeMode: RoomMode =
          prev.mode === 'COOP_EVENT' || prev.mode === 'SELF_HOSTED' ? 'PVE' : prev.mode;
        const nextMap =
          safeMode === prev.mode
            ? getMapById(prev.mapId)
            : (ROOM_MAPS.find((m) => m.mode === safeMode) ?? ROOM_MAPS[0]);
        return {
          ...prev,
          hostTarget,
          mode: safeMode,
          mapId: nextMap.id,
          difficulty:
            nextMap.difficulties.includes(prev.difficulty)
              ? prev.difficulty
              : (nextMap.difficulties[Math.min(2, Math.max(0, nextMap.difficulties.length - 1))] ?? ''),
        };
      }
      const nextMode: RoomMode = prev.mode === 'PVE' || prev.mode === 'PVP' ? 'COOP_EVENT' : prev.mode;
      const nextMap = ROOM_MAPS.find((m) => m.mode === nextMode) ?? getMapById(prev.mapId);
      return {
        ...prev,
        hostTarget,
        mode: nextMode,
        mapId: nextMap.id,
        difficulty: nextMap.difficulties[Math.min(2, Math.max(0, nextMap.difficulties.length - 1))] ?? '',
      };
    });
  };

  const handleModeChange = (mode: RoomMode) => {
    const firstMap = ROOM_MAPS.find((m) => m.mode === mode) ?? ROOM_MAPS[0];
    setDraft((prev) => ({
      ...prev,
      hostTarget: firstMap.officialAvailable ? prev.hostTarget : 'SELF_HOSTED',
      mode,
      mapId: firstMap.id,
      difficulty: firstMap.difficulties[Math.min(2, Math.max(0, firstMap.difficulties.length - 1))] ?? '',
    }));
  };

  const handleMapChange = (mapId: number) => {
    const nextMap = getMapById(mapId);
    setDraft((prev) => ({
      ...prev,
      hostTarget: nextMap.officialAvailable ? prev.hostTarget : 'SELF_HOSTED',
      mapId,
      difficulty: nextMap.difficulties.includes(prev.difficulty)
        ? prev.difficulty
        : (nextMap.difficulties[Math.min(2, Math.max(0, nextMap.difficulties.length - 1))] ?? ''),
    }));
  };

  const handleSimulateCreate = () => {
    const nextCode = randomRoomCode();
    const nextStatus: RoomPreviewStatus = 'RECRUITING';
    const nextConfig: RoomConfigDraft = { ...draft };
    setRoom({
      roomCode: nextCode,
      status: nextStatus,
      config: nextConfig,
      slots: buildSlotsForStatus(nextStatus, nextConfig.maxPlayers),
      updatedAtLabel: isZh ? '刚刚 · 已按左侧配置模拟创建' : 'Just now · Simulated from draft',
    });
    flashToast(
      isZh
        ? `已在模板中模拟创建房间 #${nextCode}（尚未接入实机接口）`
        : `Simulated room #${nextCode} created in preview template`,
    );
  };

  const handleSwitchPreviewStatus = (status: RoomPreviewStatus) => {
    setRoom((prev) => ({
      ...prev,
      status,
      slots: buildSlotsForStatus(status, prev.config.maxPlayers),
      updatedAtLabel: isZh ? '刚刚 · 预览状态已切换' : 'Just now · Preview state switched',
    }));
  };

  const handleJoinEmptySlot = (targetSeat?: number) => {
    const openSlot = room.slots.find(
      (s) => s.seat <= room.config.maxPlayers && !s.occupied && (!targetSeat || s.seat === targetSeat),
    );
    if (!openSlot) {
      flashToast(isZh ? '当前预览房间已满员' : 'Preview room is full');
      return;
    }
    const charChoice = SAMPLE_CHARACTERS[(openSlot.seat + Math.floor(Math.random() * 3)) % SAMPLE_CHARACTERS.length];
    setRoom((prev) => {
      const updatedSlots = prev.slots.map((s) =>
        s.seat === openSlot.seat
          ? {
              ...s,
              occupied: true,
              isHost: prev.status === 'IDLE' && openSlot.seat === 1,
              playerName: openSlot.seat === 1 ? '星枢旅人' : `测试队友 P${openSlot.seat}`,
              uid: `10${openSlot.seat}9420${openSlot.seat}`,
              characterName: charChoice.name,
              characterTitle: charChoice.title,
              ready: prev.status === 'IDLE' && openSlot.seat === 1,
            }
          : s,
      );
      const count = updatedSlots.filter((s) => s.seat <= prev.config.maxPlayers && s.occupied).length;
      const nextStatus: RoomPreviewStatus =
        count >= prev.config.maxPlayers ? 'READY_FULL' : 'RECRUITING';
      return {
        ...prev,
        status: nextStatus,
        slots: updatedSlots,
        updatedAtLabel: isZh ? `刚刚 · P${openSlot.seat} 已模拟加入` : `Just now · P${openSlot.seat} joined`,
      };
    });
  };

  const handleToggleSlotReady = (seat: number) => {
    setRoom((prev) => ({
      ...prev,
      slots: prev.slots.map((s) =>
        s.seat === seat && s.occupied && !s.isHost ? { ...s, ready: !s.ready } : s,
      ),
    }));
  };

  const handleClearSlot = (seat: number) => {
    setRoom((prev) => {
      const updatedSlots = prev.slots.map((s) =>
        s.seat === seat && !s.isHost
          ? {
              ...s,
              occupied: false,
              playerName: '',
              uid: '',
              characterName: '',
              characterTitle: '',
              ready: false,
            }
          : s,
      );
      const count = updatedSlots.filter((s) => s.seat <= prev.config.maxPlayers && s.occupied).length;
      return {
        ...prev,
        status: count === 0 ? 'IDLE' : 'RECRUITING',
        slots: updatedSlots,
      };
    });
  };

  const statusMeta = (status: RoomPreviewStatus) => {
    if (status === 'IDLE') {
      return {
        label: isZh ? '空闲待机' : 'Idle',
        bg: 'rgba(120, 117, 110, 0.12)',
        color: 'var(--text-secondary)',
        border: 'rgba(120, 117, 110, 0.24)',
      };
    }
    if (status === 'RECRUITING') {
      return {
        label: isZh ? `招募中 (${occupiedCount}/${room.config.maxPlayers})` : `Recruiting (${occupiedCount}/${room.config.maxPlayers})`,
        bg: 'var(--accent-soft)',
        color: 'var(--accent-text)',
        border: 'var(--accent-border)',
      };
    }
    return {
      label: isZh ? `满员备战中 (${occupiedCount}/${room.config.maxPlayers})` : `Full & Ready (${occupiedCount}/${room.config.maxPlayers})`,
      bg: 'var(--success-bg)',
      color: 'var(--success)',
      border: 'rgba(21, 128, 61, 0.28)',
    };
  };

  const inviteText = useMemo(() => {
    const mapLabel = isZh ? activeRoomMap.name : activeRoomMap.enName;
    const diffPart = room.config.difficulty ? ` · ${room.config.difficulty}` : '';
    const thinkPart = isZh ? ` · 思考${room.config.thinkTime}` : ` · Think:${room.config.thinkTime}`;
    const tagPart = room.config.roomTag ? ` · [${room.config.roomTag}]` : '';
    const yieldPart = activeZeroYield ? (isZh ? ' · 自托管0收益' : ' · Self-Hosted 0 Yield') : '';
    const cleanPwd = sanitizeNumericPassword(room.config.password);
    const pwdPart =
      activeEffectivePrivacy === 'PASSWORD'
        ? (isZh ? `｜密码：${cleanPwd}` : ` | Pass: ${cleanPwd}`)
        : '';
    return isZh
      ? `【星枢组队邀请】房间号 #${room.roomCode}｜${room.config.mode} · ${mapLabel}${diffPart}${thinkPart}${tagPart}${yieldPart}${pwdPart}｜当前 ${occupiedCount}/${room.config.maxPlayers} 人｜QQ 发送「趴加入 ${room.roomCode}」查看/上车`
      : `[XingShu Squad Invite] Room #${room.roomCode} | ${room.config.mode} · ${mapLabel}${diffPart}${thinkPart}${tagPart}${yieldPart}${pwdPart} | ${occupiedCount}/${room.config.maxPlayers} players`;
  }, [activeEffectivePrivacy, activeRoomMap, activeZeroYield, isZh, occupiedCount, room]);

  const badge = statusMeta(room.status);

  return (
    <div style={{ maxWidth: 1220, margin: '0 auto', paddingBottom: 40 }}>
      {/* Page Header */}
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'flex-start',
          flexWrap: 'wrap',
          gap: 12,
          marginBottom: 18,
        }}
      >
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
            <h1 style={{ fontSize: 22, fontWeight: 700, margin: 0 }}>
              {isZh ? '房间与组队 · 趴开房' : 'Rooms & Squad · Quick Host'}
            </h1>
            <span
              style={{
                fontSize: 11,
                fontWeight: 700,
                padding: '3px 10px',
                borderRadius: 999,
                background: 'var(--accent-soft)',
                color: 'var(--accent-text)',
                border: '1px solid var(--accent-border)',
              }}
            >
              {isZh ? '模板预览 · 未接入后端' : 'Preview Template · Local Mock'}
            </span>
          </div>
          <p style={{ color: 'var(--text-secondary)', fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            {isZh
              ? '用于确认星枢 Hub、手机端与 QQ Bot 三端「趴开房 / 房间与组队信息」交互与视觉效果的静态交互模板。'
              : 'Interactive UI & command template for quick room creation and P1-P4 squad telemetry across Hub, Mobile, and QQ Bot.'}
          </p>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          {toast && (
            <span
              style={{
                fontSize: 12,
                fontWeight: 600,
                padding: '6px 12px',
                borderRadius: 10,
                background: 'var(--success-bg)',
                color: 'var(--success)',
                border: '1px solid rgba(21, 128, 61, 0.25)',
              }}
            >
              {toast}
            </span>
          )}
          <button
            type="button"
            className="btn btn-secondary"
            style={{ fontSize: 12, padding: '7px 12px', display: 'inline-flex', alignItems: 'center', gap: 6 }}
            onClick={() => {
              const def = createDefaultDraft();
              setDraft(def);
              setRoom(createInitialRoomSnapshot());
              flashToast(isZh ? '已恢复默认模板状态' : 'Reset to default template');
            }}
          >
            <RefreshCw size={14} />
            {isZh ? '重置模板' : 'Reset'}
          </button>
        </div>
      </div>

      {/* Preview Architecture Banner */}
      <div
        className="card"
        style={{
          padding: '14px 18px',
          marginBottom: 18,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 14,
          flexWrap: 'wrap',
          background: 'linear-gradient(120deg, rgba(255, 241, 229, 0.72), rgba(255, 255, 255, 0.9))',
          borderColor: 'var(--accent-border)',
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, minWidth: 260, flex: 1 }}>
          <div
            style={{
              width: 36,
              height: 36,
              borderRadius: 10,
              background: '#1c1c1c',
              color: '#fff',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              flexShrink: 0,
            }}
          >
            <Sparkles size={18} />
          </div>
          <div>
            <div style={{ fontSize: 13.5, fontWeight: 700, color: 'var(--text-primary)' }}>
              {isZh ? '当前展示为三端原型模板（纯本地状态演示）' : 'Interactive Prototype Template (Local State Only)'}
            </div>
            <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
              {isZh
                ? '左侧可配置「趴开房」参数并生成跨端指令；右侧可切换空闲 / 招募中 / 满员备战中状态并模拟 P1–P4 入队与准备。'
                : 'Configure room presets on the left, simulate creating a room, and toggle P1-P4 slots & states on the right.'}
            </div>
          </div>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <span className="status-pill ok" style={{ fontSize: 11.5 }}>
            <DoorOpen size={13} />
            Hub
          </span>
          <span className="status-pill ok" style={{ fontSize: 11.5 }}>
            <Smartphone size={13} />
            Android
          </span>
          <span className="status-pill ok" style={{ fontSize: 11.5 }}>
            <MessageSquare size={13} />
            QQ Bot
          </span>
        </div>
      </div>

      {/* Main 2-Column Grid */}
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(360px, 1fr))',
          gap: 18,
          alignItems: 'start',
        }}
      >
        {/* Left: 「趴开房」One-Tap Host Config Template */}
        <section className="card" style={{ marginBottom: 0 }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 14 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <DoorOpen size={18} color="var(--accent)" />
              <h2 style={{ fontSize: 16, fontWeight: 700, margin: 0 }}>
                {isZh ? '「趴开房」一键开房配置' : 'Quick Host Configuration'}
              </h2>
            </div>
            <span style={{ fontSize: 11, color: 'var(--text-muted)', fontWeight: 600 }}>
              {isZh ? '模板表单' : 'Template Form'}
            </span>
          </div>

          {/* Host Target Selector: Official Active vs Self-Hosted 0-Yield */}
          <div style={{ marginBottom: 14 }}>
            <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-secondary)', marginBottom: 6 }}>
              {isZh ? '0. 托管目标与收益规则' : '0. Host Target & Yield Policy'}
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
              {(
                [
                  {
                    id: 'OFFICIAL',
                    zh: '官方现有模式',
                    en: 'Official Live Mode',
                    descZh: '直连官方规则 · 正常结算经验/星币/任务',
                    descEn: 'Official rules · Normal EXP & Coin rewards',
                  },
                  {
                    id: 'SELF_HOSTED',
                    zh: '自托管服务器 · 0收益 (原型占位)',
                    en: 'Self-Hosted Server · 0 Yield (Prototype)',
                    descZh: '非官方现役活动/娱乐演练 · 0 收益隔离',
                    descEn: 'Retired / custom modes · 0 official yield',
                  },
                ] as const
              ).map((target) => {
                const active = (draftZeroYield ? 'SELF_HOSTED' : draft.hostTarget) === target.id;
                const isSelf = target.id === 'SELF_HOSTED';
                return (
                  <button
                    key={target.id}
                    type="button"
                    onClick={() => handleHostTargetChange(target.id)}
                    style={{
                      textAlign: 'left',
                      padding: '9px 11px',
                      borderRadius: 12,
                      cursor: 'pointer',
                      border: active
                        ? (isSelf ? '1.5px solid #b45309' : '1.5px solid #15803d')
                        : '1px solid var(--glass-border)',
                      background: active
                        ? (isSelf ? 'rgba(254, 243, 199, 0.72)' : 'rgba(232, 246, 237, 0.78)')
                        : 'var(--glass-surface-subtle)',
                      color: active ? (isSelf ? '#92400e' : '#166534') : 'var(--text-primary)',
                    }}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12.5, fontWeight: 700 }}>
                      <Server size={13} />
                      <span>{isZh ? target.zh : target.en}</span>
                    </div>
                    <div style={{ fontSize: 11, opacity: 0.85, marginTop: 2 }}>
                      {isZh ? target.descZh : target.descEn}
                    </div>
                  </button>
                );
              })}
            </div>
          </div>

          {/* Mode Selection */}
          <div style={{ marginBottom: 14 }}>
            <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-secondary)', marginBottom: 6 }}>
              {isZh ? '1. 对局模式' : '1. Game Mode'}
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: 8 }}>
              {(
                [
                  { id: 'PVE', zh: 'PVE 合作', en: 'PVE Co-op', descZh: '官方现役副本 / 打怪', descEn: 'Official Co-op Stages', zeroYield: false },
                  { id: 'PVP', zh: 'PVP 对战', en: 'PVP Match', descZh: '官方现役 4 人大乱斗', descEn: 'Official 4P Arena', zeroYield: false },
                  { id: 'COOP_EVENT', zh: '合作活动 · 0收益', en: 'Co-op Event · 0 Yield', descZh: '官方未开放 · 自托管托管', descEn: 'Unavailable in Live · Self-Hosted', zeroYield: true },
                  { id: 'SELF_HOSTED', zh: '星枢自托管 · 0收益', en: 'XingShu Self-Hosted · 0 Yield', descZh: '绝版复刻 / 自定义沙盒', descEn: 'Retired / Custom Sandbox', zeroYield: true },
                ] as const
              ).map((m) => {
                const active = draft.mode === m.id;
                return (
                  <button
                    key={m.id}
                    type="button"
                    onClick={() => handleModeChange(m.id)}
                    style={{
                      textAlign: 'left',
                      padding: '10px 11px',
                      borderRadius: 12,
                      cursor: 'pointer',
                      border: active ? '1.5px solid #1c1c1c' : '1px solid var(--glass-border)',
                      background: active ? '#1c1c1c' : 'var(--glass-surface-subtle)',
                      color: active ? '#ffffff' : 'var(--text-primary)',
                      transition: 'all 0.16s ease',
                    }}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 6 }}>
                      <span style={{ fontSize: 13, fontWeight: 700 }}>{isZh ? m.zh : m.en}</span>
                      {m.zeroYield && (
                        <span
                          style={{
                            fontSize: 10,
                            fontWeight: 700,
                            padding: '1px 6px',
                            borderRadius: 999,
                            background: active ? 'rgba(251, 191, 36, 0.24)' : 'rgba(245, 158, 11, 0.16)',
                            color: active ? '#fde68a' : '#b45309',
                          }}
                        >
                          0 收益
                        </span>
                      )}
                    </div>
                    <div style={{ fontSize: 11, opacity: active ? 0.78 : 0.7, marginTop: 2 }}>
                      {isZh ? m.descZh : m.descEn}
                    </div>
                  </button>
                );
              })}
            </div>
          </div>

          {draftZeroYield && (
            <div
              style={{
                padding: '10px 12px',
                borderRadius: 11,
                marginBottom: 14,
                background: 'rgba(254, 243, 199, 0.72)',
                border: '1px solid rgba(217, 119, 6, 0.35)',
                color: '#92400e',
                fontSize: 12,
                lineHeight: 1.5,
                display: 'flex',
                alignItems: 'flex-start',
                gap: 8,
              }}
            >
              <AlertTriangle size={15} style={{ flexShrink: 0, marginTop: 2 }} />
              <div>
                <div style={{ fontWeight: 700 }}>
                  {isZh ? '自托管服务器 · 0 收益模式（原型占位）' : 'Self-Hosted Server · 0 Yield Mode (Prototype)'}
                </div>
                <div style={{ fontSize: 11.5, marginTop: 2 }}>
                  {isZh ? ZERO_YIELD_NOTICE_ZH : ZERO_YIELD_NOTICE_EN}
                </div>
              </div>
            </div>
          )}

          {/* Map Selection */}
          <div style={{ marginBottom: 14 }}>
            <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-secondary)', marginBottom: 6 }}>
              {isZh ? '2. 游玩地图' : '2. Map'}
            </div>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
              {filteredMaps.map((m) => {
                const active = draft.mapId === m.id;
                return (
                  <button
                    key={m.id}
                    type="button"
                    onClick={() => handleMapChange(m.id)}
                    style={{
                      padding: '6px 11px',
                      borderRadius: 999,
                      fontSize: 12,
                      fontWeight: active ? 700 : 500,
                      cursor: 'pointer',
                      border: active ? '1px solid var(--accent-border)' : '1px solid var(--glass-border)',
                      background: active ? 'var(--accent-soft)' : 'rgba(255,255,255,0.75)',
                      color: active ? 'var(--accent-text)' : 'var(--text-secondary)',
                    }}
                  >
                    {isZh ? m.name : m.enName}
                  </button>
                );
              })}
            </div>
          </div>

          {/* Difficulty & Max Players */}
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginBottom: 14 }}>
            <div>
              <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-secondary)', marginBottom: 6 }}>
                {isZh ? '3. 难度档位（原版分级）' : '3. Official Difficulty'}
              </div>
              {selectedDraftMap.difficulties.length > 0 ? (
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                  {selectedDraftMap.difficulties.map((diff) => {
                    const active = draft.difficulty === diff;
                    return (
                      <button
                        key={diff}
                        type="button"
                        onClick={() => setDraft((prev) => ({ ...prev, difficulty: diff }))}
                        style={{
                          padding: '5px 10px',
                          borderRadius: 8,
                          fontSize: 12,
                          fontWeight: active ? 700 : 500,
                          cursor: 'pointer',
                          border: active ? '1px solid #1c1c1c' : '1px solid var(--glass-border)',
                          background: active ? '#1c1c1c' : 'var(--glass-surface-subtle)',
                          color: active ? '#fff' : 'var(--text-primary)',
                        }}
                      >
                        {diff}
                      </button>
                    );
                  })}
                </div>
              ) : (
                <div
                  style={{
                    fontSize: 12,
                    color: 'var(--text-muted)',
                    padding: '7px 10px',
                    borderRadius: 8,
                    background: 'var(--glass-surface-subtle)',
                    border: '1px dashed var(--glass-border)',
                  }}
                >
                  {isZh ? '当前模式无需难度选择' : 'Standard difficulty'}
                </div>
              )}
            </div>

            <div>
              <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-secondary)', marginBottom: 6 }}>
                {isZh ? '4. 人数上限' : '4. Max Players'}
              </div>
              <div style={{ display: 'flex', gap: 6 }}>
                {([2, 3, 4] as const).map((n) => {
                  const active = draft.maxPlayers === n;
                  return (
                    <button
                      key={n}
                      type="button"
                      onClick={() => setDraft((prev) => ({ ...prev, maxPlayers: n }))}
                      style={{
                        flex: 1,
                        padding: '6px 8px',
                        borderRadius: 8,
                        fontSize: 12,
                        fontWeight: active ? 700 : 500,
                        cursor: 'pointer',
                        border: active ? '1px solid #1c1c1c' : '1px solid var(--glass-border)',
                        background: active ? '#1c1c1c' : 'var(--glass-surface-subtle)',
                        color: active ? '#fff' : 'var(--text-primary)',
                      }}
                    >
                      {n}{isZh ? '人' : 'P'}
                    </button>
                  );
                })}
              </div>
            </div>
          </div>

          {/* Think Time & Room Tag (aligned with official Create Room UI) */}
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginBottom: 14 }}>
            <div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 5, fontSize: 12, fontWeight: 600, color: 'var(--text-secondary)', marginBottom: 6 }}>
                <Clock size={13} />
                {isZh ? '5. 思考时间' : '5. Think Time'}
              </div>
              <div style={{ display: 'flex', gap: 6 }}>
                {THINK_TIME_OPTIONS.map((t) => {
                  const active = draft.thinkTime === t;
                  return (
                    <button
                      key={t}
                      type="button"
                      onClick={() => setDraft((prev) => ({ ...prev, thinkTime: t }))}
                      style={{
                        flex: 1,
                        padding: '6px 8px',
                        borderRadius: 8,
                        fontSize: 12,
                        fontWeight: active ? 700 : 500,
                        cursor: 'pointer',
                        border: active ? '1px solid #1c1c1c' : '1px solid var(--glass-border)',
                        background: active ? '#1c1c1c' : 'var(--glass-surface-subtle)',
                        color: active ? '#fff' : 'var(--text-primary)',
                      }}
                    >
                      {t}
                    </button>
                  );
                })}
              </div>
            </div>

            <div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 5, fontSize: 12, fontWeight: 600, color: 'var(--text-secondary)', marginBottom: 6 }}>
                <Tag size={13} />
                {isZh ? '6. 房间标签' : '6. Room Tag'}
              </div>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 5 }}>
                {ROOM_TAG_OPTIONS.map((tag) => {
                  const active = draft.roomTag === tag;
                  return (
                    <button
                      key={tag}
                      type="button"
                      onClick={() => setDraft((prev) => ({ ...prev, roomTag: tag }))}
                      style={{
                        padding: '5px 9px',
                        borderRadius: 8,
                        fontSize: 11.5,
                        fontWeight: active ? 700 : 500,
                        cursor: 'pointer',
                        border: active ? '1px solid var(--accent-border)' : '1px solid var(--glass-border)',
                        background: active ? 'var(--accent-soft)' : 'var(--glass-surface-subtle)',
                        color: active ? 'var(--accent-text)' : 'var(--text-secondary)',
                      }}
                    >
                      {tag}
                    </button>
                  );
                })}
              </div>
            </div>
          </div>

          {/* Privacy & Password */}
          <div style={{ marginBottom: 14 }}>
            <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-secondary)', marginBottom: 6 }}>
              {isZh ? '7. 密码与可见性（不输入则为公开房间）' : '7. Password & Privacy (empty = public)'}
            </div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
              {(
                [
                  { id: 'PUBLIC', zh: '公开可见', en: 'Public', icon: Globe },
                  { id: 'FRIENDS', zh: '仅好友', en: 'Friends Only', icon: Shield },
                  { id: 'PASSWORD', zh: '密码房', en: 'Password', icon: Lock },
                ] as const
              ).map((item) => {
                const active = draft.privacy === item.id;
                const Icon = item.icon;
                return (
                  <button
                    key={item.id}
                    type="button"
                    onClick={() => setDraft((prev) => ({ ...prev, privacy: item.id as RoomPrivacy }))}
                    style={{
                      display: 'inline-flex',
                      alignItems: 'center',
                      gap: 5,
                      padding: '6px 11px',
                      borderRadius: 10,
                      fontSize: 12,
                      fontWeight: active ? 700 : 500,
                      cursor: 'pointer',
                      border: active ? '1px solid var(--accent-border)' : '1px solid var(--glass-border)',
                      background: active ? 'var(--accent-soft)' : 'var(--glass-surface-subtle)',
                      color: active ? 'var(--accent-text)' : 'var(--text-secondary)',
                    }}
                  >
                    <Icon size={13} />
                    {isZh ? item.zh : item.en}
                  </button>
                );
              })}

              {draft.privacy === 'PASSWORD' && (
                <input
                  type="text"
                  inputMode="numeric"
                  maxLength={6}
                  value={draft.password}
                  onChange={(e) =>
                    setDraft((prev) => ({ ...prev, password: sanitizeNumericPassword(e.target.value) }))
                  }
                  placeholder={isZh ? '1-6位数字密码（留空则公开）' : '1-6 digits (empty = public)'}
                  style={{
                    width: 210,
                    padding: '6px 10px',
                    borderRadius: 10,
                    border: '1px solid var(--glass-border-strong)',
                    background: '#fff',
                    fontSize: 12,
                    fontFamily: 'monospace',
                    color: 'var(--text-primary)',
                  }}
                />
              )}
            </div>
            {draft.privacy === 'PASSWORD' && draftEffectivePrivacy === 'PUBLIC' && (
              <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 4 }}>
                {isZh ? '当前未输入数字密码，创建后将自动作为公开房间。' : 'No digits entered — will create as a public room.'}
              </div>
            )}
          </div>

          {/* Room Title & Note */}
          <div style={{ marginBottom: 14 }}>
            <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-secondary)', marginBottom: 6 }}>
              {isZh ? '8. 房间标题与备注' : '8. Room Title & Note'}
            </div>
            <input
              type="text"
              maxLength={40}
              value={draft.roomName}
              onChange={(e) => setDraft((prev) => ({ ...prev, roomName: e.target.value }))}
              placeholder={isZh ? '输入房名，例如：日常刷本速车' : 'Enter room title...'}
              style={{
                width: '100%',
                boxSizing: 'border-box',
                padding: '8px 12px',
                borderRadius: 10,
                border: '1px solid var(--glass-border)',
                background: '#fff',
                fontSize: 13,
                color: 'var(--text-primary)',
                marginBottom: 8,
              }}
            />
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginBottom: 8 }}>
              {ROOM_TITLE_PRESETS.map((preset) => (
                <button
                  key={preset}
                  type="button"
                  onClick={() => setDraft((prev) => ({ ...prev, roomName: preset }))}
                  style={{
                    padding: '3px 9px',
                    borderRadius: 999,
                    fontSize: 11,
                    cursor: 'pointer',
                    border: '1px solid var(--glass-border)',
                    background: 'var(--glass-surface-subtle)',
                    color: 'var(--text-secondary)',
                  }}
                >
                  + {preset.split(' · ')[0]}
                </button>
              ))}
            </div>
            <input
              type="text"
              maxLength={60}
              value={draft.note}
              onChange={(e) => setDraft((prev) => ({ ...prev, note: e.target.value }))}
              placeholder={isZh ? '备注（如：缺辅助位 / 欢迎萌新）' : 'Optional squad note...'}
              style={{
                width: '100%',
                boxSizing: 'border-box',
                padding: '7px 12px',
                borderRadius: 10,
                border: '1px solid var(--glass-border)',
                background: 'var(--glass-surface-subtle)',
                fontSize: 12,
                color: 'var(--text-secondary)',
              }}
            />
          </div>

          {/* Generated 「趴开房」 Command Box */}
          <div
            style={{
              padding: '11px 13px',
              borderRadius: 12,
              background: 'var(--glass-code-bg)',
              border: '1px solid var(--glass-border)',
              marginBottom: 14,
            }}
          >
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                marginBottom: 6,
              }}
            >
              <span style={{ fontSize: 11.5, fontWeight: 700, color: 'var(--text-secondary)' }}>
                {isZh ? '生成「趴开房」跨端指令预览' : 'Generated Command Preview'}
              </span>
              <button
                type="button"
                onClick={() => void copyText('cmd', generatedCommand, isZh ? '开房指令' : 'command')}
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: 4,
                  padding: '3px 8px',
                  borderRadius: 6,
                  fontSize: 11,
                  fontWeight: 600,
                  cursor: 'pointer',
                  border: '1px solid var(--glass-border)',
                  background: '#fff',
                  color: 'var(--text-primary)',
                }}
              >
                {copiedTarget === 'cmd' ? <Check size={12} color="var(--success)" /> : <Copy size={12} />}
                {copiedTarget === 'cmd' ? (isZh ? '已复制' : 'Copied') : (isZh ? '复制指令' : 'Copy')}
              </button>
            </div>
            <code
              style={{
                display: 'block',
                fontSize: 12.5,
                lineHeight: 1.5,
                color: 'var(--text-primary)',
                wordBreak: 'break-all',
              }}
            >
              {generatedCommand}
            </code>
          </div>

          {/* Primary CTA */}
          <button
            type="button"
            className="btn btn-primary"
            style={{
              width: '100%',
              padding: '11px 16px',
              fontSize: 13.5,
              fontWeight: 700,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              gap: 8,
            }}
            onClick={handleSimulateCreate}
          >
            <DoorOpen size={16} />
            {isZh ? '模拟创建房间并刷新右侧预览' : 'Simulate Create Room (Update Preview)'}
          </button>
        </section>

        {/* Right: Current Room & Squad Info Card + Cross-Platform Link Placeholders */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
          <section className="card" style={{ marginBottom: 0 }}>
            {/* Room Top Meta */}
            <div
              style={{
                display: 'flex',
                alignItems: 'flex-start',
                justifyContent: 'space-between',
                gap: 12,
                flexWrap: 'wrap',
                marginBottom: 12,
              }}
            >
              <div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                  <span style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-muted)' }}>
                    {isZh ? '当前房间与组队信息' : 'Current Room & Squad'}
                  </span>
                  <span
                    style={{
                      fontSize: 11.5,
                      fontWeight: 700,
                      padding: '2px 9px',
                      borderRadius: 999,
                      background: badge.bg,
                      color: badge.color,
                      border: `1px solid ${badge.border}`,
                    }}
                  >
                    {badge.label}
                  </span>
                  <span
                    style={{
                      fontSize: 11,
                      fontWeight: 700,
                      padding: '2px 9px',
                      borderRadius: 999,
                      background: activeZeroYield ? 'rgba(254, 243, 199, 0.85)' : 'var(--success-bg)',
                      color: activeZeroYield ? '#92400e' : 'var(--success)',
                      border: activeZeroYield
                        ? '1px solid rgba(217, 119, 6, 0.35)'
                        : '1px solid rgba(21, 128, 61, 0.25)',
                    }}
                  >
                    {activeZeroYield
                      ? (isZh ? '自托管 · 0收益 (原型占位)' : 'Self-Hosted · 0 Yield')
                      : (isZh ? '官方现有模式' : 'Official Mode')}
                  </span>
                </div>
                <div style={{ display: 'flex', alignItems: 'baseline', gap: 10, marginTop: 6 }}>
                  <span style={{ fontSize: 24, fontWeight: 800, fontFamily: 'monospace', letterSpacing: 1 }}>
                    #{room.roomCode}
                  </span>
                  <button
                    type="button"
                    title={isZh ? '随机切换预览房号' : 'Randomize code'}
                    onClick={() => setRoom((prev) => ({ ...prev, roomCode: randomRoomCode() }))}
                    style={{
                      border: 'none',
                      background: 'transparent',
                      color: 'var(--text-muted)',
                      cursor: 'pointer',
                      padding: 2,
                      display: 'inline-flex',
                      alignItems: 'center',
                    }}
                  >
                    <RefreshCw size={13} />
                  </button>
                </div>
                <div style={{ fontSize: 14, fontWeight: 700, color: 'var(--text-primary)', marginTop: 2 }}>
                  {room.config.roomName || (isZh ? '未命名星枢房间' : 'Untitled Room')}
                </div>
                <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 3 }}>
                  {room.config.mode} · {isZh ? activeRoomMap.name : activeRoomMap.enName}
                  {room.config.difficulty ? ` · ${room.config.difficulty}` : ''}
                  {` · ${isZh ? '思考' : 'Think'}:${room.config.thinkTime}`}
                  {room.config.roomTag ? ` · [${room.config.roomTag}]` : ''} ·{' '}
                  {activeEffectivePrivacy === 'PASSWORD'
                    ? (isZh ? `密码 ${sanitizeNumericPassword(room.config.password)}` : `Pass ${sanitizeNumericPassword(room.config.password)}`)
                    : activeEffectivePrivacy === 'FRIENDS'
                    ? (isZh ? '仅好友' : 'Friends')
                    : (isZh ? '公开房' : 'Public')}
                </div>
                {activeZeroYield && (
                  <div
                    style={{
                      marginTop: 6,
                      padding: '6px 9px',
                      borderRadius: 8,
                      background: 'rgba(254, 243, 199, 0.72)',
                      border: '1px solid rgba(217, 119, 6, 0.3)',
                      color: '#92400e',
                      fontSize: 11.5,
                      fontWeight: 600,
                    }}
                  >
                    {isZh ? ZERO_YIELD_NOTICE_ZH : ZERO_YIELD_NOTICE_EN}
                  </div>
                )}
                {room.config.note && (
                  <div style={{ fontSize: 11.5, color: 'var(--text-muted)', marginTop: 3 }}>
                    {room.config.note}
                  </div>
                )}
              </div>

              {/* Preview State Switcher */}
              <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 6 }}>
                <span style={{ fontSize: 11, color: 'var(--text-muted)', fontWeight: 600 }}>
                  {isZh ? '切换预览状态' : 'Preview State'}
                </span>
                <div
                  style={{
                    display: 'inline-flex',
                    padding: 3,
                    borderRadius: 10,
                    background: 'var(--glass-surface-subtle)',
                    border: '1px solid var(--glass-border)',
                    gap: 3,
                  }}
                >
                  {(
                    [
                      { id: 'IDLE', zh: '空闲', en: 'Idle' },
                      { id: 'RECRUITING', zh: '招募中', en: 'Recruiting' },
                      { id: 'READY_FULL', zh: '满员备战中', en: 'Full Ready' },
                    ] as const
                  ).map((st) => {
                    const active = room.status === st.id;
                    return (
                      <button
                        key={st.id}
                        type="button"
                        onClick={() => handleSwitchPreviewStatus(st.id)}
                        style={{
                          padding: '4px 9px',
                          borderRadius: 7,
                          fontSize: 11.5,
                          fontWeight: active ? 700 : 500,
                          cursor: 'pointer',
                          border: active ? '1px solid var(--glass-border)' : '1px solid transparent',
                          background: active ? '#fff' : 'transparent',
                          color: active ? 'var(--text-primary)' : 'var(--text-muted)',
                          boxShadow: active ? '0 1px 3px rgba(0,0,0,0.06)' : 'none',
                        }}
                      >
                        {isZh ? st.zh : st.en}
                      </button>
                    );
                  })}
                </div>
              </div>
            </div>

            {/* Quick Action Bar */}
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginBottom: 14 }}>
              <button
                type="button"
                className="btn btn-secondary"
                style={{ fontSize: 12, padding: '6px 11px', display: 'inline-flex', alignItems: 'center', gap: 5 }}
                onClick={() => void copyText('code', room.roomCode, isZh ? '房间号' : 'room code')}
              >
                {copiedTarget === 'code' ? <Check size={13} color="var(--success)" /> : <Copy size={13} />}
                {isZh ? '复制房间号' : 'Copy Code'}
              </button>
              <button
                type="button"
                className="btn btn-secondary"
                style={{ fontSize: 12, padding: '6px 11px', display: 'inline-flex', alignItems: 'center', gap: 5 }}
                onClick={() => void copyText('invite', inviteText, isZh ? '邀请文案' : 'invite text')}
              >
                {copiedTarget === 'invite' ? <Check size={13} color="var(--success)" /> : <Users size={13} />}
                {isZh ? '邀请队友' : 'Invite Squad'}
              </button>
              <button
                type="button"
                className="btn btn-secondary"
                style={{ fontSize: 12, padding: '6px 11px', display: 'inline-flex', alignItems: 'center', gap: 5 }}
                onClick={() => handleJoinEmptySlot()}
              >
                <UserPlus size={13} />
                {isZh ? '加入房间（模拟占位）' : 'Simulate Join'}
              </button>
            </div>

            {/* P1 - P4 Squad Slots */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: 9 }}>
              {room.slots.map((slot) => {
                const disabledByLimit = slot.seat > room.config.maxPlayers;
                return (
                  <div
                    key={slot.seat}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      gap: 10,
                      padding: '10px 12px',
                      borderRadius: 12,
                      border: slot.occupied
                        ? '1px solid var(--glass-border)'
                        : '1px dashed var(--glass-border-strong)',
                      background: disabledByLimit
                        ? 'rgba(28, 28, 28, 0.025)'
                        : slot.occupied
                        ? 'rgba(255, 255, 255, 0.88)'
                        : 'var(--glass-surface-subtle)',
                      opacity: disabledByLimit ? 0.5 : 1,
                    }}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
                      <span
                        style={{
                          width: 30,
                          height: 30,
                          borderRadius: 9,
                          display: 'inline-flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          fontSize: 12,
                          fontWeight: 800,
                          flexShrink: 0,
                          background: slot.isHost
                            ? '#1c1c1c'
                            : slot.occupied
                            ? 'var(--accent-soft)'
                            : 'rgba(28,28,28,0.06)',
                          color: slot.isHost
                            ? '#fff'
                            : slot.occupied
                            ? 'var(--accent-text)'
                            : 'var(--text-muted)',
                        }}
                      >
                        P{slot.seat}
                      </span>

                      {slot.occupied ? (
                        <div style={{ minWidth: 0 }}>
                          <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                            <span style={{ fontSize: 13.5, fontWeight: 700, color: 'var(--text-primary)' }}>
                              {slot.playerName}
                            </span>
                            {slot.isHost && (
                              <span
                                style={{
                                  display: 'inline-flex',
                                  alignItems: 'center',
                                  gap: 3,
                                  fontSize: 10.5,
                                  fontWeight: 700,
                                  padding: '1px 7px',
                                  borderRadius: 999,
                                  background: 'rgba(255, 196, 108, 0.28)',
                                  color: '#92400e',
                                }}
                              >
                                <Crown size={10} />
                                {isZh ? '房主' : 'Host'}
                              </span>
                            )}
                            <span style={{ fontSize: 11, color: 'var(--text-muted)', fontFamily: 'monospace' }}>
                              UID {slot.uid}
                            </span>
                          </div>
                          <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginTop: 2 }}>
                            {slot.characterName} · {slot.characterTitle}
                          </div>
                        </div>
                      ) : (
                        <div>
                          <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-muted)' }}>
                            {disabledByLimit
                              ? (isZh ? `未开放（当前上限 ${room.config.maxPlayers} 人）` : `Closed (limit ${room.config.maxPlayers})`)
                              : (isZh ? '等待队友加入空位…' : 'Open slot waiting for teammate...')}
                          </div>
                          {!disabledByLimit && (
                            <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>
                              {isZh ? '点击右侧按钮可模拟占位加入' : 'Click Join to simulate teammate'}
                            </div>
                          )}
                        </div>
                      )}
                    </div>

                    {/* Slot Actions */}
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexShrink: 0 }}>
                      {slot.occupied ? (
                        <>
                          <button
                            type="button"
                            onClick={() => handleToggleSlotReady(slot.seat)}
                            style={{
                              padding: '4px 10px',
                              borderRadius: 999,
                              fontSize: 11.5,
                              fontWeight: 700,
                              cursor: slot.isHost ? 'default' : 'pointer',
                              border: 'none',
                              background: slot.ready ? 'var(--success-bg)' : 'var(--warning-bg)',
                              color: slot.ready ? 'var(--success)' : 'var(--warning)',
                            }}
                          >
                            {slot.isHost
                              ? (isZh ? '房主已就绪' : 'Host Ready')
                              : slot.ready
                              ? (isZh ? '已准备' : 'Ready')
                              : (isZh ? '未准备' : 'Not Ready')}
                          </button>
                          {!slot.isHost && (
                            <button
                              type="button"
                              onClick={() => handleClearSlot(slot.seat)}
                              style={{
                                padding: '4px 8px',
                                borderRadius: 8,
                                fontSize: 11,
                                cursor: 'pointer',
                                border: '1px solid var(--glass-border)',
                                background: 'transparent',
                                color: 'var(--text-muted)',
                              }}
                            >
                              {isZh ? '移出' : 'Remove'}
                            </button>
                          )}
                        </>
                      ) : !disabledByLimit ? (
                        <button
                          type="button"
                          onClick={() => handleJoinEmptySlot(slot.seat)}
                          style={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            gap: 4,
                            padding: '5px 10px',
                            borderRadius: 999,
                            fontSize: 11.5,
                            fontWeight: 600,
                            cursor: 'pointer',
                            border: '1px solid var(--accent-border)',
                            background: 'var(--accent-soft)',
                            color: 'var(--accent-text)',
                          }}
                        >
                          <Plus size={12} />
                          {isZh ? '占位加入' : 'Join'}
                        </button>
                      ) : null}
                    </div>
                  </div>
                );
              })}
            </div>

            <div style={{ fontSize: 11.5, color: 'var(--text-muted)', marginTop: 10, textAlign: 'right' }}>
              {room.updatedAtLabel}
            </div>
          </section>

          {/* Cross-Platform Link Placeholder Card (Mobile + QQ Bot) */}
          <section className="card" style={{ marginBottom: 0 }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 10 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <Eye size={17} color="var(--accent)" />
                <h2 style={{ fontSize: 15, fontWeight: 700, margin: 0 }}>
                  {isZh ? '手机端 / QQ Bot 跨端联动占位' : 'Mobile & QQ Bot Sync Placeholders'}
                </h2>
              </div>
              <button
                type="button"
                onClick={() => setShowQqPreview((v) => !v)}
                style={{
                  border: '1px solid var(--glass-border)',
                  background: 'var(--glass-surface-subtle)',
                  color: 'var(--text-secondary)',
                  fontSize: 11.5,
                  fontWeight: 600,
                  borderRadius: 8,
                  padding: '4px 9px',
                  cursor: 'pointer',
                }}
              >
                {showQqPreview ? (isZh ? '收起卡片预览' : 'Hide Card') : (isZh ? '展开卡片预览' : 'Show Card')}
              </button>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10, marginBottom: showQqPreview ? 12 : 0 }}>
              <div
                style={{
                  padding: '10px 12px',
                  borderRadius: 12,
                  background: 'var(--glass-surface-subtle)',
                  border: '1px solid var(--glass-border)',
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12.5, fontWeight: 700 }}>
                  <Smartphone size={14} />
                  {isZh ? '手机助手 · 开房组队页' : 'Android · Room Tab'}
                </div>
                <div style={{ fontSize: 11.5, color: 'var(--text-secondary)', marginTop: 4 }}>
                  {isZh
                    ? '占位接口：待接入 Hub/云端房间同步（当前可在手机端「开房组队」标签页预览相同模板）。'
                    : 'Placeholder for live cloud room sync; interactive preview available in Android tab.'}
                </div>
              </div>

              <div
                style={{
                  padding: '10px 12px',
                  borderRadius: 12,
                  background: 'var(--glass-surface-subtle)',
                  border: '1px solid var(--glass-border)',
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12.5, fontWeight: 700 }}>
                  <MessageSquare size={14} />
                  {isZh ? 'QQ Bot · 趴开房 / 趴房间' : 'QQ Bot · Room Commands'}
                </div>
                <div style={{ fontSize: 11.5, color: 'var(--text-secondary)', marginTop: 4 }}>
                  {isZh
                    ? '占位指令：趴开房、趴房间、趴组队、趴加入（当前仅展示模板消息卡，未改线上逻辑）。'
                    : 'Planned commands: 趴开房 / 趴房间 / 趴组队 (preview spec staged only).'}
                </div>
              </div>
            </div>

            {showQqPreview && (
              <div
                style={{
                  padding: '12px 14px',
                  borderRadius: 12,
                  background: '#faf9f5',
                  border: '1px solid var(--glass-border)',
                  fontSize: 12,
                  lineHeight: 1.6,
                }}
              >
                <div style={{ fontWeight: 700, color: 'var(--text-primary)', marginBottom: 4 }}>
                  {isZh ? 'QQ 群 Markdown 卡片消息预览（Mock）' : 'QQ Bot Markdown Card Mockup'}
                </div>
                <div style={{ color: 'var(--text-secondary)', fontFamily: 'monospace', whiteSpace: 'pre-wrap' }}>
                  {[
                    `**🎮 星枢组队大厅 · 房间 #${room.roomCode}**`,
                    `> 模式：${room.config.mode} ｜ 地图：${activeRoomMap.name}${room.config.difficulty ? `（${room.config.difficulty}）` : ''} ｜ 思考：${room.config.thinkTime}${room.config.roomTag ? ` ｜ 标签：${room.config.roomTag}` : ''} ｜ ${occupiedCount}/${room.config.maxPlayers} 人`,
                    `> 状态：${badge.label}${activeEffectivePrivacy === 'PASSWORD' ? ` ｜ 密码：${sanitizeNumericPassword(room.config.password)}` : ' ｜ 公开房'}${activeZeroYield ? ' ｜ ⚠️ 0收益（自托管娱乐模式）' : ' ｜ 官方现有模式'}`,
                    ...room.slots
                      .filter((s) => s.seat <= room.config.maxPlayers)
                      .map((s) =>
                        s.occupied
                          ? `> P${s.seat} ${s.isHost ? '👑' : s.ready ? '✅' : '⏳'} ${s.playerName}（${s.characterName}）`
                          : `> P${s.seat} ▫️ 空位待加入`,
                      ),
                    `[复制房间号]  [一键加入 #${room.roomCode}]  [刷新队伍状态]`,
                  ].join('\n')}
                </div>
              </div>
            )}
          </section>
        </div>
      </div>
    </div>
  );
};
