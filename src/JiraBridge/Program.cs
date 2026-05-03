using JiraBridge.Bootstrap;
using JiraBridge.Host;
using Microsoft.Extensions.DependencyInjection;

var host = AppHostBuilder.Build(args);

return await host.Services.GetRequiredService<AppRunner>().RunAsync(CancellationToken.None);
