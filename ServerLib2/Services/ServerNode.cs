using CommonLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerLib.Services.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerLib
{
    public sealed class ServerNode : IHostedService, IDisposable
    {
        IServiceProvider _sp;
        ILogger<ServerNode> _logger;
        IHostApplicationLifetime _lifetime;
        List<ISingletonService> _services;

        T GetService<T>() => _sp.GetService<T>()!;
        IEnumerable<T> GetSingletonServices<T>() where T : class => _services.Where(x => x is T).Select(x => (x as T))!;

        public ServerNode(IServiceProvider sp, ILogger<ServerNode> logger, IHostApplicationLifetime appLifetime)
        {
            _sp = sp;
            _logger = logger;
            _lifetime = appLifetime;
            _services = Globals.GetTypesFromAssemblies<ISingletonService>().Select(x => _sp.GetService(x) as ISingletonService).ToList()!;
        }

        public void Dispose()
        {
            foreach (var service in GetSingletonServices<IDisposable>())
            {
                service!.Dispose();
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("StartAsync.");

            _lifetime.ApplicationStarted.Register(OnStarted);
            _lifetime.ApplicationStopping.Register(OnStopping);
            _lifetime.ApplicationStopped.Register(OnStopped);

            foreach (var service in _services)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                await service.OnServerStartAsync(cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                _lifetime.StopApplication();
                return;
            }

            if(!await GetService<ServerTimerQueueService>()!.BeginTimerThreadAsync(GetSingletonServices<ITimerObject>()))
            {
                _logger.LogError($"!ServerTimerManager.BeginTimerThread.");
                _lifetime.StopApplication();
            }
        }

        void OnStarted()
        {
            _logger.LogInformation("OnStarted.");
            _services.ForEach(x=>x.OnServerStarted());
        }

        void OnStopping()
        {
            _logger.LogInformation("OnStopping.");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("StopAsync.");

            List<Task> tasks = new();
            _services.ForEach(x => tasks.Add(x.OnServerStopAsync()));

            await Task.WhenAll(tasks);
        }

        void OnStopped()
        {
            _logger.LogInformation("OnStopped.");
            _services.ForEach(x => x.OnServerStopped());
        }
    }
}
