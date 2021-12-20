global using TimerObjId = System.Int32;

using CommonLib;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


/*
 전역 타이머 서비스

- 전용 스레드 사용.
- 밀리 세컨드 시간 정확도
- monitor lock, eventhandle 사용
- 서비스 running 중에는 등록 불가능. 
- 등록된 이후 삭제 불가능.
- Expire 이후 다시 스케줄링되기 전까지 소요된 시간은 무시함.

 */

namespace ServerLib.Services.Core
{
    public enum TimerObjIds
    {
        Dummy = 0,
        MatchMaker
    }

    public interface ITimerObject
    {
        TimerObjIds TimerId { get; }
        TimeT64 PeriordMs { get; }
        void OnTimer();
    }

    public class ServerTimerQueueService : ISingletonService, IDisposable
    {
        class DummyObject : ITimerObject
        {
            public TimerObjIds TimerId => TimerObjIds.Dummy;
            public TimeT64 PeriordMs => TimeEx.Duration_Year_To_Ms;
            public void OnTimer() { }
        }

        class TimerNode
        {
            public ITimerObject Obj { get; init; } = default!;
            public TimeT64 ExpireTime { get; set; }

            public class Comparer : IComparer<TimerNode>
            {
                public int Compare(TimerNode x, TimerNode y)
                {
                    if (x.ExpireTime < y.ExpireTime)
                    {
                        return -1;
                    }
                    else if (x.ExpireTime > y.ExpireTime)
                    {
                        return 1;
                    }
                    else
                    {
                        return (int)x.Obj.TimerId - (int)y.Obj.TimerId;
                    }
                }
            }
        }

        ILogger<ServerTimerQueueService> _logger;
        Action<ITimerObject> _onCompleteCallback;

        EventWaitHandle[] _handles = new EventWaitHandle[] { new AutoResetEvent(false), new AutoResetEvent(false) };
        EventWaitHandle ShutdownHandle => _handles[0];
        EventWaitHandle TimerHandle => _handles[1];

        object _lock = new();
        Dictionary<TimerObjIds, TimerNode> _nodes = new();
        SortedSet<TimerNode> _sortedNodes = new(new TimerNode.Comparer());

        volatile bool _threadStarted = false;
        volatile bool _threadShutdowned = false;
        volatile bool _shutdownRequested = false;

        public ServerTimerQueueService(ILogger<ServerTimerQueueService> logger)
        {
            _logger = logger;
            _onCompleteCallback = OnComplete;

            // 더미 타이머 오브젝트를 넣어두어 리스트가 empty가 되지 않도록 보장한다.
            var dummy = new DummyObject();
            var dummyNode = new TimerNode { Obj = dummy, ExpireTime = 0 };
            _nodes.Add(dummy.TimerId, dummyNode);
            _sortedNodes.Add(dummyNode);
        }

        public Task OnServerStartAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }
        public void OnServerStarted()
        {

        }
        public async Task OnServerStopAsync()
        {
            _shutdownRequested = true;
            ShutdownHandle.Set();

            while (!_threadShutdowned)
            {
                await Task.Delay(10);
            };
        }
        public void OnServerStopped()
        {

        }

        public void Dispose()
        {
            foreach (var h in _handles)
                h?.Dispose();
        }

        public async Task<bool> BeginTimerThreadAsync(IEnumerable<ITimerObject> objs)
        {
            lock (_lock)
            {
                foreach (var obj in objs)
                {
                    if (!_nodes.TryAdd(obj.TimerId, new TimerNode { Obj = obj, ExpireTime = 0 }))
                    {
                        _logger.LogError($"!BeginTimerThread. {obj.TimerId}");
                        return false;
                    }
                }

                _sortedNodes.Clear();
                var now = TimeEx.GetTimeT64();
                foreach (var (k, v) in _nodes)
                {
                    v.ExpireTime = now + v.Obj.PeriordMs;
                    _sortedNodes.Add(v);
                }
            }

            TimerHandle.Set();

            new Thread(Process).Start();
            while (!_threadStarted)
            {
                await Task.Delay(10);
            };

            return true;
        }

        void Process()
        {
            TimeT64 now = 0;
            TimeT64 toptime = 0;

            _threadStarted = true;

            while (!_shutdownRequested)
            {
                WaitHandle.WaitAny(_handles, (int)(toptime - now));

                while (!_shutdownRequested)
                {
                    ITimerObject dueObj = null!;
                    lock (_lock)
                    {
                        var topNode = _sortedNodes.First();
                        toptime = topNode.ExpireTime;
                        now = TimeEx.GetTimeT64();
                        if (toptime > now)
                        {
                            break;
                        }

                        _sortedNodes.Remove(topNode);
                        topNode.ExpireTime = 0;
                        dueObj = topNode.Obj;
                    }

                    Task.Run(() =>
                    {
                        dueObj.OnTimer();
                        OnComplete(dueObj);
                    });
                }
            }

            while (true)
            {
                // Execute된 모든 노드가 OnComplete 완료될 때까지 대기한다.
                lock (_lock)
                {
                    if (_nodes.Count == _sortedNodes.Count)
                    {
                        break;
                    }
                }
                Thread.Sleep(100);
            }

            _threadShutdowned = true;
        }

        void OnComplete(ITimerObject obj)
        {
            bool isTop = false;
            lock (_lock)
            {
                if (!_nodes.TryGetValue(obj.TimerId, out var node))
                {
                    _logger.LogError($"Invalid Object. {obj.TimerId}");
                    return;
                }
                if (node.ExpireTime != 0)
                {
                    _logger.LogError($"Already Queued. {obj.TimerId}");
                    return;
                }

                var now = TimeEx.GetTimeT64();
                node.ExpireTime = now + obj.PeriordMs;

                if (node.ExpireTime < _sortedNodes.First().ExpireTime)
                {
                    isTop = true;
                }
                _sortedNodes.Add(node);
            }

            if (isTop)
            {
                TimerHandle.Set();
            }
        }
    }
}
