#nullable enable

using System.Globalization;
using System.Text;

namespace CopilotSessionSearch.Services;

public static class HtmlClipboardFormat
{
    private const string HeaderTemplate =
        "Version:1.0\r\n" +
        "StartHTML:{0:D10}\r\n" +
        "EndHTML:{1:D10}\r\n" +
        "StartFragment:{2:D10}\r\n" +
        "EndFragment:{3:D10}\r\n";
    private const string HtmlPrefix =
        "<html><head><meta charset=\"utf-8\"></head><body><!--StartFragment-->";
    private const string HtmlSuffix =
        "<!--EndFragment--></body></html>";

    public static string Create(string htmlFragment)
    {
        ArgumentNullException.ThrowIfNull(htmlFragment);

        string placeholderHeader = CreateHeader(0, 0, 0, 0);
        int startHtml = Encoding.UTF8.GetByteCount(placeholderHeader);
        int startFragment =
            startHtml + Encoding.UTF8.GetByteCount(HtmlPrefix);
        int endFragment =
            startFragment + Encoding.UTF8.GetByteCount(htmlFragment);
        int endHtml =
            endFragment + Encoding.UTF8.GetByteCount(HtmlSuffix);

        return CreateHeader(
                startHtml,
                endHtml,
                startFragment,
                endFragment)
            + HtmlPrefix
            + htmlFragment
            + HtmlSuffix;
    }

    private static string CreateHeader(
        int startHtml,
        int endHtml,
        int startFragment,
        int endFragment)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            HeaderTemplate,
            startHtml,
            endHtml,
            startFragment,
            endFragment);
    }
}
