#nullable enable

using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.Tests;

public sealed class WpfXamlToHtmlConverterTests
{
    [Fact]
    public void ConvertEncodesTextAndDoesNotExportUnsafeLinks()
    {
        const string xaml =
            """
            <Section xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Paragraph>
                <Hyperlink CommandParameter="javascript:alert(1)">
                  <Run>&lt;script&gt;</Run>
                </Hyperlink>
                <Run> &amp; safe</Run>
              </Paragraph>
            </Section>
            """;

        string html = WpfXamlToHtmlConverter.Convert(xaml);

        Assert.Contains("&lt;script&gt; &amp; safe", html, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
    }
}
