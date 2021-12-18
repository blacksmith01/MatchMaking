using CommonLib;
using System;
using System.Collections.Generic;
using System.Text;


namespace ServerLib.Services.Content
{
    public partial class MatchMakerService
    {
        List<MatchingNode> _matchTempList = new(SHARDED_LIST_CAPACITY_SIZE * (int)SHARDED_GROUP_SIZE);
        List<MatchingNode> _matchSortedTime = new(SHARDED_LIST_CAPACITY_SIZE * (int)SHARDED_GROUP_SIZE);
        List<MatchingNode> _matchSortedPoint = new(SHARDED_LIST_CAPACITY_SIZE * (int)SHARDED_GROUP_SIZE);
        int _matchedInLastProcess;

        class MatchingContext
        {
            public TimeT TimeSec;
            public MatchingNode[] Nodes = new MatchingNode[GameConstants.MAX_PLAYER_COUNT_IN_ROOM];
            public Int32 NodeCount = 0;

            public void Init(TimeT64 now)
            {
                TimeSec = (TimeT)(now / TimeEx.Duration_Sec_To_Ms);
                ClearInLoop();
            }
            public void Clear()
            {
                NodeCount = 0;
                for (int i = 0; i < GameConstants.MAX_PLAYER_COUNT_IN_ROOM; i++)
                {
                    Nodes[i] = null!;
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

            public bool CheckNodePoint(MatchingNode newNode)
            {
                for (int i = 0; i < NodeCount; i++)
                {
                    var node = Nodes[i];
                    if ((node.Point < newNode.Point - newNode.PointBound) || (node.Point > newNode.Point + newNode.PointBound))
                    {
                        return false;
                    }

                    if ((newNode.Point < node.Point - node.PointBound) || (newNode.Point > node.Point + node.PointBound))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
        MatchingContext _matchingCtx = new();

        public void ProcessMatch(TimeT64 now)
        {
            _matchedInLastProcess = 0;
            _matchingCtx.Init(now);

            foreach (var myNode in _matchSortedTime)
            {
                if (myNode.IsGameMatched)
                {
                    continue;
                }

                if (ProcessMatch(myNode))
                {
                    OnMatchedPlayers(_matchingCtx.Nodes);
                    _matchedInLastProcess++;
                }

                _matchingCtx.ClearInLoop();
            }

            _matchingCtx.Clear();
        }

        bool ProcessMatch(MatchingNode myNode)
        {
            var myPt = myNode.Point;

            // 포인트 정렬 리스트에서 현재 플레이어 기준으로 좌(less), 우(high)를
            // 순차적으로 점수를 비교해서 매칭 여부를 확인한다.
            bool noMoreLesser = false;
            bool noMoreHigher = false;
            var pt_list_size = _matchSortedTime.Count;
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
                    var opNode = _matchSortedPoint[op_idx];
                    var opPt = opNode.Point;
                    if (dist < 0)
                    {
                        // ... op <- my -> ....
                        if (opPt < myPt - myNode.PointBound)
                        {
                            noMoreLesser = true;
                            if (noMoreHigher)
                            {
                                return false;
                            }
                            continue;
                        }
                        else if (opNode.IsGameMatched || (opPt + opNode.PointBound < myPt))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        // ... <- my -> op ....
                        if (myPt + myNode.PointBound < opPt)
                        {
                            noMoreHigher = true;
                            if (noMoreLesser)
                            {
                                return false;
                            }
                            continue;
                        }
                        else if (opNode.IsGameMatched || (myPt < opPt - opNode.PointBound))
                        {
                            continue;
                        }
                    }

                    // 현재 리스트에 포함된 노드들과도 점수 비교를 한다.
                    if (!_matchingCtx.CheckNodePoint(opNode))
                    {
                        continue;
                    }

                    _matchingCtx.Add(opNode);

                    if (_matchingCtx.NodeCount + 1 >= GameConstants.MAX_PLAYER_COUNT_IN_ROOM)
                    {
                        _matchingCtx.Add(myNode);
                        return true;
                    }

                }
            }

            return false;
        }
    }
}
