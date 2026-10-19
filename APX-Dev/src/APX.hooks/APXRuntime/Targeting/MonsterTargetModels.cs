using System;
using System.Collections.Generic;

namespace APXRuntime.Targeting
{
    /// <summary>
    /// 怪物索敌与路径预测判定原因 (Phase 2D-5B)
    /// 明确分层：
    /// PROVEN GAME FACTS: 有向图、地块邻接、角色类型
    /// USER RULE MODEL: 有限步数、距离优先、位置靠前、红>蓝>绿分支决胜
    /// </summary>
    public enum PredictionReason
    {
        /// <summary>唯一最短图拓扑距离目标</summary>
        NearestTarget,

        /// <summary>距离并列，基于位置最靠前 (FrontMost / RouteProgress) 决胜</summary>
        FrontMostTieBreak,

        /// <summary>距离与位置均并列，基于房间玩家 Slot 保底决胜</summary>
        SlotTieBreak,

        /// <summary>等价候选路径在交叉路口由 RED > BLUE > GREEN 分支优先级决胜</summary>
        BranchPriorityTieBreak,

        /// <summary>怪物本回合已攻击过玩家，按内置路线继续移动</summary>
        DefaultRouteAlreadyAttacked,

        /// <summary>索敌步数范围内无存活可达目标，按内置路线继续移动</summary>
        DefaultRouteNoTarget,

        /// <summary>未决 (缺少搜索范围或路口映射)</summary>
        Unresolved
    }

    /// <summary>
    /// 预测置信度分级 (Phase 2D-5B)
    /// 严禁将所有预测声称为 Exact Server AI
    /// </summary>
    public enum PredictionConfidence
    {
        /// <summary>高置信度：唯一最近目标或已攻击/无目标沿内置路线</summary>
        High,

        /// <summary>中置信度：触发了用户规则 FrontMost 进度决胜</summary>
        Medium,

        /// <summary>低置信度：触发了 RED > BLUE > GREEN 分支优先级决胜</summary>
        Low,

        /// <summary>未决：必要参数未知</summary>
        Unresolved
    }

    /// <summary>
    /// 交叉路口分支颜色优先级
    /// RED (0) > BLUE (1) > GREEN (2)
    /// </summary>
    public enum BranchColor
    {
        Red = 0,
        Blue = 1,
        Green = 2,
        None = -1
    }

    /// <summary>
    /// 交叉路口单点分支映射项
    /// 严格基于真实 NodeId，严禁使用屏幕坐标
    /// </summary>
    public struct BranchPriorityEntry
    {
        public int RedNextNode;
        public int BlueNextNode;
        public int GreenNextNode;

        public BranchPriorityEntry(int red, int blue, int green)
        {
            RedNextNode = red;
            BlueNextNode = blue;
            GreenNextNode = green;
        }

        public int GetPriority(int nextNodeId)
        {
            if (RedNextNode > 0 && nextNodeId == RedNextNode) return 0;
            if (BlueNextNode > 0 && nextNodeId == BlueNextNode) return 1;
            if (GreenNextNode > 0 && nextNodeId == GreenNextNode) return 2;
            return 999;
        }
    }

    /// <summary>
    /// 候选玩家输入结构
    /// </summary>
    public struct CandidatePlayer
    {
        public long PlayerId;
        public int NodeId;
        public float RouteProgress;
        public bool RouteProgressKnown;
        public int Slot;
        public bool SlotKnown;
        public bool IsAlive;
        public bool IsValid;

        public CandidatePlayer(long playerId, int nodeId, float routeProgress, bool routeProgressKnown, int slot, bool slotKnown, bool isAlive, bool isValid)
        {
            PlayerId = playerId;
            NodeId = nodeId;
            RouteProgress = routeProgress;
            RouteProgressKnown = routeProgressKnown;
            Slot = slot;
            SlotKnown = slotKnown;
            IsAlive = isAlive;
            IsValid = isValid;
        }

        public CandidatePlayer(long playerId, int nodeId, float routeProgress, int slot, bool isAlive, bool isValid)
            : this(playerId, nodeId, routeProgress, true, slot, true, isAlive, isValid)
        {
        }
    }

    /// <summary>
    /// 纯计算索敌预测输入
    /// </summary>
    public struct TargetPredictionInput
    {
        public long MonsterId;
        public int CurrentNode;
        public int PostMoveNode;
        public bool PostMoveNodeKnown;
        public bool HasAttackedThisTurn;
        public bool HasAttackedKnown;
        public int SearchRange;
        public bool SearchRangeKnown;
        public bool DefaultRouteKnown;
        public bool GraphKnown;
        public bool MonsterNodeKnown;
        public bool PlayersKnown;
        public IReadOnlyList<CandidatePlayer> Players;
        public IReadOnlyDictionary<int, List<int>> GraphEdges;
        public IReadOnlyDictionary<int, BranchPriorityEntry> BranchPriorities;
        public IReadOnlyList<int> DefaultRoute;
    }

    /// <summary>
    /// 索敌预测结果
    /// </summary>
    public struct MonsterTargetPrediction
    {
        public long MonsterId;
        public long TargetPlayerId;
        public int TargetNodeId;
        public int CurrentNodeId;
        public int PostMoveNodeId;
        public int Distance;
        public List<int> Path;
        public PredictionReason Reason;
        public PredictionConfidence Confidence;
        public string Details;
    }
}
