#nullable enable

using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.Tests;

public sealed class LiteralTextMatcherTests
{
    [Fact]
    public void WholeWordIgnoresLongerWordsAndUnderscoreIdentifiers()
    {
        const string content = "cat category CAT cat_cat cat.";
        var options = new SessionSearchOptions(
            MatchWholeWord: true,
            IsCaseSensitive: false);

        IReadOnlyList<int> matches = LiteralTextMatcher.FindMatches(
            content,
            "cat",
            options);

        Assert.Equal(3, matches.Count);
        Assert.Equal(["cat", "CAT", "cat"], matches.Select(index => content.Substring(index, 3)));
    }

    [Fact]
    public void CaseSensitiveMatchesExactCasingOnly()
    {
        const string content = "ClrMD clrmd ClrMD";
        var options = new SessionSearchOptions(
            MatchWholeWord: false,
            IsCaseSensitive: true);

        IReadOnlyList<int> matches = LiteralTextMatcher.FindMatches(
            content,
            "ClrMD",
            options);

        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void CombinedOptionsRequireWholeWordAndExactCasing()
    {
        const string content = "Bug bug buggy bug_2 bug.";
        var options = new SessionSearchOptions(
            MatchWholeWord: true,
            IsCaseSensitive: true);

        IReadOnlyList<int> matches = LiteralTextMatcher.FindMatches(
            content,
            "bug",
            options);

        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void WholeWordSupportsIdentifiersBeginningWithPunctuation()
    {
        const string content = "PR !777228, not !777228extra.";
        var options = new SessionSearchOptions(
            MatchWholeWord: true,
            IsCaseSensitive: false);

        IReadOnlyList<int> matches = LiteralTextMatcher.FindMatches(
            content,
            "!777228",
            options);

        Assert.Single(matches);
    }
}
