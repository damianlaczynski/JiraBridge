using JiraBridge.Host.Terminal;

namespace JiraBridge.Host;

public sealed class AppRunner(TerminalLoop terminalLoop)
{
  public Task<int> RunAsync(CancellationToken cancellationToken) =>
    terminalLoop.RunAsync(cancellationToken);
}
