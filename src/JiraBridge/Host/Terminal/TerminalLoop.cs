namespace JiraBridge.Host.Terminal;

public sealed class TerminalLoop(
  InteractiveShellController controller,
  KeyboardDispatcher keyboardDispatcher,
  ScreenRenderer screenRenderer)
{
  public async Task<int> RunAsync(CancellationToken cancellationToken)
  {
    controller.Initialize();
    screenRenderer.Render(controller.Title, controller.GetLines(), controller.GetCursorPlacement());

    if (Console.IsInputRedirected)
    {
      Console.WriteLine();
      Console.WriteLine("Interactive input is redirected. Exiting after initial render.");
      return 0;
    }

    while (!cancellationToken.IsCancellationRequested)
    {
      ConsoleKeyInfo key = keyboardDispatcher.ReadKey();
      bool shouldContinue = await controller.HandleKeyAsync(key, cancellationToken);
      if (!shouldContinue)
      {
        return 0;
      }

      screenRenderer.Render(controller.Title, controller.GetLines(), controller.GetCursorPlacement());
    }

    return 0;
  }
}
