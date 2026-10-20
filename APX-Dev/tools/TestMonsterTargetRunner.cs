using System;
using System.Collections.Generic;
using APXRuntime.Targeting;

namespace APX.Tests
{
    internal static class Program
    {
        private static int _passedCount = 0;
        private static int _totalCount = 33;

        private static void AssertCheck(string name, bool condition, string details)
        {
            if (condition)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  [PASS] " + name);
                Console.ResetColor();
                _passedCount++;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  [FAIL] " + name + " - " + details);
                Console.ResetColor();
            }
        }

        public static int Main(string[] args)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("MonsterTargetMath + Maximum Dice Preview: 33 Test Cases");
            Console.WriteLine("==================================================");

            // --- Case 1: 直线单目标（唯一最短距离） ---
            var c1Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                DefaultRouteKnown = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = new Dictionary<int, List<int>>
                {
                    { 1, new List<int> { 2 } },
                    { 2, new List<int> { 3 } },
                    { 3, new List<int> { 4 } }
                },
                Players = new List<CandidatePlayer>
                {
                    new CandidatePlayer(1001L, 3, 10f, 0, true, true)
                }
            };
            var r1 = MonsterTargetMath.Predict(c1Input);
            bool c1Ok = r1.TargetPlayerId == 1001L && r1.Distance == 2 && r1.Reason == PredictionReason.NearestTarget && r1.Confidence == PredictionConfidence.High;
            AssertCheck("Case 1: 直线单目标（唯一最短距离）", c1Ok, string.Format("Target={0}, Dist={1}, Reason={2}", r1.TargetPlayerId, r1.Distance, r1.Reason));

            // --- Case 2: 距离并列，FrontMost 决胜 ---
            var c2Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                DefaultRouteKnown = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = new Dictionary<int, List<int>>
                {
                    { 1, new List<int> { 2, 4 } },
                    { 2, new List<int> { 3 } },
                    { 4, new List<int> { 5 } }
                },
                Players = new List<CandidatePlayer>
                {
                    new CandidatePlayer(1001L, 3, 50f, 1, true, true), // P1 progress 50
                    new CandidatePlayer(1002L, 5, 20f, 0, true, true)  // P2 progress 20
                }
            };
            var r2 = MonsterTargetMath.Predict(c2Input);
            bool c2Ok = r2.TargetPlayerId == 1001L && r2.Distance == 2 && r2.Reason == PredictionReason.FrontMostTieBreak && r2.Confidence == PredictionConfidence.Medium;
            AssertCheck("Case 2: 距离并列，FrontMost 决胜", c2Ok, string.Format("Target={0}, Dist={1}, Reason={2}", r2.TargetPlayerId, r2.Distance, r2.Reason));

            // --- Case 3: 距离并列，FrontMost 仍并列，同路径无分叉，Slot 决胜 ---
            var c3Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                DefaultRouteKnown = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = c1Input.GraphEdges,
                Players = new List<CandidatePlayer>
                {
                    new CandidatePlayer(1001L, 3, 30f, 0, true, true), // Slot 0
                    new CandidatePlayer(1002L, 3, 30f, 1, true, true)  // Slot 1
                }
            };
            var r3 = MonsterTargetMath.Predict(c3Input);
            bool c3Ok = r3.TargetPlayerId == 1001L && r3.Distance == 2 && r3.Reason == PredictionReason.SlotTieBreak && r3.Confidence == PredictionConfidence.Medium;
            AssertCheck("Case 3: 距离并列，FrontMost 仍并列，Slot 决胜", c3Ok, string.Format("Target={0}, Reason={1}", r3.TargetPlayerId, r3.Reason));

            // --- Case 4: 分叉路口 RED 优先 ---
            var c4Branches = new Dictionary<int, BranchPriorityEntry>
            {
                { 1, new BranchPriorityEntry(10, 20, 0) } // 10=RED, 20=BLUE
            };
            var c4Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                DefaultRouteKnown = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = new Dictionary<int, List<int>>
                {
                    { 1, new List<int> { 10, 20 } },
                    { 10, new List<int> { 11 } },
                    { 20, new List<int> { 21 } }
                },
                BranchPriorities = c4Branches,
                Players = new List<CandidatePlayer>
                {
                    new CandidatePlayer(1001L, 11, 25f, 1, true, true), // P1 on RED, slot 1
                    new CandidatePlayer(1002L, 21, 25f, 0, true, true)  // P2 on BLUE, slot 0
                }
            };
            var r4 = MonsterTargetMath.Predict(c4Input);
            bool c4Ok = r4.TargetPlayerId == 1001L && r4.Distance == 2 && r4.Reason == PredictionReason.BranchPriorityTieBreak && r4.Confidence == PredictionConfidence.Low;
            AssertCheck("Case 4: 分叉路口 RED 优先", c4Ok, string.Format("Target={0}, Reason={1}", r4.TargetPlayerId, r4.Reason));

            // --- Case 5: 分叉路口 BLUE 优先（RED 不通/无目标） ---
            var c5Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                DefaultRouteKnown = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = new Dictionary<int, List<int>>
                {
                    { 1, new List<int> { 10, 20 } },
                    { 10, new List<int>() }, // dead end
                    { 20, new List<int> { 21 } }
                },
                BranchPriorities = c4Branches,
                Players = new List<CandidatePlayer>
                {
                    new CandidatePlayer(1002L, 21, 25f, 0, true, true) // Only P2 on BLUE
                }
            };
            var r5 = MonsterTargetMath.Predict(c5Input);
            bool c5Ok = r5.TargetPlayerId == 1002L && r5.Distance == 2 && r5.Reason == PredictionReason.NearestTarget && r5.Confidence == PredictionConfidence.High;
            AssertCheck("Case 5: 分叉路口 BLUE 优先（RED 不通/无目标）", c5Ok, string.Format("Target={0}, Reason={1}", r5.TargetPlayerId, r5.Reason));

            // --- Case 6: 分叉路口 GREEN 优先（RED/BLUE 不通/无目标） ---
            var c6Branches = new Dictionary<int, BranchPriorityEntry>
            {
                { 1, new BranchPriorityEntry(10, 20, 30) } // 10=RED, 20=BLUE, 30=GREEN
            };
            var c6Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                DefaultRouteKnown = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = new Dictionary<int, List<int>>
                {
                    { 1, new List<int> { 10, 20, 30 } },
                    { 10, new List<int>() },
                    { 20, new List<int>() },
                    { 30, new List<int> { 31 } }
                },
                BranchPriorities = c6Branches,
                Players = new List<CandidatePlayer>
                {
                    new CandidatePlayer(1003L, 31, 25f, 0, true, true) // Only P3 on GREEN
                }
            };
            var r6 = MonsterTargetMath.Predict(c6Input);
            bool c6Ok = r6.TargetPlayerId == 1003L && r6.Distance == 2 && r6.Reason == PredictionReason.NearestTarget && r6.Confidence == PredictionConfidence.High;
            AssertCheck("Case 6: 分叉路口 GREEN 优先（RED/BLUE 不通/无目标）", c6Ok, string.Format("Target={0}, Reason={1}", r6.TargetPlayerId, r6.Reason));

            // --- Case 7: 超出 SearchRange，Fallback 到 DefaultRouteNoTarget ---
            var c7Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                DefaultRouteKnown = true,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = new Dictionary<int, List<int>>
                {
                    { 1, new List<int> { 2 } },
                    { 2, new List<int> { 3 } },
                    { 3, new List<int> { 4 } },
                    { 4, new List<int> { 5 } },
                    { 5, new List<int> { 6 } }
                },
                DefaultRoute = new List<int> { 1, 2, 3 },
                Players = new List<CandidatePlayer>
                {
                    new CandidatePlayer(1005L, 6, 60f, 0, true, true) // distance 5 > SearchRange 3
                }
            };
            var r7 = MonsterTargetMath.Predict(c7Input);
            bool c7Ok = r7.TargetPlayerId == 0L && r7.Reason == PredictionReason.DefaultRouteNoTarget && r7.Confidence == PredictionConfidence.High;
            AssertCheck("Case 7: 超出 SearchRange，Fallback 到 DefaultRouteNoTarget", c7Ok, string.Format("Target={0}, Reason={1}", r7.TargetPlayerId, r7.Reason));

            // --- Case 8: HasAttackedThisTurn == true，Fallback 到 DefaultRouteAlreadyAttacked ---
            var c8Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = true, // Monster already attacked
                DefaultRouteKnown = true,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = c1Input.GraphEdges,
                DefaultRoute = new List<int> { 1, 2 },
                Players = c1Input.Players
            };
            var r8 = MonsterTargetMath.Predict(c8Input);
            bool c8Ok = r8.TargetPlayerId == 0L && r8.Reason == PredictionReason.DefaultRouteAlreadyAttacked && r8.Confidence == PredictionConfidence.High;
            AssertCheck("Case 8: HasAttackedThisTurn == true，Fallback 到 DefaultRouteAlreadyAttacked", c8Ok, string.Format("Target={0}, Reason={1}", r8.TargetPlayerId, r8.Reason));

            // --- Case 9: SearchRange 未知或未决，返回 Unresolved ---
            var c9Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = false, // Unknown
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                DefaultRouteKnown = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = c1Input.GraphEdges,
                Players = c1Input.Players
            };
            var r9 = MonsterTargetMath.Predict(c9Input);
            bool c9Ok = r9.TargetPlayerId == 0L && r9.Reason == PredictionReason.Unresolved && r9.Confidence == PredictionConfidence.Unresolved;
            AssertCheck("Case 9: SearchRange 未知或未决，返回 Unresolved", c9Ok, string.Format("Target={0}, Reason={1}", r9.TargetPlayerId, r9.Reason));

            // --- Case 10: 环形拓扑无死循环，正确找到目标 ---
            var c10Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 5,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                DefaultRouteKnown = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = new Dictionary<int, List<int>>
                {
                    { 1, new List<int> { 2 } },
                    { 2, new List<int> { 3 } },
                    { 3, new List<int> { 1, 4 } }, // 3 -> 1 cycle, 3 -> 4 path to target
                    { 4, new List<int>() }
                },
                Players = new List<CandidatePlayer>
                {
                    new CandidatePlayer(1004L, 4, 10f, 0, true, true)
                }
            };
            var r10 = MonsterTargetMath.Predict(c10Input);
            string c10PathStr = string.Join("->", r10.Path != null ? r10.Path.ConvertAll(i => i.ToString()).ToArray() : new string[0]);
            bool c10Ok = r10.TargetPlayerId == 1004L && r10.Distance == 3 && c10PathStr == "1->2->3->4" && r10.Reason == PredictionReason.NearestTarget;
            AssertCheck("Case 10: 环形拓扑无死循环，正确找到目标", c10Ok, string.Format("Target={0}, Dist={1}, Path={2}", r10.TargetPlayerId, r10.Distance, c10PathStr));

            // --- Case 11: SearchRange Unknown -> Unresolved ---
            var c11Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 0,
                SearchRangeKnown = false, // Unknown
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = c1Input.GraphEdges,
                Players = c1Input.Players
            };
            var r11 = MonsterTargetMath.Predict(c11Input);
            bool c11Ok = r11.TargetPlayerId == 0L && r11.Reason == PredictionReason.Unresolved && r11.Confidence == PredictionConfidence.Unresolved;
            AssertCheck("Case 11: SearchRange Unknown -> Unresolved", c11Ok, string.Format("Reason={0}, Confidence={1}", r11.Reason, r11.Confidence));

            // --- Case 12: HasAttacked Unknown -> Unresolved ---
            var c12Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = false, // Unknown
                HasAttackedThisTurn = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = c1Input.GraphEdges,
                Players = c1Input.Players
            };
            var r12 = MonsterTargetMath.Predict(c12Input);
            bool c12Ok = r12.TargetPlayerId == 0L && r12.Reason == PredictionReason.Unresolved && r12.Confidence == PredictionConfidence.Unresolved;
            AssertCheck("Case 12: HasAttacked Unknown -> Unresolved", c12Ok, string.Format("Reason={0}, Confidence={1}", r12.Reason, r12.Confidence));

            // --- Case 13: PostMoveNode Unknown -> Unresolved ---
            var c13Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 0,
                PostMoveNodeKnown = false, // Unknown
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = c1Input.GraphEdges,
                Players = c1Input.Players
            };
            var r13 = MonsterTargetMath.Predict(c13Input);
            bool c13Ok = r13.TargetPlayerId == 0L && r13.Reason == PredictionReason.Unresolved && r13.Confidence == PredictionConfidence.Unresolved;
            AssertCheck("Case 13: PostMoveNode Unknown -> Unresolved", c13Ok, string.Format("Reason={0}, Confidence={1}", r13.Reason, r13.Confidence));

            // --- Case 14: Branch Mapping Missing (不同分支路径但无 BranchPriorityEntry -> 严禁使用 SlotTieBreak，必须 Unresolved) ---
            var c14Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = new Dictionary<int, List<int>>
                {
                    { 1, new List<int> { 10, 20 } },
                    { 10, new List<int> { 11 } },
                    { 20, new List<int> { 21 } }
                },
                BranchPriorities = null, // Missing mapping!
                Players = new List<CandidatePlayer>
                {
                    new CandidatePlayer(1001L, 11, 25f, 1, true, true), // P1 on branch 10, slot 1
                    new CandidatePlayer(1002L, 21, 25f, 0, true, true)  // P2 on branch 20, slot 0
                }
            };
            var r14 = MonsterTargetMath.Predict(c14Input);
            bool c14Ok = r14.TargetPlayerId == 0L && r14.Reason == PredictionReason.Unresolved && r14.Confidence == PredictionConfidence.Unresolved && r14.Reason != PredictionReason.SlotTieBreak;
            AssertCheck("Case 14: Branch Mapping Missing -> Unresolved (严禁 SlotTieBreak)", c14Ok, string.Format("Target={0}, Reason={1}, Confidence={2}", r14.TargetPlayerId, r14.Reason, r14.Confidence));

            // --- Case 15: Branch Mapping Known 且全平局 -> 允许 SlotTieBreak ---
            var c15Branches = new Dictionary<int, BranchPriorityEntry>
            {
                { 1, new BranchPriorityEntry(10, 20, 0) } // Node 1 mapped: 10=RED(0), 20=BLUE(1)
            };
            var c15Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = new Dictionary<int, List<int>>
                {
                    { 1, new List<int> { 10, 20 } },
                    { 10, new List<int> { 11 } },
                    { 20, new List<int> { 21 } }
                },
                BranchPriorities = c15Branches,
                Players = new List<CandidatePlayer>
                {
                    // 两位玩家均在 RED 分支终点 11（已知且分支得分全为 0 平局）
                    new CandidatePlayer(1001L, 11, 25f, 1, true, true), // Slot 1
                    new CandidatePlayer(1002L, 11, 25f, 0, true, true)  // Slot 0
                }
            };
            var r15 = MonsterTargetMath.Predict(c15Input);
            bool c15Ok = r15.TargetPlayerId == 1002L && r15.Reason == PredictionReason.SlotTieBreak && r15.Confidence == PredictionConfidence.Medium;
            AssertCheck("Case 15: Branch Mapping Known 且全平局 -> 允许 SlotTieBreak", c15Ok, string.Format("Target={0}, Reason={1}, Confidence={2}", r15.TargetPlayerId, r15.Reason, r15.Confidence));

            // --- Case 16: Known HasAttacked = false -> 正常流程 ---
            var c16Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true, // Known
                HasAttackedThisTurn = false, // Not attacked
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = c1Input.GraphEdges,
                Players = c1Input.Players
            };
            var r16 = MonsterTargetMath.Predict(c16Input);
            bool c16Ok = r16.TargetPlayerId == 1001L && r16.Distance == 2 && r16.Reason == PredictionReason.NearestTarget && r16.Confidence == PredictionConfidence.High;
            AssertCheck("Case 16: Known HasAttacked = false -> 正常流程", c16Ok, string.Format("Target={0}, Dist={1}, Reason={2}", r16.TargetPlayerId, r16.Distance, r16.Reason));

            // --- Case 17: GraphKnown = false 但 GraphEdges 有数据 -> Unresolved ---
            var c17Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                GraphKnown = false, // False!
                PlayersKnown = true,
                GraphEdges = c1Input.GraphEdges,
                Players = c1Input.Players
            };
            var r17 = MonsterTargetMath.Predict(c17Input);
            bool c17Ok = r17.TargetPlayerId == 0L && r17.Reason == PredictionReason.Unresolved && r17.Confidence == PredictionConfidence.Unresolved;
            AssertCheck("Case 17: GraphKnown = false 但 GraphEdges 有数据 -> Unresolved", c17Ok, string.Format("Reason={0}, Confidence={1}", r17.Reason, r17.Confidence));

            // --- Case 18: MonsterNodeKnown = false -> Unresolved ---
            var c18Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = false, // False!
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = c1Input.GraphEdges,
                Players = c1Input.Players
            };
            var r18 = MonsterTargetMath.Predict(c18Input);
            bool c18Ok = r18.TargetPlayerId == 0L && r18.Reason == PredictionReason.Unresolved && r18.Confidence == PredictionConfidence.Unresolved;
            AssertCheck("Case 18: MonsterNodeKnown = false -> Unresolved", c18Ok, string.Format("Reason={0}, Confidence={1}", r18.Reason, r18.Confidence));

            // --- Case 19: PlayersKnown = false 且 Players 为空 -> Unresolved (严禁误判为 DefaultRouteNoTarget) ---
            var c19Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                GraphKnown = true,
                PlayersKnown = false, // False!
                GraphEdges = c1Input.GraphEdges,
                DefaultRouteKnown = true,
                DefaultRoute = new List<int> { 1, 2 },
                Players = new List<CandidatePlayer>() // Empty list
            };
            var r19 = MonsterTargetMath.Predict(c19Input);
            bool c19Ok = r19.TargetPlayerId == 0L && r19.Reason == PredictionReason.Unresolved && r19.Confidence == PredictionConfidence.Unresolved && r19.Reason != PredictionReason.DefaultRouteNoTarget;
            AssertCheck("Case 19: PlayersKnown = false 且 Players empty -> Unresolved (严禁 DefaultRouteNoTarget)", c19Ok, string.Format("Reason={0}, Confidence={1}", r19.Reason, r19.Confidence));

            // --- Case 20: PlayersKnown = true 且已知没有有效玩家 -> DefaultRouteNoTarget ---
            var c20Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                GraphKnown = true,
                PlayersKnown = true, // Known!
                DefaultRouteKnown = true,
                DefaultRoute = new List<int> { 1, 2 },
                GraphEdges = c1Input.GraphEdges,
                Players = new List<CandidatePlayer>() // Empty known list
            };
            var r20 = MonsterTargetMath.Predict(c20Input);
            bool c20Ok = r20.TargetPlayerId == 0L && r20.Reason == PredictionReason.DefaultRouteNoTarget && r20.Confidence == PredictionConfidence.High;
            AssertCheck("Case 20: PlayersKnown = true 且已知没有有效玩家 -> DefaultRouteNoTarget", c20Ok, string.Format("Reason={0}, Confidence={1}", r20.Reason, r20.Confidence));

            // --- Case 21: 距离并列，一个候选 RouteProgressKnown = false -> Unresolved (前级证据缺失严禁跳级) ---
            var c21Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = c2Input.GraphEdges,
                Players = new List<CandidatePlayer>
                {
                    new CandidatePlayer(1001L, 3, 50f, true, 1, true, true, true), // Known progress
                    new CandidatePlayer(1002L, 5, 20f, false, 0, true, true, true)  // RouteProgressKnown = false!
                }
            };
            var r21 = MonsterTargetMath.Predict(c21Input);
            bool c21Ok = r21.TargetPlayerId == 0L && r21.Reason == PredictionReason.Unresolved && r21.Confidence == PredictionConfidence.Unresolved;
            AssertCheck("Case 21: 距离并列，候选 RouteProgressKnown = false -> Unresolved", c21Ok, string.Format("Reason={0}, Confidence={1}", r21.Reason, r21.Confidence));

            // --- Case 22: 需要 SlotTieBreak，一个候选 SlotKnown = false -> Unresolved (严禁数组 index fallback) ---
            var c22Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = c1Input.GraphEdges,
                Players = new List<CandidatePlayer>
                {
                    new CandidatePlayer(1001L, 3, 30f, true, 0, true, true, true),  // SlotKnown = true
                    new CandidatePlayer(1002L, 3, 30f, true, -1, false, true, true) // SlotKnown = false!
                }
            };
            var r22 = MonsterTargetMath.Predict(c22Input);
            bool c22Ok = r22.TargetPlayerId == 0L && r22.Reason == PredictionReason.Unresolved && r22.Confidence == PredictionConfidence.Unresolved && r22.Reason != PredictionReason.SlotTieBreak;
            AssertCheck("Case 22: 需要 SlotTieBreak，候选 SlotKnown = false -> Unresolved", c22Ok, string.Format("Reason={0}, Confidence={1}", r22.Reason, r22.Confidence));

            // --- Case 23: HasAttackedKnown = true 且 HasAttackedThisTurn = true，即使 SearchRangeKnown = false 也返回 DefaultRouteAlreadyAttacked ---
            var c23Input = new TargetPredictionInput
            {
                MonsterId = 1L,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 0,
                SearchRangeKnown = false, // SearchRange is unknown!
                HasAttackedKnown = true,
                HasAttackedThisTurn = true, // Monster already attacked!
                DefaultRouteKnown = true,
                DefaultRoute = new List<int> { 1, 2 },
                GraphKnown = false,
                PlayersKnown = false,
                GraphEdges = null,
                Players = null
            };
            var r23 = MonsterTargetMath.Predict(c23Input);
            bool c23Ok = r23.TargetPlayerId == 0L && r23.Reason == PredictionReason.DefaultRouteAlreadyAttacked && r23.Confidence == PredictionConfidence.High;
            AssertCheck("Case 23: HasAttacked=True + SearchRange=Unknown -> DefaultRouteAlreadyAttacked", c23Ok, string.Format("Reason={0}, Confidence={1}", r23.Reason, r23.Confidence));

            // --- Case 24: long MonsterId roundtrip (> Int32.MaxValue) 验证无截断 ---
            long largeMonsterId = 5000000000L; // > Int32.MaxValue (2147483647)
            var c24Input = new TargetPredictionInput
            {
                MonsterId = largeMonsterId,
                CurrentNode = 1,
                MonsterNodeKnown = true,
                PostMoveNode = 1,
                PostMoveNodeKnown = true,
                SearchRange = 3,
                SearchRangeKnown = true,
                HasAttackedKnown = true,
                HasAttackedThisTurn = false,
                GraphKnown = true,
                PlayersKnown = true,
                GraphEdges = c1Input.GraphEdges,
                Players = c1Input.Players
            };
            var r24 = MonsterTargetMath.Predict(c24Input);
            bool c24Ok = r24.MonsterId == largeMonsterId && r24.TargetPlayerId == 1001L;
            AssertCheck("Case 24: long MonsterId roundtrip (> Int32.MaxValue) 验证无截断", c24Ok, string.Format("MonsterId={0}", r24.MonsterId));

            RunMaximumDicePreviewTests();

            Console.WriteLine("==================================================");
            Console.WriteLine(string.Format("Results: {0} / {1} Passed", _passedCount, _totalCount));
            Console.WriteLine("==================================================");

            return _passedCount == _totalCount ? 0 : 1;
        }

        private static void RunMaximumDicePreviewTests()
        {
            var graph = new Dictionary<int, List<int>>();
            for (int node = 1; node <= 12; node++) graph[node] = node < 12 ? new List<int> { node + 1 } : new List<int>();
            var input = new TargetPredictionInput
            {
                MonsterId = 5000000000L, CurrentNode = 1, MonsterNodeKnown = true,
                GraphKnown = true, PlayersKnown = true, GraphEdges = graph,
                Players = new List<CandidatePlayer> { new CandidatePlayer(9000000000L, 11, 0f, false, 0, true, true, true) }
            };
            var edge = MonsterTargetPreview.Calculate(input, new[] { 2 });
            AssertCheck("Case 25: 最大10格含先行1格，边界目标可达", edge.IsValid && edge.Candidates.Count == 1 && edge.Candidates[0].Distance == 10 && edge.Candidates[0].Path.Count == 11, "10-step boundary failed");

            input.Players = new[] { new CandidatePlayer(1001L, 12, 0f, false, 0, true, true, true) };
            var outside = MonsterTargetPreview.Calculate(input, new[] { 2 });
            AssertCheck("Case 26: 第11格不误报为10格内", outside.IsValid && outside.Candidates.Count == 0, "budget off by one");

            var fork = new Dictionary<int, List<int>>
            {
                { 1, new List<int> { 2, 3 } }, { 2, new List<int> { 4 } },
                { 3, new List<int> { 5 } }, { 4, new List<int>() }, { 5, new List<int>() }
            };
            input.GraphEdges = fork;
            input.Players = new[]
            {
                new CandidatePlayer(1001L, 4, 0f, false, 0, true, true, true),
                new CandidatePlayer(1002L, 5, 0f, false, 1, true, true, true)
            };
            var branches = MonsterTargetPreview.Calculate(input, new[] { 2, 3 });
            AssertCheck("Case 27: 未映射岔路保留不同目标", branches.IsValid && branches.AmbiguousFirstStep && branches.Candidates.Count == 2, "fork was guessed");

            input.GraphEdges = new Dictionary<int, List<int>> { { 1, new List<int> { 2 } }, { 2, new List<int> { 4, 5 } }, { 4, new List<int>() }, { 5, new List<int>() } };
            var tied = MonsterTargetPreview.Calculate(input, new[] { 2 });
            AssertCheck("Case 28: 同距离且位置靠前未知，保留双方", tied.TiedTargets && tied.Candidates.Count == 2 && tied.Candidates.TrueForAll(p => p.Confidence == PredictionConfidence.Unresolved), "tie collapsed");

            input.Players = new[] { new CandidatePlayer(1001L, 4, 0f, false, 0, true, false, true), new CandidatePlayer(1002L, 5, 0f, false, 1, true, true, true) };
            var dead = MonsterTargetPreview.Calculate(input, new[] { 2 });
            AssertCheck("Case 29: 死亡玩家不参与索敌", dead.Candidates.Count == 1 && dead.Candidates[0].TargetPlayerId == 1002L, "dead target included");

            input.HasAttackedKnown = input.HasAttackedThisTurn = true;
            var attacked = MonsterTargetPreview.Calculate(input, null);
            AssertCheck("Case 30: 本回合已攻击时停止追击推演", attacked.IsValid && attacked.AlreadyAttacked && attacked.Candidates.Count == 0, "attack state ignored");
            input.HasAttackedKnown = input.HasAttackedThisTurn = false;
            var invalid = MonsterTargetPreview.Calculate(input, new[] { 99 });
            AssertCheck("Case 31: 非邻接或缺失首步不当作当前格", !invalid.IsValid && invalid.Candidates.Count == 0, "invalid first step accepted");

            input.GraphEdges = fork;
            input.Players = new[] { new CandidatePlayer(1001L, 4, 0f, false, 0, true, true, true) };
            var partial = MonsterTargetPreview.Calculate(input, new[] { 2, 3, 2 });
            AssertCheck("Case 32: 部分岔路无目标时保留不确定性", partial.AmbiguousFirstStep && partial.SomeRoutesHaveNoTarget && partial.Candidates.Count == 1, "partial fork uncertainty lost");

            input.Players = new[] { new CandidatePlayer(1001L, 2, 0f, false, 0, true, true, true) };
            var firstStep = MonsterTargetPreview.Calculate(input, new[] { 2 }, 1);
            AssertCheck("Case 33: 首步即到目标且无剩余步数", firstStep.IsValid && firstStep.Candidates.Count == 1 && firstStep.Candidates[0].Distance == 1, "zero remaining budget failed");
        }
    }
}
