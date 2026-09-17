#nullable enable

using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace CopilotSessionSearch.Services;

public static class WpfXamlToHtmlConverter
{
    private const double DefaultFontSize = 12;

    public static string Convert(string xaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xaml);

        XDocument document = XDocument.Parse(xaml, LoadOptions.None);
        XElement root = document.Root
            ?? throw new InvalidDataException("The selected XAML has no root element.");
        double baseFontSize = GetDoubleAttribute(root, "FontSize")
            ?? DefaultFontSize;
        var html = new StringBuilder();
        html.Append("<div>");
        AppendElement(
            root,
            html,
            new RenderContext(
                baseFontSize,
                IsTableHeader: false,
                CompactParagraphs: false));
        html.Append("</div>");
        return html.ToString();
    }

    private static void AppendElement(
        XElement element,
        StringBuilder html,
        RenderContext context)
    {
        switch (element.Name.LocalName)
        {
            case "FlowDocument":
            case "Section":
                AppendChildren(element, html, context);
                break;

            case "Paragraph":
                AppendParagraph(element, html, context);
                break;

            case "Run":
                AppendInline(element, html, context, appendText: true);
                break;

            case "Span":
                AppendInline(element, html, context, appendText: false);
                break;

            case "Bold":
                AppendWrappedChildren(element, html, context, "strong");
                break;

            case "Italic":
                AppendWrappedChildren(element, html, context, "em");
                break;

            case "Underline":
                AppendWrappedChildren(element, html, context, "u");
                break;

            case "Hyperlink":
                AppendHyperlink(element, html, context);
                break;

            case "LineBreak":
                html.Append("<br>");
                break;

            case "List":
                AppendList(element, html, context);
                break;

            case "ListItem":
                html.Append("<li>");
                AppendChildren(
                    element,
                    html,
                    context with { CompactParagraphs = true });
                html.Append("</li>");
                break;

            case "Table":
                html.Append(
                    "<table border=\"1\" cellpadding=\"4\" cellspacing=\"0\" " +
                    "style=\"border-collapse:collapse;border:1px solid #888\">");
                AppendChildren(element, html, context);
                html.Append("</table>");
                break;

            case "TableRowGroup":
                AppendChildren(
                    element,
                    html,
                    context with
                    {
                        IsTableHeader = IsHeaderRowGroup(element),
                    });
                break;

            case "TableRow":
                html.Append("<tr>");
                AppendChildren(element, html, context);
                html.Append("</tr>");
                break;

            case "TableCell":
                AppendTableCell(element, html, context);
                break;

            case "BlockUIContainer":
            case "Figure":
            case "Floater":
            case "InlineUIContainer":
                AppendChildren(element, html, context);
                break;

            default:
                if (!IsNonContentElement(element.Name.LocalName))
                {
                    AppendChildren(element, html, context);
                }

                break;
        }
    }

    private static void AppendParagraph(
        XElement element,
        StringBuilder html,
        RenderContext context)
    {
        string tagName = context.CompactParagraphs
            ? "div"
            : GetParagraphTag(element, context.BaseFontSize);
        string style = context.CompactParagraphs
            ? " style=\"margin:0\""
            : string.Empty;

        html.Append('<').Append(tagName).Append(style).Append('>');
        AppendInline(element, html, context, appendText: false);
        html.Append("</").Append(tagName).Append('>');
    }

    private static void AppendInline(
        XElement element,
        StringBuilder html,
        RenderContext context,
        bool appendText)
    {
        IReadOnlyList<string> tags = GetFormattingTags(element);
        foreach (string tag in tags)
        {
            html.Append('<').Append(tag).Append('>');
        }

        if (appendText)
        {
            string text = GetRunText(element);
            AppendEncodedText(html, text);
        }
        else
        {
            AppendChildren(element, html, context);
        }

        for (int index = tags.Count - 1; index >= 0; index--)
        {
            string tag = tags[index];
            html.Append("</").Append(GetTagName(tag)).Append('>');
        }
    }

    private static void AppendHyperlink(
        XElement element,
        StringBuilder html,
        RenderContext context)
    {
        string? href =
            GetSafeHref(GetAttribute(element, "NavigateUri"))
            ?? GetSafeHref(GetAttribute(element, "CommandParameter"))
            ?? GetSafeHref(GetAttribute(element, "ToolTip"));
        if (href is null)
        {
            AppendChildren(element, html, context);
            return;
        }

        html.Append("<a href=\"")
            .Append(WebUtility.HtmlEncode(href))
            .Append("\">");
        AppendChildren(element, html, context);
        html.Append("</a>");
    }

    private static void AppendList(
        XElement element,
        StringBuilder html,
        RenderContext context)
    {
        string? markerStyle = GetAttribute(element, "MarkerStyle");
        bool isOrdered = markerStyle is
            "Decimal"
            or "LowerLatin"
            or "UpperLatin"
            or "LowerRoman"
            or "UpperRoman";
        string tagName = isOrdered ? "ol" : "ul";
        string? listStyle = GetListStyle(markerStyle);

        html.Append('<').Append(tagName);
        if (listStyle is not null)
        {
            html.Append(" style=\"list-style-type:")
                .Append(listStyle)
                .Append("\"");
        }

        html.Append('>');
        AppendChildren(element, html, context);
        html.Append("</").Append(tagName).Append('>');
    }

    private static void AppendTableCell(
        XElement element,
        StringBuilder html,
        RenderContext context)
    {
        string tagName = context.IsTableHeader ? "th" : "td";
        html.Append('<')
            .Append(tagName)
            .Append(
                " style=\"border:1px solid #888;padding:4px;text-align:left;vertical-align:top\">");
        AppendChildren(
            element,
            html,
            context with { CompactParagraphs = true });
        html.Append("</").Append(tagName).Append('>');
    }

    private static void AppendWrappedChildren(
        XElement element,
        StringBuilder html,
        RenderContext context,
        string tagName)
    {
        html.Append('<').Append(tagName).Append('>');
        AppendChildren(element, html, context);
        html.Append("</").Append(tagName).Append('>');
    }

    private static void AppendChildren(
        XElement element,
        StringBuilder html,
        RenderContext context)
    {
        foreach (XNode child in element.Nodes())
        {
            switch (child)
            {
                case XElement childElement:
                    AppendElement(childElement, html, context);
                    break;

                case XText text:
                    AppendEncodedText(html, text.Value);
                    break;
            }
        }
    }

    private static void AppendEncodedText(StringBuilder html, string text)
    {
        string normalizedText = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        string[] lines = normalizedText.Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                html.Append("<br>");
            }

            html.Append(WebUtility.HtmlEncode(lines[index]));
        }
    }

    private static IReadOnlyList<string> GetFormattingTags(XElement element)
    {
        var tags = new List<string>();

        if (IsBoldWeight(GetAttribute(element, "FontWeight")))
        {
            tags.Add("strong");
        }

        if (string.Equals(
            GetAttribute(element, "FontStyle"),
            "Italic",
            StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("em");
        }

        if (GetAttribute(element, "TextDecorations")?.Contains(
            "Underline",
            StringComparison.OrdinalIgnoreCase) is true)
        {
            tags.Add("u");
        }

        if (IsMonospaceFont(GetAttribute(element, "FontFamily")))
        {
            tags.Add(
                "code style=\"font-family:Consolas,'Courier New',monospace;" +
                "white-space:pre-wrap\"");
        }

        return tags;
    }

    private static string GetTagName(string tag)
    {
        int separatorIndex = tag.IndexOf(' ');
        return separatorIndex < 0
            ? tag
            : tag[..separatorIndex];
    }

    private static string GetRunText(XElement element)
    {
        string? textAttribute = GetAttribute(element, "Text");
        return textAttribute
            ?? string.Concat(element.Nodes().OfType<XText>().Select(text => text.Value));
    }

    private static string GetParagraphTag(
        XElement element,
        double baseFontSize)
    {
        double fontSize = GetDoubleAttribute(element, "FontSize")
            ?? baseFontSize;
        double ratio = fontSize / baseFontSize;

        if (ratio >= 1.9)
        {
            return "h1";
        }

        if (ratio >= 1.55)
        {
            return "h2";
        }

        if (ratio >= 1.35)
        {
            return "h3";
        }

        if (ratio >= 1.2)
        {
            return "h4";
        }

        if (ratio > 1)
        {
            return "h5";
        }

        return IsBoldWeight(GetAttribute(element, "FontWeight"))
            ? "h6"
            : "p";
    }

    private static bool IsHeaderRowGroup(XElement element)
    {
        bool isFirstRowGroup = !element
            .ElementsBeforeSelf()
            .Any(sibling => sibling.Name.LocalName == "TableRowGroup");
        return isFirstRowGroup
            && IsHeaderWeight(GetAttribute(element, "FontWeight"));
    }

    private static bool IsHeaderWeight(string? fontWeight)
    {
        return IsBoldWeight(fontWeight)
            || string.Equals(
                fontWeight,
                "SemiBold",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBoldWeight(string? fontWeight)
    {
        if (fontWeight is null)
        {
            return false;
        }

        if (int.TryParse(
            fontWeight,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int numericWeight))
        {
            return numericWeight >= 700;
        }

        return fontWeight is
            "Bold"
            or "ExtraBold"
            or "Black"
            or "Heavy";
    }

    private static bool IsMonospaceFont(string? fontFamily)
    {
        if (fontFamily is null)
        {
            return false;
        }

        return fontFamily.Contains("Consolas", StringComparison.OrdinalIgnoreCase)
            || fontFamily.Contains("Courier", StringComparison.OrdinalIgnoreCase)
            || fontFamily.Contains("Cascadia Mono", StringComparison.OrdinalIgnoreCase)
            || fontFamily.Contains("monospace", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetSafeHref(string? value)
    {
        if (value is null
            || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        return uri.Scheme is "http" or "https" or "mailto"
            ? uri.AbsoluteUri
            : null;
    }

    private static string? GetListStyle(string? markerStyle)
    {
        return markerStyle switch
        {
            "LowerLatin" => "lower-alpha",
            "UpperLatin" => "upper-alpha",
            "LowerRoman" => "lower-roman",
            "UpperRoman" => "upper-roman",
            "Circle" => "circle",
            "Square" => "square",
            "Box" => "square",
            _ => null,
        };
    }

    private static double? GetDoubleAttribute(
        XElement element,
        string attributeName)
    {
        string? value = GetAttribute(element, attributeName);
        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double result)
            ? result
            : null;
    }

    private static string? GetAttribute(
        XElement element,
        string attributeName)
    {
        return element
            .Attributes()
            .FirstOrDefault(
                attribute => attribute.Name.LocalName == attributeName)
            ?.Value;
    }

    private static bool IsNonContentElement(string elementName)
    {
        return elementName is
                "Style"
                or "Setter"
                or "Trigger"
                or "TableColumn"
            || elementName.EndsWith(".Resources", StringComparison.Ordinal)
            || elementName.EndsWith(".Command", StringComparison.Ordinal)
            || elementName.EndsWith(".Style", StringComparison.Ordinal)
            || elementName.EndsWith(".Columns", StringComparison.Ordinal);
    }

    private readonly record struct RenderContext(
        double BaseFontSize,
        bool IsTableHeader,
        bool CompactParagraphs);
}
