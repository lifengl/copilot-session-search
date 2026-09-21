#nullable enable

using System.IO;
using System.Text.Json;
using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public static class AiSearchJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = new(
        JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static AiSearchPlan ParsePlan(string response)
    {
        AiSearchPlan? plan = JsonSerializer.Deserialize<AiSearchPlan>(
            ExtractJsonObject(response),
            SerializerOptions);
        if (plan is null)
        {
            throw new InvalidDataException(
                "The planner returned an empty search plan.");
        }

        plan.RequiredGroups = NormalizeGroups(
            plan.RequiredGroups,
            maximumGroups: 4);
        plan.PreferredGroups = NormalizeGroups(
            plan.PreferredGroups,
            maximumGroups: 8);
        plan.ExactPhrases = NormalizeTerms(
            plan.ExactPhrases,
            maximumTerms: 8);

        if (plan.AllTerms.Count == 0)
        {
            throw new InvalidDataException(
                "The planner returned no usable search terms.");
        }

        return plan;
    }

    public static List<AiRankingItem> ParseRankings(
        string response,
        IReadOnlyList<AiSearchCandidate> candidates)
    {
        AiRankingResponse? ranking = JsonSerializer.Deserialize<AiRankingResponse>(
            ExtractJsonObject(response),
            SerializerOptions);
        if (ranking is null)
        {
            throw new InvalidDataException(
                "The reranker returned an empty response.");
        }

        if (ranking.Results is null)
        {
            throw new InvalidDataException(
                "The reranker response did not contain a results array.");
        }

        if (ranking.Results.Count == 0)
        {
            return [];
        }

        Dictionary<string, AiSearchCandidate> candidatesById = candidates
            .ToDictionary(
                candidate => candidate.CandidateId,
                StringComparer.Ordinal);
        var validResults = new List<AiRankingItem>();

        foreach (AiRankingItem item in ranking.Results.Take(15))
        {
            if (!candidatesById.TryGetValue(
                item.CandidateId,
                out AiSearchCandidate? candidate)
                || item.Score is < 0 or > 100)
            {
                continue;
            }

            HashSet<int> availableMessages = candidate.Evidence
                .Select(evidence => evidence.MessageNumber)
                .ToHashSet();
            item.MessageNumbers = item.MessageNumbers
                .Where(availableMessages.Contains)
                .Distinct()
                .Order()
                .ToList();
            item.Confidence = NormalizeSingleLine(item.Confidence, 20);
            item.Reason = NormalizeSingleLine(item.Reason, 300);
            validResults.Add(item);
        }

        List<AiRankingItem> orderedResults = validResults
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.CandidateId, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count > 0 && orderedResults.Count == 0)
        {
            throw new InvalidDataException(
                "The reranker returned no valid candidate IDs.");
        }

        return orderedResults;
    }

    private static List<AiTermGroup> NormalizeGroups(
        IEnumerable<AiTermGroup>? groups,
        int maximumGroups)
    {
        var normalizedGroups = new List<AiTermGroup>();

        foreach (AiTermGroup group in groups ?? [])
        {
            List<string> terms = NormalizeTerms(
                group.AnyOf,
                maximumTerms: 10);
            if (terms.Count == 0)
            {
                continue;
            }

            normalizedGroups.Add(
                new AiTermGroup
                {
                    Name = NormalizeSingleLine(group.Name, 60),
                    AnyOf = terms,
                });

            if (normalizedGroups.Count == maximumGroups)
            {
                break;
            }
        }

        return normalizedGroups;
    }

    private static List<string> NormalizeTerms(
        IEnumerable<string>? terms,
        int maximumTerms)
    {
        return (terms ?? [])
            .Select(term => NormalizeSingleLine(term, 80))
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maximumTerms)
            .ToList();
    }

    private static string NormalizeSingleLine(
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = string.Join(
            " ",
            value.Split(
                ['\r', '\n', '\t'],
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    private static string ExtractJsonObject(string response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(response);

        int start = response.IndexOf('{');
        int end = response.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new InvalidDataException(
                "The model response did not contain a JSON object.");
        }

        return response[start..(end + 1)];
    }
}
