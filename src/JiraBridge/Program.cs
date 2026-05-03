using JiraBridge.Bootstrap;
using JiraBridge.Host;
using Microsoft.Extensions.DependencyInjection;

var host = AppHostBuilder.Build(args);

if (!Console.IsOutputRedirected)
{
  try
  {
    Console.Clear();
  }
  catch (IOException)
  {
  }
}

return await host.Services.GetRequiredService<AppRunner>().RunAsync(CancellationToken.None);
