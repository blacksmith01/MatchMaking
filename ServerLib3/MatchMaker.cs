using CommonLib;
using System;
using System.Collections.Generic;
using System.Text;


namespace ServerLib
{
    public class MatchMaker
    {
        const int DEFAULT_CAPACITY_SIZE = 2000;

        List<MatchingNode> _tempList = new(DEFAULT_CAPACITY_SIZE);
        List<MatchingNode> _sortedTime = new(DEFAULT_CAPACITY_SIZE);
        List<MatchingNode> _sortedPoint = new(DEFAULT_CAPACITY_SIZE);
        int _matched_count;

        class MatchedContext
        {
            public SecCnt TimeSec;
            public MatchingNode[] Nodes = new MatchingNode[GameConstants.MAX_PLAYER_COUNT_IN_ROOM];
            public Int32 NodeCount = 0;

            public void Init(TickCnt now)
            {
                TimeSec = TimeEx.GetUtcSecCount(now);
                ClearInLoop();
            }
            public void Clear()
            {
                NodeCount = 0;
                for (int i = 0; i < GameConstants.MAX_PLAYER_COUNT_IN_ROOM; i++)
                {
                    Nodes[i] = null;
                }
            }
            public void ClearInLoop()
            {
                NodeCount = 0;
            }
            public void Add(MatchingNode node)
            {
                Nodes[NodeCount] = node;
                NodeCount++;
            }
        }
        MatchedContext matchCtx = new();

        public void Build(List<MatchingNode> adds, List<MatchingNode> dels)
        {
            if (_matched_count == 0 && adds.Count == 0 && dels.Count == 0)
            {
                // 추가/삭제가 없다면 리스트를 재구축할 필요가 없다.
                return;
            }

            _tempList.Clear();
            _tempList.EnsureCapacity(_sortedPoint.Count + adds.Count - dels.Count);

            // 점수순으로 정렬시킨다.
            adds.Sort(MatchingNode.DefaultComparerByPoint);
            var addIdx = adds.Count;
            var addRemains = adds.Count;
            var cmpPoint = addIdx < adds.Count ? adds[addIdx].Point : GamePoint.MaxValue;

            // 기존에 정렬되어 있던 리스트를 순회하면서 추가/삭제를 하고 Swap한다.
            foreach (var node in _sortedPoint)
            {
                if (node.IsDelReserved || node.IsGameMatched)
                {
                    continue;
                }
                if (cmpPoint < node.Point)
                {
                    AddToTempListPoint(node);
                    addIdx++;
                    cmpPoint = addIdx < adds.Count ? adds[addIdx].Point : GamePoint.MaxValue;
                }
                AddToTempListPoint(node);
            }
            for (; addIdx < adds.Count; addIdx++)
            {
                AddToTempListPoint(adds[addIdx]);
            }
            ClassUtils.Swap(ref _tempList, ref _sortedPoint);


            // OnSchedule 간격이 짧기 때문에 시간순으로 정렬시키지 않고 추가/삭제 후 Swap한다.
            _tempList.Clear();
            foreach (var node in _sortedTime)
            {
                if (node.IsDelReserved || node.IsGameMatched)
                {
                    continue;
                }
                AddToTempListRegTime(node);
            }
            for (; addIdx < adds.Count; addIdx++)
            {
                AddToTempListRegTime(adds[addIdx]);
            }
            ClassUtils.Swap(ref _tempList, ref _sortedPoint);
        }

        void AddToTempListPoint(MatchingNode node)
        {
            node.IdxPoint = _tempList.Count;
            _tempList.Add(node);
        }
        void AddToTempListRegTime(MatchingNode node)
        {
            node.IdxRegtime = _tempList.Count;
            _tempList.Add(node);
        }

        public void ProcessMatch(TickCnt now, Action<IEnumerable<MatchingNode>> onMatched)
        {
            _matched_count = 0;
            matchCtx.Init(now);

            foreach (var myNode in _sortedTime)
            {
                if (myNode.IsGameMatched)
                {
                    continue;
                }

                if (ProcessMatch(myNode))
                {
                    onMatched(matchCtx.Nodes);
                    _matched_count++;
                }

                matchCtx.ClearInLoop();
            }

            matchCtx.Clear();
        }

        bool ProcessMatch(MatchingNode myNode)
        {
            var myPt = myNode.Point;
            var myPtBound = GameConstants.GetPointBound_ByRegistTime(matchCtx.TimeSec - myNode.RegTime);

            // 포인트 정렬 리스트에서 현재 플레이어 기준으로 좌(less), 우(high)를
            // 순차적으로 점수를 비교해서 매칭 여부를 확인한다.
            bool noMoreLesser = false;
            bool noMoreHigher = false;
            var pt_list_size = _sortedTime.Count;
            for (int i = 0; i < pt_list_size; i++)
            {
                int dir = (i & 1); // 0, 1, 0, 1, ...
                int dist = ((i / 2) + 1) * (1 - dir * 2); // 1, -1, 2, -2, 3, -3, ...
                int op_idx = myNode.IdxPoint + dist;
                if (op_idx < 0)
                {
                    noMoreLesser = true;
                    if (noMoreHigher)
                    {
                        return false;
                    }
                }
                else if (op_idx >= pt_list_size)
                {
                    noMoreHigher = true;
                    if (noMoreLesser)
                    {
                        return false;
                    }
                }
                else
                {
                    var opNode = _sortedPoint[op_idx];
                    var opPt = opNode.Point;
                    var opPtBound = GameConstants.GetPointBound_ByRegistTime(matchCtx.TimeSec - opNode.RegTime);
                    if (dist < 0)
                    {
                        // ... op <- my -> ....
                        if (opPt < myPt - myPtBound)
                        {
                            noMoreLesser = true;
                            if (noMoreHigher)
                            {
                                return false;
                            }
                            continue;
                        }
                        else if (opNode.IsGameMatched || myPt > opPt + opPtBound)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        // ... <- my -> op ....
                        if (opPt > myPt + myPtBound)
                        {
                            noMoreHigher = true;
                            if (noMoreLesser)
                            {
                                return false;
                            }
                            continue;
                        }
                        else if (opNode.IsGameMatched || myPt < opPt - opPtBound)
                        {
                            continue;
                        }
                    }

                    matchCtx.Add(opNode);

                    if (matchCtx.NodeCount + 1 >= GameConstants.MAX_PLAYER_COUNT_IN_ROOM)
                    {
                        matchCtx.Add(myNode);
                        return true;
                    }

                }
            }

            return false;
        }
    }
}
