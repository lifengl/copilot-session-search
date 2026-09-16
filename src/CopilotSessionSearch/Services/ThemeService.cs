#nullable enable

using System.Windows;
using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public sealed class ThemeService : IThemeService
{
    public AppThemePreference CurrentPreference { get; private set; } =
        AppThemePreference.System;

    public void Apply(AppThemePreference preference)
    {
        Application application = Application.Current
            ?? throw new InvalidOperationException("The WPF application is not running.");

#pragma warning disable WPF0001
        application.ThemeMode = preference switch
        {
            AppThemePreference.System => ThemeMode.System,
            AppThemePreference.Light => ThemeMode.Light,
            AppThemePreference.Dark => ThemeMode.Dark,
            _ => throw new ArgumentOutOfRangeException(nameof(preference)),
        };
#pragma warning restore WPF0001

        CurrentPreference = preference;
    }
}
