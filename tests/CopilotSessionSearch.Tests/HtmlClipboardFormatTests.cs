#nullable enable

using System.Globalization;
using System.Text;
using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.Tests;

public sealed class HtmlClipboardFormatTests
{
    [Fact]
    public void CreateUsesUtf8ByteOffsetsForTheHtmlFragment()
    {
        const string fragment = "<p>Résumé café</p>";

        string clipboardData = HtmlClipboardFormat.Create(fragment);
        byte[] bytes = Encoding.UTF8.GetBytes(clipboardData);
        int startHtml = GetOffset(clipboardData, "StartHTML");
        int endHtml = GetOffset(clipboardData, "EndHTML");
        int startFragment = GetOffset(clipboardData, "StartFragment");
        int endFragment = GetOffset(clipboardData, "EndFragment");

        Assert.Equal("<html", Encoding.UTF8.GetString(bytes[startHtml..(startHtml + 5)]));
        Assert.Equal(fragment, Encoding.UTF8.GetString(bytes[startFragment..endFragment]));
        Assert.Equal(bytes.Length, endHtml);
    }

    private static int GetOffset(string clipboardData, string name)
    {
        string prefix = name + ":";
        string line = clipboardData
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return int.Parse(
            line[prefix.Length..],
            NumberStyles.None,
            CultureInfo.InvariantCulture);
    }
}
