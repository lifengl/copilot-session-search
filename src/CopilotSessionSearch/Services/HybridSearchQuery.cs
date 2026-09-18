#nullable enable

using System.Text.RegularExpressions;
using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public static partial class HybridSearchQuery
{
    private static readonly HashSet<string> StopWords = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "about",
        "an",
        "and",
        "between",
        "earlier",
        "find",
        "for",
        "from",
        "in",
        "is",
        "of",
        "on",
        "session",
        "sessions",
        "the",
        "to",
        "with",
    };

    public static IReadOnlyList<string> ExtractTerms(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        return TokenPattern()
            .Matches(query)
            .Select(match => match.Value.Trim('.', '-', '_'))
            .Where(term => term.Length >= 2)
            .Where(term => !StopWords.Contains(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string CreateMatchExpression(
        IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            throw new ArgumentException(
                "At least one search term is required.",
                nameof(terms));
        }

        return string.Join(
            " OR ",
            terms.Select(
                term => $"\"{term.Replace("\"", "\"\"", StringComparison.Ordinal)}\""));
    }

    public static IReadOnlyList<string> CreateSemanticQueries(
        string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        if (!IsEvidenceQuery(query))
        {
            return [query];
        }

        return
        [
            query,
            "Detailed technical evidence answering this request, including " +
            "measurements, tables, concrete timings or counts, comparison, " +
            "root cause, and conclusions: " + query,
        ];
    }

    public static double ScoreExact(
        string query,
        IReadOnlyList<string> terms,
        HybridSearchBlock block)
    {
        string searchableText =
            block.SessionName + "\n" + block.Text;
        int matchedTerms = terms.Count(
            term => FindTermPosition(searchableText, term) >= 0);
        if (matchedTerms == 0)
        {
            return 0;
        }

        double coverage = matchedTerms / (double)terms.Count;
        double score = matchedTerms * 10 + coverage * 30;
        if (searchableText.Contains(
            query,
            StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        int proximitySpan = GetProximitySpan(searchableText, terms);
        if (proximitySpan >= 0)
        {
            score += proximitySpan switch
            {
                <= 80 => 25,
                <= 200 => 15,
                <= 500 => 8,
                _ => 0,
            };
        }

        if (IsEvidenceQuery(query))
        {
            score += Math.Min(
                QuantitativeSignalPattern().Matches(block.Text).Count,
                10) * 3;
            score += Math.Min(
                block.Text
                    .Split('\n')
                    .Count(
                        line => line.Count(
                            character => character == '|') >= 2),
                8) * 2;
        }

        return score;
    }

    private static int GetProximitySpan(
        string content,
        IReadOnlyList<string> terms)
    {
        int[] positions = terms
            .Select(
                term => FindTermPosition(content, term))
            .Where(position => position >= 0)
            .Order()
            .ToArray();
        return positions.Length < 2
            ? -1
            : positions[^1] - positions[0];
    }

    private static bool IsEvidenceQuery(string query)
    {
        string[] indicators =
        [
            "benchmark",
            "compare",
            "comparison",
            "deadlock",
            "duration",
            "elapsed",
            "fault",
            "hang",
            "nfe",
            "performance",
            "regression",
            "timing",
        ];
        return indicators.Any(
            indicator => query.Contains(
                indicator,
                StringComparison.OrdinalIgnoreCase));
    }

    private static int FindTermPosition(
        string content,
        string term)
    {
        if (!term.EndsWith(
            ".x",
            StringComparison.OrdinalIgnoreCase))
        {
            return content.IndexOf(
                term,
                StringComparison.OrdinalIgnoreCase);
        }

        string prefix = term[..^1];
        int searchStart = 0;
        while (searchStart < content.Length)
        {
            int matchStart = content.IndexOf(
                prefix,
                searchStart,
                StringComparison.OrdinalIgnoreCase);
            if (matchStart < 0)
            {
                return -1;
            }

            int digitIndex = matchStart + prefix.Length;
            if (digitIndex < content.Length
                && char.IsDigit(content[digitIndex]))
            {
                return matchStart;
            }

            searchStart = matchStart + prefix.Length;
        }

        return -1;
    }

    [GeneratedRegex(
        @"[A-Za-z][A-Za-z0-9_.-]*|\d+(?:\.\d+)*(?:\.x)?",
        RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [GeneratedRegex(
        @"\b\d+(?:\.\d+)?\s*(?:%|x|ms|msec|s|sec|secs|second|seconds|mb|gb|kb|bytes?)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex QuantitativeSignalPattern();
}
