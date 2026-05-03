namespace JiraBridge.Host.Terminal;

public sealed class KeyboardDispatcher
{
  public ConsoleKeyInfo ReadKey() => Console.ReadKey(intercept: true);
}
