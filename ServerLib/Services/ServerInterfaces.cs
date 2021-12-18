using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServerLib
{
    public interface ISingletonService
    {
        Task OnServerStartAsync(CancellationToken ct);
        void OnServerStarted();
        Task OnServerStopAsync();
        void OnServerStopped();
    }

    public struct ServiceState
    {
        bool Started = false;
        bool Stopped = false;

        public void SetStarted() => Started = true;
        public void SetStopped() => Stopped = true;
        public bool IsRunning => Started && !Stopped;
    }
}
