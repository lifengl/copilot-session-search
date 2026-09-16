#nullable enable

using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public interface IThemeService
{
    AppThemePreference CurrentPreference { get; }

    void Apply(AppThemePreference preference);
}
