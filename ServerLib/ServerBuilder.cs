using CommonLib;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ServerLib
{
    public class ServerBuilder
    {
        public static IHostBuilder Generate(string[] args)
        {
            return Generate(new List<Assembly>() { Assembly.GetEntryAssembly()! }, args);
        }

        public static IHostBuilder Generate(IEnumerable<Assembly> asms, string[] args)
        {
            var totalAsms = asms.ToList();
            var libAsm = Assembly.GetExecutingAssembly();
            if(!totalAsms.Contains(libAsm))
            {
                totalAsms.Add(libAsm);
            }
            Globals.Init(args, totalAsms);

            return Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddHostedService<ServerNode>();
                    Globals.GetTypesFromAssemblies<ISingletonService>().ForEach(x => services.AddSingleton(x));
                });
        }
    }
}
