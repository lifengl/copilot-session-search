#nullable enable

using System.Windows;

namespace CopilotSessionSearch.Services;

public sealed class ClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Clipboard.SetText(text);
    }
}
