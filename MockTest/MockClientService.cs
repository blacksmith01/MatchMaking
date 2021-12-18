using ServerLib;
using ServerLib.Services.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MockTest
{
    internal class MockClientService : ISingletonService
    {
        const int THREAD_COUNT = 8;

        int _threadInit;
        int _threadShutdown;
        bool _startGenerate;
        bool _isStopped;

        MatchMakerService _matchMaker;

        public MockClientService(MatchMakerService matchMaker)
        {
            _matchMaker = matchMaker;
        }

        public async Task OnServerStartAsync(CancellationToken ct)
        {
            for (int i = 0; i < THREAD_COUNT; i++)
            {
                new Thread(() => { OnThread(i); }).Start();
            }

            while (_threadInit < THREAD_COUNT)
            {
                await Task.Delay(100);
            };
        }

        public void OnServerStarted()
        {
            _startGenerate = true;
        }

        public async Task OnServerStopAsync()
        {
            _isStopped = true;

            while (_threadShutdown < THREAD_COUNT)
            {
                await Task.Delay(100);
            };
        }

        public void OnServerStopped()
        {

        }

        void OnThread(int idx)
        {
            Interlocked.Increment(ref _threadInit);
            Random rand = new Random(idx);

            UInt32 i = 0;
            UInt32 lastSuccId = 0;
            while (!_isStopped)
            {
                Thread.Sleep(rand.Next(1000, 1200));

                if (_startGenerate)
                {
                    if (lastSuccId != 0 && rand.Next(0, 4) < 1)
                    {
                        if (_matchMaker.DelPlayer((int)lastSuccId, out var direct_deleted) == ErrNo.OK)
                        {
                            lastSuccId = 0;
                        }
                    }
                    else
                    {
                        var id = (UInt32)(i * THREAD_COUNT + idx);
                        var point = rand.Next(0, GameConstants.GAMEPOINT_GOLD_START * 2);
                        if (_matchMaker.AddPlayer((int)id, point) == ErrNo.OK)
                        {
                            lastSuccId = id;
                        }
                    }

                    i = (i + 1) % Int32.MaxValue;
                }
            }

            Interlocked.Increment(ref _threadShutdown);
        }
    }
}
