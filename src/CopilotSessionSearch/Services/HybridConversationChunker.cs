#nullable enable

using System.Text;
using System.Text.RegularExpressions;
using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public static partial class HybridConversationChunker
{
    private const int MaximumChunkLength = 1_800;

    public static IReadOnlyList<PendingBlock> CreateBlocks(
        SessionDocument document)
    {
        var blocks = new List<PendingBlock>();

        for (int messageIndex = 0;
            messageIndex < document.Entries.Count;
            messageIndex++)
        {
            ConversationEntry entry = document.Entries[messageIndex];
            IReadOnlyList<string> chunks = ChunkMessage(entry.Content);
            for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                string chunk = chunks[chunkIndex];
                string speaker = entry.Speaker == ConversationSpeaker.User
                    ? "You"
                    : "Copilot";
                blocks.Add(
                    new PendingBlock(
                        document.Session,
                        messageIndex + 1,
                        chunkIndex + 1,
                        speaker,
                        chunk,
                        CreateRetrievalText(
                            document,
                            messageIndex,
                            speaker,
                            chunk)));
            }
        }

        return blocks;
    }

    private static IReadOnlyList<string> ChunkMessage(string content)
    {
        string normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (normalized.Length == 0)
        {
            return [];
        }

        string[] paragraphs = BlankLinePattern()
            .Split(normalized)
            .Where(paragraph => paragraph.Length > 0)
            .ToArray();
        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (string paragraph in paragraphs)
        {
            if (paragraph.Length > MaximumChunkLength)
            {
                FlushCurrent();
                SplitLongParagraph(paragraph, chunks);
                continue;
            }

            int separatorLength = current.Length == 0 ? 0 : 2;
            if (current.Length + separatorLength + paragraph.Length
                > MaximumChunkLength)
            {
                FlushCurrent();
            }

            if (current.Length > 0)
            {
                current.Append("\n\n");
            }

            current.Append(paragraph);
        }

        FlushCurrent();
        return chunks;

        void FlushCurrent()
        {
            if (current.Length == 0)
            {
                return;
            }

            chunks.Add(current.ToString());
            current.Clear();
        }
    }

    private static void SplitLongParagraph(
        string paragraph,
        ICollection<string> chunks)
    {
        string[] lines = paragraph.Split('\n');
        var current = new StringBuilder();

        foreach (string line in lines)
        {
            if (line.Length > MaximumChunkLength)
            {
                FlushCurrent();
                for (int start = 0;
                    start < line.Length;
                    start += MaximumChunkLength)
                {
                    chunks.Add(
                        line.Substring(
                            start,
                            Math.Min(
                                MaximumChunkLength,
                                line.Length - start)));
                }

                continue;
            }

            int separatorLength = current.Length == 0 ? 0 : 1;
            if (current.Length + separatorLength + line.Length
                > MaximumChunkLength)
            {
                FlushCurrent();
            }

            if (current.Length > 0)
            {
                current.Append('\n');
            }

            current.Append(line);
        }

        FlushCurrent();

        void FlushCurrent()
        {
            if (current.Length == 0)
            {
                return;
            }

            chunks.Add(current.ToString());
            current.Clear();
        }
    }

    private static string CreateRetrievalText(
        SessionDocument document,
        int messageIndex,
        string speaker,
        string chunk)
    {
        const int NeighborLength = 300;
        string previous = messageIndex > 0
            ? CreateNeighborText(
                document.Entries[messageIndex - 1].Content,
                NeighborLength)
            : string.Empty;
        string next = messageIndex + 1 < document.Entries.Count
            ? CreateNeighborText(
                document.Entries[messageIndex + 1].Content,
                NeighborLength)
            : string.Empty;

        return
            $"Current message: {chunk}\n" +
            $"Session topic: {document.Session.Name}\n" +
            $"Speaker: {speaker}\n" +
            (previous.Length > 0
                ? $"Previous context: {previous}\n"
                : string.Empty) +
            (next.Length > 0
                ? $"Next context: {next}"
                : string.Empty);
    }

    private static string CreateNeighborText(
        string content,
        int maximumLength)
    {
        string normalized = WhitespacePattern()
            .Replace(content, " ")
            .Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength] + "...";
    }

    [GeneratedRegex(@"\n[ \t]*\n+", RegexOptions.CultureInvariant)]
    private static partial Regex BlankLinePattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();

    public sealed record PendingBlock(
        SessionDescriptor Session,
        int MessageNumber,
        int ChunkNumber,
        string Speaker,
        string Text,
        string RetrievalText);
}
