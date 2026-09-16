#nullable enable

using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.ViewModels;

public sealed record ThemeOption(
    AppThemePreference Preference,
    string DisplayName)
{
    public override string ToString()
    {
        return DisplayName;
    }
}
