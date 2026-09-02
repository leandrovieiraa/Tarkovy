using System.Text;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Tarkovy.Services;

public sealed class LoadingTerminal
{
    private readonly TextBlock _output;
    private readonly Dispatcher _dispatcher;
    private readonly StringBuilder _history = new();

    public LoadingTerminal(TextBlock output)
    {
        _output = output;
        _dispatcher = output.Dispatcher;
    }

    public void Seed()
    {
        _history.Clear();
        _history.AppendLine("> BOOT  tarkovy companion");
        Render("", showCursor: true);
    }

    public async Task TypeLineAsync(string locKey, int charDelayMs = 14, CancellationToken ct = default)
    {
        var line = Loc.T(locKey);
        var typed = new StringBuilder();
        foreach (var ch in line)
        {
            ct.ThrowIfCancellationRequested();
            typed.Append(ch);
            Render(typed.ToString(), showCursor: true);
            await Task.Delay(charDelayMs, ct).ConfigureAwait(true);
        }

        _history.AppendLine(line);
        Render("", showCursor: true);
    }

    public void AppendLine(string line)
    {
        _history.AppendLine(line);
        Render("", showCursor: true);
    }

    public void Complete() => Render("", showCursor: false);

    private void Render(string currentLine, bool showCursor)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => Render(currentLine, showCursor));
            return;
        }

        var text = _history.ToString();
        if (!string.IsNullOrEmpty(currentLine))
            text += currentLine;
        if (showCursor)
            text += "▌";
        _output.Text = text;
    }
}
