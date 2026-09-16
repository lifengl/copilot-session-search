#nullable enable

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public sealed class ThemePreferenceStore : IThemePreferenceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public ThemePreferenceStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CopilotSessionSearch",
                "settings.json"))
    {
    }

    public ThemePreferenceStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        SettingsPath = Path.GetFullPath(settingsPath);
    }

    public string SettingsPath { get; }

    public AppThemePreference? Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return null;
        }

        using FileStream stream = File.OpenRead(SettingsPath);
        ThemeSettings? settings = JsonSerializer.Deserialize<ThemeSettings>(
            stream,
            SerializerOptions);

        return settings?.Theme
            ?? throw new InvalidDataException(
                $"The theme settings file is empty: {SettingsPath}");
    }

    public void Save(AppThemePreference preference)
    {
        if (!Enum.IsDefined(preference))
        {
            throw new ArgumentOutOfRangeException(nameof(preference));
        }

        string directory = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException(
                $"The theme settings path has no directory: {SettingsPath}");
        Directory.CreateDirectory(directory);

        string temporaryPath = SettingsPath + ".tmp";
        using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(
                stream,
                new ThemeSettings(preference),
                SerializerOptions);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(
                namingPolicy: JsonNamingPolicy.CamelCase,
                allowIntegerValues: false));
        return options;
    }

    private sealed record ThemeSettings(AppThemePreference Theme);
}
