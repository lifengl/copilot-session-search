#nullable enable

using System.Text.Json;
using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.Tests;

public sealed class ThemePreferenceStoreTests
{
    [Fact]
    public void LoadReturnsNullWhenSettingsDoNotExist()
    {
        using var directory = new TemporaryDirectory();
        var store = new ThemePreferenceStore(
            Path.Combine(directory.Path, "settings.json"));

        Assert.Null(store.Load());
    }

    [Fact]
    public void SaveAndLoadRoundTripAndOverwritePreference()
    {
        using var directory = new TemporaryDirectory();
        string settingsPath = Path.Combine(directory.Path, "settings.json");
        var store = new ThemePreferenceStore(settingsPath);

        store.Save(AppThemePreference.Dark);
        Assert.Equal(AppThemePreference.Dark, store.Load());

        store.Save(AppThemePreference.System);
        Assert.Equal(AppThemePreference.System, store.Load());
        Assert.Contains(
            "\"theme\": \"system\"",
            File.ReadAllText(settingsPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void LoadRejectsInvalidThemeValues()
    {
        using var directory = new TemporaryDirectory();
        string settingsPath = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(settingsPath, """{"theme":"unknown"}""");
        var store = new ThemePreferenceStore(settingsPath);

        Assert.Throws<JsonException>(() => store.Load());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CopilotSessionSearch.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
