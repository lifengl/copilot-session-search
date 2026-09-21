#nullable enable

using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

internal static class AiSearchCandidateBuilder
{
    private const int MaximumCandidates = 24;
    private const int MaximumEvidenceCharacters = 3_600;
    private const int MaximumEvidenceMessages = 6;
    private const int MaximumCopilotEvidenceLength = 1_800;
    private const int MaximumUserEvidenceLength = 600;

    public static AiSearchCandidate[] Create(
        string query,
        IReadOnlyList<HybridRankedBlock> results,
        IReadOnlyDictionary<string, SessionDocument>
            documentsBySessionId)
    {
        IReadOnlyList<string> terms =
            HybridSearchQuery.ExtractTerms(query);
        CandidateSource[] sources = results
            .Select(
                result => CreateCandidateSource(
                    result,
                    documentsBySessionId))
            .OfType<CandidateSource>()
            .ToArray();

        return sources
            .GroupBy(
                source => new CandidateExchangeKey(
                    source.Result.Block.SessionId,
                    source.Exchange.StartMessageNumber,
                    source.Exchange.EndMessageNumber))
            .Select(
                group =>
                {
                    CandidateSource[] rankedSources = group
                        .OrderByDescending(
                            source =>
                                source.Result.HybridScore)
                        .ThenBy(
                            source =>
                                source.Result.Block.MessageNumber)
                        .ToArray();
                    return new CandidateGroup(
                        rankedSources[0].Document,
                        rankedSources[0].Exchange,
                        rankedSources);
                })
            .OrderByDescending(
                group =>
                    group.Sources[0].Result.HybridScore)
            .ThenByDescending(
                group =>
                    group.Document.Session.ModifiedTime)
            .Take(MaximumCandidates)
            .Select(
                (group, index) => CreateCandidate(
                    query,
                    terms,
                    group,
                    index))
            .ToArray();
    }

    private static CandidateSource? CreateCandidateSource(
        HybridRankedBlock result,
        IReadOnlyDictionary<string, SessionDocument>
            documentsBySessionId)
    {
        if (!documentsBySessionId.TryGetValue(
            result.Block.SessionId,
            out SessionDocument? document))
        {
            return null;
        }

        int anchorIndex = result.Block.MessageNumber - 1;
        if (anchorIndex < 0
            || anchorIndex >= document.Entries.Count)
        {
            return null;
        }

        return new CandidateSource(
            result,
            document,
            FindExchange(
                document,
                anchorIndex));
    }

    private static MessageRange FindExchange(
        SessionDocument document,
        int anchorIndex)
    {
        int startIndex = anchorIndex;
        while (startIndex > 0
            && !StartsNewExchange(
                document.Entries,
                startIndex))
        {
            startIndex--;
        }

        int endIndex = anchorIndex;
        while (endIndex + 1 < document.Entries.Count
            && !StartsNewExchange(
                document.Entries,
                endIndex + 1))
        {
            endIndex++;
        }

        return new MessageRange(
            startIndex + 1,
            endIndex + 1);
    }

    private static bool StartsNewExchange(
        IReadOnlyList<ConversationEntry> entries,
        int index)
    {
        return index > 0
            && entries[index].Speaker
                == ConversationSpeaker.User
            && entries[index - 1].Speaker
                == ConversationSpeaker.Copilot;
    }

    private static AiSearchCandidate CreateCandidate(
        string query,
        IReadOnlyList<string> terms,
        CandidateGroup group,
        int index)
    {
        HybridRankedBlock bestResult =
            group.Sources[0].Result;
        HybridSearchBlock block = bestResult.Block;
        HashSet<int> preferredAnswerMessageNumbers =
            CreatePreferredAnswerMessageNumbers(
                group);
        IReadOnlyList<CandidateEvidence> evidence =
            CreateEvidence(
                query,
                terms,
                group,
                preferredAnswerMessageNumbers);
        string combinedEvidence = string.Join(
            "\n",
            evidence.Select(item => item.Text));
        int tableRows = combinedEvidence
            .Split('\n')
            .Count(
                line => line.Count(
                    character => character == '|') >= 2);
        int distinctTermsMatched = terms.Count(
            term => combinedEvidence.Contains(
                term,
                StringComparison.OrdinalIgnoreCase));
        int exactPhrasesMatched = combinedEvidence.Contains(
            query,
            StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

        return new AiSearchCandidate(
            $"H{index + 1:D3}",
            block.SessionId,
            block.SessionName,
            block.ModifiedTime,
            bestResult.HybridScore,
            new AiLocalMatchSignals(
                RequiredGroupsMatched: 0,
                RequiredGroupsTotal: 0,
                PreferredGroupsMatched: 0,
                PreferredGroupsTotal: 0,
                ExactPhrasesMatched: exactPhrasesMatched,
                DistinctTermsMatched: distinctTermsMatched,
                QuantitativeSignals: 0,
                MarkdownTableRows: tableRows),
            evidence)
        {
            Document = group.Document,
        };
    }

    private static IReadOnlyList<CandidateEvidence>
        CreateEvidence(
            string query,
            IReadOnlyList<string> terms,
            CandidateGroup group,
            IReadOnlySet<int>
                preferredAnswerMessageNumbers)
    {
        Dictionary<int, CandidateSource> bestSourcesByMessage =
            group.Sources
            .GroupBy(
                source =>
                    source.Result.Block.MessageNumber)
            .ToDictionary(
                sources => sources.Key,
                sources => sources
                    .OrderByDescending(
                        source =>
                            source.Result.HybridScore)
                    .First());
        var options = new List<EvidenceOption>();
        for (int messageNumber =
                group.Exchange.StartMessageNumber;
            messageNumber <=
                group.Exchange.EndMessageNumber;
            messageNumber++)
        {
            ConversationEntry entry =
                group.Document.Entries[messageNumber - 1];
            bestSourcesByMessage.TryGetValue(
                messageNumber,
                out CandidateSource? bestSource);
            string text = CreatePreview(
                bestSource?.Result.Block.Text
                    ?? entry.Content,
                entry.Speaker
                    == ConversationSpeaker.User
                    ? MaximumUserEvidenceLength
                    : MaximumCopilotEvidenceLength);
            options.Add(
                new EvidenceOption(
                    messageNumber,
                    entry.Speaker,
                    text,
                    bestSource?.Result.HybridScore
                        ?? 0,
                    text.Contains(
                        query,
                        StringComparison.OrdinalIgnoreCase),
                    terms.Count(
                        term => text.Contains(
                            term,
                            StringComparison.OrdinalIgnoreCase))));
        }

        int totalCharacters = options.Sum(
            option => option.Text.Length);
        if (options.Count <= MaximumEvidenceMessages
            && totalCharacters <=
                MaximumEvidenceCharacters)
        {
            return options
                .Select(
                    option => CreateEvidence(
                        option,
                        preferredAnswerMessageNumbers))
                .ToArray();
        }

        int anchorMessageNumber =
            group.Sources[0].Result.Block.MessageNumber;
        var selectedMessageNumbers = new HashSet<int>();
        int selectedCharacters = 0;

        TryAdd(
            options.Single(
                option =>
                    option.MessageNumber
                    == anchorMessageNumber));

        EvidenceOption? finalCopilot = options
            .LastOrDefault(
                option =>
                    option.Speaker
                    == ConversationSpeaker.Copilot);
        if (finalCopilot is not null)
        {
            TryAdd(finalCopilot);
        }

        foreach (EvidenceOption option in options
            .Where(
                option =>
                    option.Speaker
                    == ConversationSpeaker.Copilot)
            .OrderByDescending(
                option => option.LocalScore)
            .ThenByDescending(
                option => option.ExactQueryMatch)
            .ThenByDescending(
                option => option.MatchedTerms)
            .ThenByDescending(
                option => option.MessageNumber))
        {
            TryAdd(option);
        }

        foreach (EvidenceOption option in options
            .OrderByDescending(
                option => option.LocalScore)
            .ThenByDescending(
                option => option.ExactQueryMatch)
            .ThenByDescending(
                option => option.MatchedTerms)
            .ThenBy(
                option => option.MessageNumber))
        {
            TryAdd(option);
        }

        return options
            .Where(
                option =>
                    selectedMessageNumbers.Contains(
                        option.MessageNumber))
            .OrderBy(
                option => option.MessageNumber)
            .Select(
                option => CreateEvidence(
                    option,
                    preferredAnswerMessageNumbers))
            .ToArray();

        void TryAdd(EvidenceOption option)
        {
            if (selectedMessageNumbers.Contains(
                option.MessageNumber)
                || selectedMessageNumbers.Count
                    == MaximumEvidenceMessages
                || selectedCharacters + option.Text.Length
                    > MaximumEvidenceCharacters)
            {
                return;
            }

            selectedMessageNumbers.Add(
                option.MessageNumber);
            selectedCharacters += option.Text.Length;
        }
    }

    private static HashSet<int>
        CreatePreferredAnswerMessageNumbers(
            CandidateGroup group)
    {
        var preferred = group.Sources
            .Select(
                source =>
                    source.Result.Block.MessageNumber)
            .Where(
                messageNumber =>
                    group.Document
                        .Entries[messageNumber - 1]
                        .Speaker
                    == ConversationSpeaker.Copilot)
            .ToHashSet();
        for (int messageNumber =
                group.Exchange.EndMessageNumber;
            messageNumber >=
                group.Exchange.StartMessageNumber;
            messageNumber--)
        {
            if (group.Document
                    .Entries[messageNumber - 1]
                    .Speaker
                == ConversationSpeaker.Copilot)
            {
                preferred.Add(messageNumber);
                break;
            }
        }

        return preferred;
    }

    private static CandidateEvidence CreateEvidence(
        EvidenceOption option,
        IReadOnlySet<int>
            preferredAnswerMessageNumbers)
    {
        return new CandidateEvidence(
            option.MessageNumber,
            option.Speaker
                == ConversationSpeaker.User
                ? "You"
                : "Copilot",
            option.Text,
            preferredAnswerMessageNumbers.Contains(
                option.MessageNumber));
    }

    private static string CreatePreview(
        string content,
        int maximumLength)
    {
        return content.Length <= maximumLength
            ? content
            : content[..(maximumLength - 3)] + "...";
    }

    private sealed record CandidateSource(
        HybridRankedBlock Result,
        SessionDocument Document,
        MessageRange Exchange);

    private sealed record CandidateGroup(
        SessionDocument Document,
        MessageRange Exchange,
        IReadOnlyList<CandidateSource> Sources);

    private sealed record EvidenceOption(
        int MessageNumber,
        ConversationSpeaker Speaker,
        string Text,
        double LocalScore,
        bool ExactQueryMatch,
        int MatchedTerms);

    private readonly record struct CandidateExchangeKey(
        string SessionId,
        int StartMessageNumber,
        int EndMessageNumber);

    private readonly record struct MessageRange(
        int StartMessageNumber,
        int EndMessageNumber);
}
