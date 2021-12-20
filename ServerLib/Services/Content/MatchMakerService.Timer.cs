using CommonLib;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Linq;

namespace ServerLib.Services.Content
{
    public partial class MatchMakerService
    {
        public void OnTimer()
        {
            var now = _gametime.GetTimeT64();

            for (UInt32 i = 0; i < SHARDED_GROUP_SIZE; i++)
            {
                UpdateGroup(i);
            }

            BuildSortedList(now);

            foreach (var node in _procDelQueue)
            {
                if (node.IsDelReserved)
                {
                    if (node.IsGameMatched)
                    {
                        _logger.LogWarning($"[{Thread.CurrentThread.ManagedThreadId:D02}] del player({node.Id:D05}) ignored");
                    }
                    else
                    {
                        _logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId:D02}] del player({node.Id:D05}) complete");
                        Send_CancelCompleted(node);
                    }
                }
                var groupIdx = GetShardingIdx(node.Id);
                var group = _nodeGroups[groupIdx];
                group.FreeQueue.Add(node);
                // 오브젝트 해제는 다음 업데이트시 처리
            }
            _procAddQueue.Clear();
            _procDelQueue.Clear();

            ProcessMatch(now);

            for (UInt32 i = 0; i < SHARDED_GROUP_SIZE; i++)
            {
                var group = _nodeGroups[i];
                foreach (var node in group.MatchedQueue)
                {
                    Send_Matched(node);
                }
            }

            var elapsed = _gametime.GetTimeT64() - now;

            _logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId:D02}] OnTimer {elapsed} ms, {_matchSortedPoint.Count - _matchedInLastProcess} players");
        }

        void UpdateGroup(UInt32 groupIdx)
        {
            var group = _nodeGroups[groupIdx];
            if (group.AddDelCount > 0)
            {
                lock (group.Lock)
                {
                    group.AddDelCount = 0;
                    foreach (var node in group.AddQueue)
                    {
                        node.IsInMatching = true;
                        _procAddQueue.Add(node);
                    }
                    group.AddQueue.Clear();

                    foreach (var node in group.MatchedQueue)
                    {
                        if (!node.IsDelReserved)
                        {
                            group.DelQueue.Add(node);
                        }
                    }
                    group.MatchedQueue.Clear();
                    foreach (var node in group.DelQueue)
                    {
                        group.Players.Remove(node.Id);
                        _procDelQueue.Add(node);
                    }
                    group.DelQueue.Clear();

                    group.FreeQueue.ForEach(node => FreeNode(node));
                    group.FreeQueue.Clear();
                }
            }
            else if (group.MatchedQueue.Any())
            {
                lock (group.Lock)
                {
                    foreach (var node in group.MatchedQueue)
                    {
                        group.Players.Remove(node.Id);
                        _procDelQueue.Add(node);
                    }
                    group.MatchedQueue.Clear();

                    group.FreeQueue.ForEach(node => FreeNode(node));
                    group.FreeQueue.Clear();
                }
            }
            else if (group.FreeQueue.Any())
            {
                lock (group.Lock)
                {
                    group.FreeQueue.ForEach(node => FreeNode(node));
                    group.FreeQueue.Clear();
                }
            }
        }

        public void BuildSortedList(TimeT64 now)
        {
            var nowSec = (TimeT)(now / TimeEx.Duration_Sec_To_Ms);

            // 추가/삭제가 없다면 리스트를 재구축할 필요가 없다.
            if (_matchedInLastProcess == 0 && _procAddQueue.Count == 0 && _procDelQueue.Count == 0)
            {
                foreach (var node in _matchSortedPoint)
                {
                    node.PointBound = GameConstants.GetPointBound_ByRegistTime(nowSec - node.RegTime);
                }
                return;
            }

            _matchTempList.Clear();
            _matchTempList.EnsureCapacity(_matchSortedPoint.Count + _procAddQueue.Count - _procDelQueue.Count);

            // 점수순으로 정렬시킨다.
            _procAddQueue.Sort(MatchingNode.DefaultComparerByPoint);
            var addIdx = 0;
            var addRemains = _procAddQueue.Count;
            var cmpPoint = addIdx < _procAddQueue.Count ? _procAddQueue[addIdx].Point : GamePoint.MaxValue;

            // 기존에 정렬되어 있던 리스트를 순회하면서 추가/삭제를 하고 Swap한다.
            foreach (var node in _matchSortedPoint)
            {
                if (node.IsDelReserved || node.IsGameMatched)
                {
                    continue;
                }
                while (cmpPoint < node.Point)
                {
                    AddToTempListPoint(_procAddQueue[addIdx]);
                    addIdx++;
                    cmpPoint = addIdx < _procAddQueue.Count ? _procAddQueue[addIdx].Point : GamePoint.MaxValue;
                }
                AddToTempListPoint(node);
            }
            for (; addIdx < _procAddQueue.Count; addIdx++)
            {
                AddToTempListPoint(_procAddQueue[addIdx]);
            }
            ClassUtils.Swap(ref _matchTempList, ref _matchSortedPoint);


            // OnTimer 간격이 짧기 때문에 시간순으로 정렬시키지 않고 추가/삭제 후 Swap한다.
            _matchTempList.Clear();
            addIdx = 0;
            foreach (var node in _matchSortedTime)
            {
                if (node.IsDelReserved || node.IsGameMatched)
                {
                    continue;
                }
                AddToTempListRegTime(node, nowSec);
            }
            for (; addIdx < _procAddQueue.Count; addIdx++)
            {
                AddToTempListRegTime(_procAddQueue[addIdx], nowSec);
            }
            ClassUtils.Swap(ref _matchTempList, ref _matchSortedTime);
        }

        void AddToTempListPoint(MatchingNode node)
        {
            node.IdxPoint = _matchTempList.Count;
            _matchTempList.Add(node);
        }
        void AddToTempListRegTime(MatchingNode node, TimeT now)
        {
            node.IdxRegtime = _matchTempList.Count;
            node.PointBound = GameConstants.GetPointBound_ByRegistTime(now - node.RegTime);
            _matchTempList.Add(node);
        }

        void OnMatchedPlayers(IEnumerable<MatchingNode> nodes)
        {
            var newGameId = ++_gameIdAlloc;

            _logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId:D02}] game {newGameId} {nodes.Sum(x => x?.Point) / nodes.Count():D06} pt");

            foreach (var node in nodes)
            {
                node.IsGameMatched = true;
                var groupIdx = GetShardingIdx(node.Id);
                var group = _nodeGroups[groupIdx];
                group.MatchedQueue.Add(node);
                Send_Matched(node);
                _logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId:D02}] game {newGameId} {node.Point:D06} pt +-{node.PointBound:D04} player({node.Id:D05})");
            }
        }
    }
}
