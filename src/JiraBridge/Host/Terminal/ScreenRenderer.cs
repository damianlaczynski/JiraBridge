using JiraBridge.Navigation.Menu;

namespace JiraBridge.Host.Terminal;

public sealed class ScreenRenderer
{
  private int previouslyRenderedLineCount;

  public void Render(string title, IReadOnlyList<string> lines, CursorPlacement? cursorPlacement = null)
  {
    if (Console.IsOutputRedirected)
    {
      TryClearConsole();
      WriteColoredLine(title, ConsoleColor.Cyan);
      WriteColoredLine(new string('=', title.Length), ConsoleColor.DarkGray);

      foreach (string line in lines)
      {
        WriteStyledLine(line);
      }

      ApplyCursorPlacement(lines, cursorPlacement);
      return;
    }

    RenderFrame(title, lines);

    ApplyCursorPlacement(lines, cursorPlacement);
  }

  public void Render(IMenuScreen screen)
  {
    Render(screen.Title, screen.GetLines());
  }

  private static void TryClearConsole()
  {
    try
    {
      if (!Console.IsOutputRedirected)
      {
        Console.Clear();
      }
    }
    catch (IOException)
    {
      // Some hosts expose stdout without a real console buffer.
    }
    catch (InvalidOperationException)
    {
      // Ignore non-interactive hosts that cannot clear the screen.
    }
  }

  private static void ApplyCursorPlacement(IReadOnlyList<string> lines, CursorPlacement? cursorPlacement)
  {
    try
    {
      if (Console.IsOutputRedirected)
      {
        return;
      }

      if (cursorPlacement is null || !cursorPlacement.IsVisible)
      {
        Console.CursorVisible = false;
        return;
      }

      int targetLeft = Math.Clamp(cursorPlacement.Left, 0, Console.BufferWidth - 1);
      int maxTop = Math.Max(0, Console.BufferHeight - 1);
      int targetTop = Math.Clamp(cursorPlacement.Top, 0, maxTop);

      Console.CursorVisible = true;
      Console.SetCursorPosition(targetLeft, targetTop);
    }
    catch (IOException)
    {
      // Ignore console cursor issues in restricted hosts.
    }
    catch (InvalidOperationException)
    {
      // Ignore hosts that do not expose cursor positioning.
    }
    catch (ArgumentOutOfRangeException)
    {
      // Ignore positions that do not fit the current host buffer.
    }
  }

  private void RenderFrame(string title, IReadOnlyList<string> lines)
  {
    try
    {
      int width = GetSafeWidth();
      int bufferHeight = GetSafeBufferHeight();
      var frameLines = new List<string>(lines.Count + 2)
      {
        title,
        new string('=', title.Length)
      };
      frameLines.AddRange(lines);

      int totalLines = frameLines.Count;
      bool truncated = totalLines > bufferHeight;
      int rowsUsed;

      if (truncated)
      {
        for (int row = 0; row < bufferHeight - 1; row++)
        {
          Console.SetCursorPosition(0, row);
          WriteStyledText(frameLines[row], width);
        }

        int omitted = totalLines - (bufferHeight - 1);
        Console.SetCursorPosition(0, bufferHeight - 1);
        WriteStyledText($"[INFO] ... ({omitted} lines not shown — terminal buffer is {bufferHeight} rows; widen buffer or shorten conflict diff)", width);
        rowsUsed = bufferHeight;
      }
      else
      {
        for (int row = 0; row < totalLines; row++)
        {
          Console.SetCursorPosition(0, row);
          WriteStyledText(frameLines[row], width);
        }

        rowsUsed = totalLines;
      }

      for (int row = rowsUsed; row < previouslyRenderedLineCount && row < bufferHeight; row++)
      {
        Console.SetCursorPosition(0, row);
        Console.Write(new string(' ', width));
      }

      previouslyRenderedLineCount = rowsUsed;
    }
    catch (IOException)
    {
      TryClearConsole();
    }
    catch (InvalidOperationException)
    {
      TryClearConsole();
    }
    catch (ArgumentOutOfRangeException)
    {
      TryClearConsole();
    }
  }

  private static void WriteStyledLine(string line)
  {
    ConsoleColor? color = GetLineColor(line);
    if (color is not null)
    {
      WriteColoredLine(line, color.Value);
      return;
    }

    Console.WriteLine(line);
  }

  private static void WriteStyledText(string line, int width)
  {
    string safeLine = FitToConsoleWidth(line, width);
    ConsoleColor? color = GetLineColor(line);
    ConsoleColor previous = Console.ForegroundColor;

    try
    {
      if (color is not null)
      {
        Console.ForegroundColor = color.Value;
      }

      Console.Write(safeLine);
    }
    finally
    {
      Console.ForegroundColor = previous;
    }
  }

  private static ConsoleColor? GetLineColor(string line)
  {
    if (line.StartsWith("+ ", StringComparison.Ordinal))
    {
      return ConsoleColor.Green;
    }

    if (line.StartsWith("- ", StringComparison.Ordinal))
    {
      return ConsoleColor.Red;
    }

    if (line.StartsWith("> ", StringComparison.Ordinal))
    {
      return ConsoleColor.Yellow;
    }

    if (line.StartsWith("[OK]", StringComparison.Ordinal))
    {
      return ConsoleColor.Green;
    }

    if (line.StartsWith("[WARN]", StringComparison.Ordinal) || line.StartsWith("[ERR]", StringComparison.Ordinal))
    {
      return ConsoleColor.Red;
    }

    if (line.StartsWith("[TIP]", StringComparison.Ordinal))
    {
      return ConsoleColor.DarkCyan;
    }

    if (line.StartsWith("[STEP]", StringComparison.Ordinal) || line.StartsWith("[INFO]", StringComparison.Ordinal))
    {
      return ConsoleColor.Gray;
    }

    if (line.StartsWith("[", StringComparison.Ordinal) && line.Length >= 3)
    {
      return ConsoleColor.Yellow;
    }

    return null;
  }

  private static void WriteColoredLine(string line, ConsoleColor color)
  {
    try
    {
      if (Console.IsOutputRedirected)
      {
        Console.WriteLine(line);
        return;
      }

      ConsoleColor previous = Console.ForegroundColor;
      Console.ForegroundColor = color;
      Console.WriteLine(line);
      Console.ForegroundColor = previous;
    }
    catch (IOException)
    {
      Console.WriteLine(line);
    }
    catch (InvalidOperationException)
    {
      Console.WriteLine(line);
    }
  }

  private static int GetSafeWidth()
  {
    try
    {
      return Math.Max(1, Console.BufferWidth);
    }
    catch
    {
      return 120;
    }
  }

  private static int GetSafeBufferHeight()
  {
    try
    {
      return Math.Max(1, Console.BufferHeight);
    }
    catch
    {
      return 25;
    }
  }

  private static string FitToConsoleWidth(string line, int width)
  {
    if (width <= 1)
    {
      return string.Empty;
    }

    int usableWidth = width - 1;
    string visible = line.Length > usableWidth
      ? line[..usableWidth]
      : line;

    return visible.PadRight(usableWidth);
  }
}

public sealed record CursorPlacement(bool IsVisible, int Left, int Top);
