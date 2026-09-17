#nullable enable

namespace CopilotSessionSearch.Models;

public readonly record struct TextMatch(int Start, int Length)
{
    public int End => Start + Length;
}
