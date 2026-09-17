#nullable enable

using System.Text.RegularExpressions;
using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.Tests;

public sealed class TextSearchPatternTests
{
    [Fact]
    public void WholeWordIgnoresLongerWordsAndUnderscoreIdentifiers()
    {
        const string content = "cat category CAT cat_cat cat.";
        var options = new SessionSearchOptions(
            MatchWholeWord: true,
            IsCaseSensitive: false,
            UseRegularExpression: false);

        IReadOnlyList<TextMatch> matches = TextSearchPattern.Create(
            "cat",
            options).FindMatches(content);

        Assert.Equal(3, matches.Count);
        Assert.Equal(
            ["cat", "CAT", "cat"],
            matches.Select(match => content.Substring(match.Start, match.Length)));
    }

    [Fact]
    public void CaseSensitiveMatchesExactCasingOnly()
    {
        const string content = "ClrMD clrmd ClrMD";
        var options = new SessionSearchOptions(
            MatchWholeWord: false,
            IsCaseSensitive: true,
            UseRegularExpression: false);

        IReadOnlyList<TextMatch> matches = TextSearchPattern.Create(
            "ClrMD",
            options).FindMatches(content);

        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void CombinedOptionsRequireWholeWordAndExactCasing()
    {
        const string content = "Bug bug buggy bug_2 bug.";
        var options = new SessionSearchOptions(
            MatchWholeWord: true,
            IsCaseSensitive: true,
            UseRegularExpression: false);

        IReadOnlyList<TextMatch> matches = TextSearchPattern.Create(
            "bug",
            options).FindMatches(content);

        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void WholeWordSupportsIdentifiersBeginningWithPunctuation()
    {
        const string content = "PR !777228, not !777228extra.";
        var options = new SessionSearchOptions(
            MatchWholeWord: true,
            IsCaseSensitive: false,
            UseRegularExpression: false);

        IReadOnlyList<TextMatch> matches = TextSearchPattern.Create(
            "!777228",
            options).FindMatches(content);

        Assert.Single(matches);
    }

    [Fact]
    public void RegularExpressionIgnoresCaseAndPreservesVariableMatchLengths()
    {
        const string content = "HotReload, hot reload, and HOTRELOAD.";
        var options = new SessionSearchOptions(
            MatchWholeWord: false,
            IsCaseSensitive: false,
            UseRegularExpression: true);

        IReadOnlyList<TextMatch> matches = TextSearchPattern.Create(
            "hot ?reload",
            options).FindMatches(content);

        Assert.Equal(
            ["HotReload", "hot reload", "HOTRELOAD"],
            matches.Select(match => content.Substring(match.Start, match.Length)));
    }

    [Fact]
    public void CaseSensitiveRegularExpressionMatchesExactCasingOnly()
    {
        const string content = "HotReload, hot reload, and HOTRELOAD.";
        var options = new SessionSearchOptions(
            MatchWholeWord: false,
            IsCaseSensitive: true,
            UseRegularExpression: true);

        IReadOnlyList<TextMatch> matches = TextSearchPattern.Create(
            "hot ?reload",
            options).FindMatches(content);

        TextMatch match = Assert.Single(matches);
        Assert.Equal("hot reload", content.Substring(match.Start, match.Length));
    }

    [Fact]
    public void WholeWordFiltersRegularExpressionMatchesUsingMatchedText()
    {
        const string content = "HotReload HotReloaded hot reload.";
        var options = new SessionSearchOptions(
            MatchWholeWord: true,
            IsCaseSensitive: false,
            UseRegularExpression: true);

        IReadOnlyList<TextMatch> matches = TextSearchPattern.Create(
            "hot ?reload",
            options).FindMatches(content);

        Assert.Equal(
            ["HotReload", "hot reload"],
            matches.Select(match => content.Substring(match.Start, match.Length)));
    }

    [Fact]
    public void InvalidRegularExpressionIsRejected()
    {
        var options = new SessionSearchOptions(
            MatchWholeWord: false,
            IsCaseSensitive: false,
            UseRegularExpression: true);

        Assert.Throws<RegexParseException>(
            () => TextSearchPattern.Create("[", options));
    }
}
