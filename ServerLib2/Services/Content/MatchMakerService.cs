using CommonLib;
using Microsoft.Extensions.Logging;
using ServerLib.Services.Core;
using System;
using System.Collections.Generic;
using System.Text;

/*

8인 free for all 매칭룰

1. 비슷한 수준의 플레이어들과 매칭시킨다. 자신과 매칭될 수 있는 최대,최소 점수 내의 플레이들만 매칭된다.

2. 대기 시간이 길어질수록 최대,최소 간격이 넓어진다.

3. 가능하면 대기 시간이 오래된 플레이어 우선으로 매칭시킨다.


핵심 사항.

1. 클라이언트 요청시 즉시 반영하지 않고 큐에 쌓아 두었다가 Scheduler에서 호출될 때마다 병합 후 매칭 처리. 

2. Lock 경합을 줄이기 위해 리스트를 샤딩. PlayerId를 해시한 index로 접근.

3. 클라이언트의 취소 요청이 성공했더라도 매칭이 될 수 있기 때문에 취소 완료 또는 매칭 성공 패킷을 기다려야 함.

*/

namespace ServerLib.Services.Content
{
    public partial class MatchMakerService : ISingletonService, ITimerObject
    {
        ILogger<MatchMakerService> _logger;
        ServiceState _state;

        const int DEFAULT_CAPACITY_SIZE = 100;
        const UInt32 SHARDED_LIST_SIZE = 8; // 2의 승수로 지정
        static UInt32 GetShardingIdx(PlayerId id) => (UInt32)id & (SHARDED_LIST_SIZE - 1);

        class ShardedNodeGroup
        {
            public object Lock = new();
            public Dictionary<PlayerId, MatchingNode> Players = new(DEFAULT_CAPACITY_SIZE * (int)SHARDED_LIST_SIZE);
            public Int32 AddDelCount;
            public List<MatchingNode> AddQueue = new(DEFAULT_CAPACITY_SIZE);
            public List<MatchingNode> DelQueue = new(DEFAULT_CAPACITY_SIZE);
            public List<MatchingNode> MatchedQueue = new(DEFAULT_CAPACITY_SIZE);
            public List<MatchingNode> FreeQueue = new(DEFAULT_CAPACITY_SIZE);
            public List<MatchingNode> Pool = new(DEFAULT_CAPACITY_SIZE);
        }
        ShardedNodeGroup[] _groups = CollectionEx.CreateArray((int)SHARDED_LIST_SIZE, () => new ShardedNodeGroup());

        List<MatchingNode> _procAddQueue = new(DEFAULT_CAPACITY_SIZE * (int)SHARDED_LIST_SIZE);
        List<MatchingNode> _procDelQueue = new(DEFAULT_CAPACITY_SIZE * (int)SHARDED_LIST_SIZE);
        MatchingImpl _matching = new MatchingImpl();

        Action<IEnumerable<MatchingNode>> _delegateOnMatched;
        Int64 _gameIdAlloc;

        public TimerObjIds TimerId => TimerObjIds.MatchMaker;
        public TimeT64 PeriordMs => TimeEx.Duration_Sec_To_Ms;

        public MatchMakerService(ILogger<MatchMakerService> logger)
        {
            _logger = logger;
            _delegateOnMatched = OnMatchedPlayers;
        }

        public Task OnServerStartAsync(CancellationToken ct)
        {
            _state.SetStarted();
            return Task.CompletedTask;
        }
        public void OnServerStarted()
        {

        }
        public Task OnServerStopAsync()
        {
            _state.SetStopped();
            return Task.CompletedTask;
        }
        public void OnServerStopped()
        {

        }

        public ErrNo AddPlayer(PlayerId id, GamePoint point)
        {
            if (!GameConstants.IsValidPlayerId(id))
            {
                return ErrNo.Matching_Add_InvalidRequest;
            }

            if(!_state.IsRunning)
            {
                return ErrNo.Matching_Add_NotServiceState;
            }

            var groupIdx = GetShardingIdx(id);
            var group = _groups[groupIdx];
            lock (group.Lock)
            {
                var node = AllocNode(id, point, TimeEx.GetTimeT());
                if (!group.Players.TryAdd(id, node))
                {
                    FreeNode(node);
                    return ErrNo.Matching_Add_AlreadyRequested;
                }
                group.AddQueue.Add(node);
                group.AddDelCount++;
            }

            _logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId:D02}] add player, {id:D05} {point:D06}");

            return ErrNo.OK;
        }

        public ErrNo DelPlayer(PlayerId id, out bool direct_deleted)
        {
            direct_deleted = false;

            if (!GameConstants.IsValidPlayerId(id))
            {
                return ErrNo.Matching_Del_InvalidRequest;
            }

            if (!_state.IsRunning)
            {
                return ErrNo.Matching_Del_NotServiceState;
            }

            var groupIdx = GetShardingIdx(id);
            var group = _groups[groupIdx];
            lock (group.Lock)
            {
                if (!group.Players.TryGetValue(id, out var node))
                {
                    return ErrNo.Matching_Del_NotRequested;
                }
                if (node.IsDelReserved)
                {
                    return ErrNo.Matching_Del_Duplicated;
                }
                if (node.IsGameMatched)
                {
                    _logger.LogWarning($"[{Thread.CurrentThread.ManagedThreadId:D02}] del ignored, {node.Id:D05}");
                    return ErrNo.Matching_Del_AlreadyMatched;
                }

                if (!node.IsInMatching)
                {
                    // inmatching상태가 아니면 add_queue 존재해야만 한다.
                    if (!group.AddQueue.Contains(node))
                    {
                        return ErrNo.Matching_Del_InvalidState;
                    }

                    group.AddQueue.Remove(node);
                    group.Players.Remove(id);
                    FreeNode(node);

                    direct_deleted = true;
                    return ErrNo.OK;
                }

                node.IsDelReserved = true;
                group.DelQueue.Add(node);
                group.AddDelCount++;
            }

            _logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId:D02}] del player, {id:D05}");

            return ErrNo.OK;
        }

        public void OnTimer(TimeT64 now)
        {
            _logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId:D02}] OnTimer");

            for (UInt32 i = 0; i < SHARDED_LIST_SIZE; i++)
            {
                UpdateGroup(i);
            }

            _matching.Build(_procAddQueue, _procDelQueue);

            foreach (var node in _procDelQueue)
            {
                if (node.IsDelReserved)
                {
                    if (node.IsGameMatched)
                    {
                        _logger.LogWarning($"[{Thread.CurrentThread.ManagedThreadId:D02}] del ignored, {node.Id:D05}");
                    }
                    else
                    {
                        _logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId:D02}] del complete, {node.Id:D05}");
                        Send_CancelCompleted(node);
                    }
                }
                var groupIdx = GetShardingIdx(node.Id);
                var group = _groups[groupIdx];
                group.FreeQueue.Add(node);
                // 오브젝트 해제는 다음 업데이트시 처리
            }
            _procAddQueue.Clear();
            _procDelQueue.Clear();

            _matching.ProcessMatch(now, _delegateOnMatched);

            for (UInt32 i = 0; i < SHARDED_LIST_SIZE; i++)
            {
                var group = _groups[i];
                foreach (var node in group.MatchedQueue)
                {
                    Send_Matched(node);
                }
            }
        }

        void UpdateGroup(UInt32 groupIdx)
        {
            var group = _groups[groupIdx];
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

                    foreach (var node in group.FreeQueue)
                    {
                        FreeNode(node);
                    }
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
                    }
                    group.MatchedQueue.Clear();

                    foreach (var node in group.FreeQueue)
                    {
                        FreeNode(node);
                    }
                    group.FreeQueue.Clear();
                }
            }
            else if (group.FreeQueue.Any())
            {
                lock (group.Lock)
                {
                    foreach (var node in group.FreeQueue)
                    {
                        FreeNode(node);
                    }
                    group.FreeQueue.Clear();
                }
            }
        }

        void OnMatchedPlayers(IEnumerable<MatchingNode> nodes)
        {
            var newGameId = ++_gameIdAlloc;

            _logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId:D02}] game {newGameId}, [{nodes.Sum(x => x?.Point) / nodes.Count():D06}]");

            foreach (var node in nodes)
            {
                node.IsGameMatched = true;
                var groupIdx = GetShardingIdx(node.Id);
                var group = _groups[groupIdx];
                group.MatchedQueue.Add(node);
                Send_Matched(node);
                _logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId:D02}] game {newGameId}, [{node.Point:D06}] player({node.Id:D05})");
            }
        }

        MatchingNode AllocNode(PlayerId id, GamePoint point, TimeT regTime)
        {
            var groupIdx = GetShardingIdx(id);
            var group = _groups[groupIdx];

            var node = group.Pool.PopBack();
            if (node == null)
            {
                node = new MatchingNode();
            }

            node.Init(id, point, regTime);

            return node;
        }

        void FreeNode(MatchingNode node)
        {
            var groupIdx = GetShardingIdx(node.Id);
            var group = _groups[groupIdx];

            node.Id = 0;
            group.Pool.Add(node);
        }

        void Send_CancelCompleted(MatchingNode node)
        {

        }

        void Send_Matched(MatchingNode node)
        {

        }
    }
}
