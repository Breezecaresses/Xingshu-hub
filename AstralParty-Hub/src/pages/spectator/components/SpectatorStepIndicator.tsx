import React from 'react';
import {
  UserCheck,
  Radio,
  Gamepad2,
  Eye,
  Check,
  AlertTriangle,
} from 'lucide-react';

export type SpectatorAccountMode = 'USER_OWNED' | 'XINGSHU_MANAGED' | null;

export interface SpectatorStepIndicatorProps {
  accountMode: SpectatorAccountMode;
  isConnected: boolean;
  hasActiveRoom: boolean;
  roomId?: number | null;
  liveStreaming?: boolean;
  isSnapshotStale?: boolean;
  onSelectStep?: (step: 1 | 2 | 3 | 4) => void;
  isZh?: boolean;
}

type StepStatus = 'completed' | 'active' | 'warning' | 'pending';

interface StepDef {
  number: 1 | 2 | 3 | 4;
  titleZh: string;
  titleEn: string;
  status: StepStatus;
  subtextZh: string;
  subtextEn: string;
  icon: React.ComponentType<{ size?: number; className?: string; color?: string }>;
}

export const SpectatorStepIndicator: React.FC<SpectatorStepIndicatorProps> = ({
  accountMode,
  isConnected,
  hasActiveRoom,
  roomId,
  liveStreaming,
  isSnapshotStale,
  onSelectStep,
  isZh = true,
}) => {
  // Step 1: 观战账号 (Spectator Account)
  let step1Status: StepStatus = 'pending';
  let step1SubZh = '选择账号来源';
  let step1SubEn = 'Select source';
  if (accountMode === null) {
    step1Status = 'active';
  } else if (accountMode === 'USER_OWNED') {
    step1Status = 'completed';
    step1SubZh = '我的观战账号';
    step1SubEn = 'My Account';
  } else if (accountMode === 'XINGSHU_MANAGED') {
    step1Status = 'completed';
    step1SubZh = '星枢官方账号';
    step1SubEn = 'XingShu Managed';
  }

  // Step 2: 游戏 / 服务连接 (Game / Service Connection)
  let step2Status: StepStatus = 'pending';
  let step2SubZh = '等待账号选定';
  let step2SubEn = 'Awaiting account';
  if (accountMode === 'USER_OWNED') {
    if (isConnected) {
      step2Status = 'completed';
      step2SubZh = '网桥直连就绪';
      step2SubEn = 'Bridge Connected';
    } else {
      step2Status = 'active';
      step2SubZh = '等待 Runtime 连接';
      step2SubEn = 'Awaiting Runtime';
    }
  } else if (accountMode === 'XINGSHU_MANAGED') {
    step2Status = 'warning';
    step2SubZh = '服务未配置';
    step2SubEn = 'Unconfigured';
  }

  // Step 3: 观战房间 (Spectator Room)
  let step3Status: StepStatus = 'pending';
  let step3SubZh = '等待连接就绪';
  let step3SubEn = 'Awaiting connection';
  if (hasActiveRoom) {
    step3Status = isConnected ? 'completed' : 'warning';
    step3SubZh = roomId ? (isConnected ? `房间 #${roomId}` : `房间 #${roomId} (已断开)`) : '房间已载入';
    step3SubEn = roomId ? (isConnected ? `Room #${roomId}` : `Room #${roomId} (Offline)`) : 'Room Loaded';
  } else if (accountMode === 'USER_OWNED' && isConnected) {
    step3Status = 'active';
    step3SubZh = '等待进入观战房间';
    step3SubEn = 'Join Spectator Room';
  }

  // Step 4: 实时手牌 (Live Hands)
  let step4Status: StepStatus = 'pending';
  let step4SubZh = '等待房间手牌';
  let step4SubEn = 'Awaiting hand data';
  if (hasActiveRoom) {
    if (isConnected && liveStreaming !== false && !isSnapshotStale) {
      step4Status = 'active';
      step4SubZh = '4人全景实时流';
      step4SubEn = '4-Player Live Stream';
    } else {
      step4Status = 'warning';
      step4SubZh = '最后快照 (Runtime 已断开)';
      step4SubEn = 'Last Snapshot (Disconnected)';
    }
  }

  const steps: StepDef[] = [
    {
      number: 1,
      titleZh: '① 观战账号',
      titleEn: '① Account',
      status: step1Status,
      subtextZh: step1SubZh,
      subtextEn: step1SubEn,
      icon: UserCheck,
    },
    {
      number: 2,
      titleZh: '② 游戏 / 服务连接',
      titleEn: '② Connection',
      status: step2Status,
      subtextZh: step2SubZh,
      subtextEn: step2SubEn,
      icon: Radio,
    },
    {
      number: 3,
      titleZh: '③ 观战房间',
      titleEn: '③ Room',
      status: step3Status,
      subtextZh: step3SubZh,
      subtextEn: step3SubEn,
      icon: Gamepad2,
    },
    {
      number: 4,
      titleZh: '④ 实时手牌',
      titleEn: '④ Live Hands',
      status: step4Status,
      subtextZh: step4SubZh,
      subtextEn: step4SubEn,
      icon: Eye,
    },
  ];

  const getStatusStyles = (status: StepStatus) => {
    switch (status) {
      case 'completed':
        return {
          badgeBg: 'rgba(34, 197, 94, 0.2)',
          badgeColor: '#22c55e',
          badgeBorder: '1px solid rgba(34, 197, 94, 0.4)',
          textColor: '#f1f5f9',
          subtextColor: '#4ade80',
          lineColor: '#22c55e',
          panelBorder: 'rgba(34, 197, 94, 0.25)',
        };
      case 'active':
        return {
          badgeBg: 'rgba(99, 102, 241, 0.25)',
          badgeColor: '#818cf8',
          badgeBorder: '1px solid rgba(99, 102, 241, 0.6)',
          textColor: '#ffffff',
          subtextColor: '#a5b4fc',
          lineColor: '#6366f1',
          panelBorder: 'rgba(99, 102, 241, 0.4)',
        };
      case 'warning':
        return {
          badgeBg: 'rgba(245, 158, 11, 0.2)',
          badgeColor: '#fbbf24',
          badgeBorder: '1px solid rgba(245, 158, 11, 0.5)',
          textColor: '#fef3c7',
          subtextColor: '#f59e0b',
          lineColor: '#f59e0b',
          panelBorder: 'rgba(245, 158, 11, 0.3)',
        };
      case 'pending':
      default:
        return {
          badgeBg: 'rgba(255, 255, 255, 0.05)',
          badgeColor: '#64748b',
          badgeBorder: '1px solid rgba(255, 255, 255, 0.08)',
          textColor: '#94a3b8',
          subtextColor: '#64748b',
          lineColor: 'rgba(255, 255, 255, 0.08)',
          panelBorder: 'rgba(255, 255, 255, 0.05)',
        };
    }
  };

  return (
    <div
      className="spectator-step-indicator panel"
      style={{
        padding: '12px 16px',
        marginBottom: 18,
        backgroundColor: 'rgba(17, 24, 39, 0.65)',
        backdropFilter: 'blur(10px)',
        border: '1px solid rgba(255, 255, 255, 0.08)',
        borderRadius: 12,
      }}
    >
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(4, 1fr)',
          gap: 12,
          position: 'relative',
        }}
      >
        {steps.map((s, idx) => {
          const style = getStatusStyles(s.status);
          const isClickable = !!onSelectStep;

          return (
            <div
              key={s.number}
              onClick={() => {
                if (isClickable) onSelectStep(s.number);
              }}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 10,
                padding: '8px 10px',
                borderRadius: 8,
                backgroundColor: s.status === 'active' ? 'rgba(99, 102, 241, 0.08)' : 'transparent',
                border: s.status === 'active' ? '1px solid rgba(99, 102, 241, 0.25)' : '1px solid transparent',
                cursor: isClickable ? 'pointer' : 'default',
                transition: 'all 0.2s ease',
              }}
              title={isZh ? `点击查看第 ${s.number} 步详情` : `Step ${s.number}`}
            >
              {/* Step Icon Badge */}
              <div
                style={{
                  width: 32,
                  height: 32,
                  minWidth: 32,
                  borderRadius: '50%',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  backgroundColor: style.badgeBg,
                  color: style.badgeColor,
                  border: style.badgeBorder,
                  fontWeight: 700,
                  fontSize: 13,
                  position: 'relative',
                  boxShadow: s.status === 'active' ? '0 0 12px rgba(99, 102, 241, 0.4)' : 'none',
                }}
              >
                {s.status === 'completed' ? (
                  <Check size={16} strokeWidth={2.6} />
                ) : s.status === 'warning' ? (
                  <AlertTriangle size={15} strokeWidth={2.4} />
                ) : (
                  <span>{s.number}</span>
                )}
              </div>

              {/* Title & Subtext */}
              <div style={{ minWidth: 0, overflow: 'hidden' }}>
                <div
                  style={{
                    fontSize: 13,
                    fontWeight: 700,
                    color: style.textColor,
                    whiteSpace: 'nowrap',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                  }}
                >
                  {isZh ? s.titleZh : s.titleEn}
                </div>
                <div
                  style={{
                    fontSize: 11,
                    color: style.subtextColor,
                    whiteSpace: 'nowrap',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    marginTop: 2,
                    fontWeight: 500,
                  }}
                >
                  {isZh ? s.subtextZh : s.subtextEn}
                </div>
              </div>

              {/* Arrow Connector (for 1, 2, 3) */}
              {idx < 3 && (
                <div
                  style={{
                    marginLeft: 'auto',
                    color: 'rgba(255, 255, 255, 0.15)',
                    fontSize: 12,
                    fontWeight: 300,
                    userSelect: 'none',
                  }}
                >
                  →
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
};
