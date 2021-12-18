using CommonLib;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServerLib.Services.Content
{
    public class GameTimeService : ISingletonService
    {
        TimeT64 _timeMod;

        public Task OnServerStartAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void OnServerStarted()
        {
            
        }

        public Task OnServerStopAsync()
        {
            return Task.CompletedTask;
        }

        public void OnServerStopped()
        {
            
        }

        public TimeT GetTimeT()
        {
            return TimeEx.GetTimeT() + (TimeT)(_timeMod / 1000);
        }
        public TimeT64 GetTimeT64()
        {
            return TimeEx.GetTimeT64() + _timeMod;
        }

        public void UpdateTimeMode(TimeT64 timeMod)
        {
            Interlocked.Exchange(ref _timeMod, timeMod);
        }
    }
}
