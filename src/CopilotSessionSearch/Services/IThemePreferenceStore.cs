#nullable enable

using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public interface IThemePreferenceStore
{
    AppThemePreference? Load();

    void Save(AppThemePreference preference);
}
