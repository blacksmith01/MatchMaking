using CommonLib;
using Microsoft.Extensions.Logging;
using ServerLib.Services.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/*

8인 free for all 매칭 서비스

- 클라이언트 요청시 즉시 반영하지 않고 큐에 쌓아 두었다가 타이머 호출시 병합 후 매칭 처리. 
- Lock 경합을 줄이기 위해 리스트를 샤딩. PlayerId를 해시한 index로 접근.
- 클라이언트의 취소 요청이 성공했더라도 매칭이 될 수 있기 때문에 취소 완료 또는 매칭 성공 패킷을 기다려야 함.

매칭룰
- 비슷한 수준의 플레이어들과 매칭시킨다. 자신과 매칭될 수 있는 최대,최소 점수 내의 플레이들만 매칭된다.
- 대기 시간이 길어질수록 매칭점수의 최대,최소 간격이 넓어진다.
- 대기 시간이 오래된 플레이어 우선으로 매칭시킨다.

*/

namespace ServerLib.Services.Content
{
    public partial class MatchMakerService : ISingletonService, ITimerObject
    {
        GameTimeService _gametime;
        ILogger<MatchMakerService> _logger;
        ServiceState _state;

        const int SHARDED_LIST_CAPACITY_SIZE = 100;
        const UInt32 SHARDED_GROUP_SIZE = 8; // 2의 승수로 지정
        static UInt32 GetShardingIdx(PlayerId id) => (UInt32)id & (SHARDED_GROUP_SIZE - 1);

        class ShardedNodeGroup
        {
            public object Lock = new();
            public Dictionary<PlayerId, MatchingNode> Players = new(SHARDED_LIST_CAPACITY_SIZE * (int)SHARDED_GROUP_SIZE);
            public Int32 AddDelCount;
            public List<MatchingNode> AddQueue = new(SHARDED_LIST_CAPACITY_SIZE);
            public List<MatchingNode> DelQueue = new(SHARDED_LIST_CAPACITY_SIZE);
            public List<MatchingNode> Pool = new(SHARDED_LIST_CAPACITY_SIZE);

            public List<MatchingNode> MatchedQueue = new(SHARDED_LIST_CAPACITY_SIZE); // ontimer
            public List<MatchingNode> FreeQueue = new(SHARDED_LIST_CAPACITY_SIZE); // ontimer
        }
        ShardedNodeGroup[] _nodeGroups = CollectionEx.CreateArray((int)SHARDED_GROUP_SIZE, () => new ShardedNodeGroup());

        List<MatchingNode> _procAddQueue = new(SHARDED_LIST_CAPACITY_SIZE * (int)SHARDED_GROUP_SIZE);
        List<MatchingNode> _procDelQueue = new(SHARDED_LIST_CAPACITY_SIZE * (int)SHARDED_GROUP_SIZE);

        Int64 _gameIdAlloc;

        public TimerObjIds TimerId => TimerObjIds.MatchMaker;
        public TimeT64 PeriordMs => TimeEx.Duration_Sec_To_Ms;

        public MatchMakerService(GameTimeService gametime, ILogger<MatchMakerService> logger)
        {
            _gametime = gametime;
            _logger = logger;
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

        public Int64 GetStat_GameAllocated() => _gameIdAlloc;

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
            var group = _nodeGroups[groupIdx];
            lock (group.Lock)
            {
                var node = AllocNode(id, point, _gametime.GetTimeT());
                if (!group.Players.TryAdd(id, node))
                {
                    // 매칭 성공 후 Ontimer 호출 전에 재요청하면 이렇게 실패할수도 있지만 일반적으로 불가능하기 때문에 에러로 간주.
                    FreeNode(node);
                    return ErrNo.Matching_Add_AlreadyRequested;
                }
                group.AddQueue.Add(node);
                group.AddDelCount++;
            }

            _logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId:D02}] add player({id:D05}) {point:D06} pt");

            return ErrNo.OK;
        }

        public ErrNo DelPlayer(PlayerId id, out bool directDeleted)
        {
            directDeleted = false;

            if (!GameConstants.IsValidPlayerId(id))
            {
                return ErrNo.Matching_Del_InvalidRequest;
            }

            if (!_state.IsRunning)
            {
                return ErrNo.Matching_Del_NotServiceState;
            }

            var groupIdx = GetShardingIdx(id);
            var group = _nodeGroups[groupIdx];
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
                    _logger.LogWarning($"[{Thread.CurrentThread.ManagedThreadId:D02}] del player({node.Id:D05}) ignored");
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

                    directDeleted = true;
                    return ErrNo.OK;
                }

                node.IsDelReserved = true;
                group.DelQueue.Add(node);
                group.AddDelCount++;
            }

            _logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId:D02}] del player({id:D05})");

            return ErrNo.OK;
        }

        MatchingNode AllocNode(PlayerId id, GamePoint point, TimeT regTime)
        {
            var groupIdx = GetShardingIdx(id);
            var group = _nodeGroups[groupIdx];

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
            var group = _nodeGroups[groupIdx];

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
