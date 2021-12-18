using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MockTest;
using ServerLib;

ServerBuilder.Generate(args)
    .ConfigureServices(services => services.AddSingleton<MockClientService>())
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(options =>
         {
             options.IncludeScopes = true;
             options.SingleLine = true;
             options.TimestampFormat = "hh:mm:ss ";
         });
    })
    .Build()
    .Run();