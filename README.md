# Copilot Session Search

A small Windows application for searching local GitHub Copilot CLI conversation history.

## Features

- Searches visible user and final Copilot messages using literal text or regular expressions.
- Reads session metadata and persisted events through the public `GitHub.Copilot.SDK`.
- Processes up to four sessions concurrently and adds matching sessions to the result list as they complete.
- Keeps results ordered by last activity, newest first.
- Shows up to three excerpts for each session in the main window.
- Opens modeless detail windows containing every matching message rendered as Markdown.
- Supports selecting and copying rendered text, plus a one-click copy of each full message.
- Makes session metadata and the shell-ready resume command directly selectable.
- Cancels an active search without removing results that have already been found.
- Opens a new Windows Terminal or PowerShell window and resumes a selected session.
- Supports and persists System, Light, and Dark Fluent themes.
- Uses an original AI-search icon for the executable, taskbar, and application windows.
- Shows live search status, progress, and matched-session totals in a bottom status bar.
- Shows an example watermark in the empty search field for first-time guidance.
- Offers regular-expression, whole-word, and case-sensitive options from the Search dropdown.

## Requirements

- Windows
- .NET 10 SDK
- GitHub Copilot CLI local session history under `COPILOT_HOME` or `%USERPROFILE%\.copilot`
- A signed-in GitHub Copilot CLI environment

## Build and run

```powershell
Set-Location Q:\ws\CopilotSessionSearch
dotnet build CopilotSessionSearch.sln
dotnet run --project src\CopilotSessionSearch\CopilotSessionSearch.csproj
```

Run the tests with:

```powershell
dotnet test CopilotSessionSearch.sln
```

## Search behavior

The application takes a snapshot of the available session list when the first search starts. It does not subscribe to session lifecycle updates, so sessions created or modified afterward appear after restarting the application.

Session histories are loaded on demand and cached in memory. Later searches reuse the cached visible messages. The main list contains no session-count limit, but each session item shows at most three excerpts. The detail window groups matching sections by conversation message and shows every matching message.

Detail messages are rendered through `MdXaml` as selectable WPF `FlowDocument` content. Headings, tables, lists, links, inline code, and fenced code blocks receive Markdown formatting. Select text and press Ctrl+C or use the document context menu. The clipboard retains WPF's plain-text, RTF, and XAML formats and adds HTML, allowing applications such as Word to preserve headings, emphasis, links, lists, inline code, and table structure. Plain-text consumers keep the existing copy behavior.

The **Copy whole message** button copies the entire rendered conversation message with rich formats while retaining the original Markdown as the clipboard's plain-text representation. Word and other rich editors can preserve tables and formatting, while Markdown-oriented tools receive the original source.

Markdown image syntax is converted into an ordinary link before rendering, and raw HTML image elements are escaped. This prevents the viewer from automatically fetching remote or local image content.

Session name, ID, working directory, repository, dates, span, search query, and `copilot --resume=<session-id>` command are selectable read-only fields. **Copy session info** places all of those values into the clipboard as a shell-friendly text block.

## Keyboard and mouse

- `Ctrl+F` focuses and selects the search text.
- `Enter` in the search box starts a search.
- `Alt+S` starts a search, `Alt+C` cancels an active search, and `Escape` also cancels.
- `Alt+O` opens the Search options dropdown.
- In either result list, `Up`, `Down`, `Page Up`, `Page Down`, `Home`, and `End` change selection and bring the selected item into view.
- `Enter` on a session opens its detail window.
- `Enter` on a detail message focuses its rendered Markdown.
- `Escape` closes the detail window from the message list, rendered Markdown, or footer buttons.
- `Ctrl+C` copies a non-empty rendered-text selection with rich clipboard formats. Otherwise, on a selected detail message, it performs the same rich whole-message copy as **Copy whole message**.
- In the detail window, `Alt+I` copies session information, `Alt+C` copies the selected message, `Alt+R` resumes the session, and `Alt+O` closes the window.
- The mouse wheel over rendered Markdown scrolls the message list. Long messages keep their own scrollbar for direct scrolling.
- The detail message list keeps its outer scrollbar visible and uses pixel scrolling for partially visible messages.

## Theme

The first run defaults to **System**. After the user selects System, Light, or Dark, that preference is saved for the current Windows user and restored before the main window is shown on later launches. System mode follows the current Windows app theme and updates automatically when Windows changes between light, dark, or high-contrast modes.

Selecting **Light** or **Dark** applies that Fluent theme immediately to the main window, open detail windows, controls, scrollbars, selection visuals, and rendered Markdown text. Select **System** again to resume following Windows. `Alt+T` opens the theme menu.

Theme support uses the built-in .NET 10 WPF `Application.ThemeMode` API. This API is currently marked experimental by WPF, and its `WPF0001` warning suppression is isolated to `ThemeService`.

The preference is stored atomically as readable JSON at `%LOCALAPPDATA%\CopilotSessionSearch\settings.json`. An unreadable or invalid file produces a warning and falls back to System instead of preventing the application from starting.

MdXaml emits some fixed light-theme colors, so `SelectableMarkdownViewer` normalizes headings, tables, code backgrounds, links, and other text elements against the active WPF foreground. This avoids white table rows and black headings when the application is dark.

Fenced code blocks use AvalonEdit. Light mode retains its language syntax highlighting; Dark and High Contrast modes disable the fixed syntax palette and use selectable, theme-aware monospace text so every token remains readable.

## Search options

The dropdown beside Search contains three independent options:

- **Match whole word** requires boundaries around the actual text matched when it begins or ends with a letter, digit, or underscore. This works with literal and regular-expression searches and avoids matching short terms inside longer words or identifiers.
- **Case sensitive** uses exact casing. When it is off, regular expressions use `RegexOptions.IgnoreCase` with culture-invariant matching, so patterns do not need constructs such as `[hH]`.
- **Use regular expression** is separated as the final advanced option and interprets the search text as a .NET regular expression. For example, `hot ?reload` matches both `HotReload` and `hot reload` when **Case sensitive** is off.

The selected options are captured when Search starts, so changing them never changes a search already in progress. Cached session documents contain raw conversation text and remain reusable across option combinations.

Regular expressions are compiled once per search rather than added to the document cache. Invalid expressions are rejected before session loading begins. Matching uses cancellation checks and a finite timeout; expressions that take too long stop the search with an actionable error. Zero-length regular-expression matches are ignored because there is no text span to show or highlight.

## Status bar

The bottom status bar keeps secondary information out of the primary search row. It contains:

- Current search state and completed-session progress.
- A determinate progress indicator while searching.
- The total number of matched sessions.
- A compact icon-only theme button. Its menu shows monitor **System**, sun **Light**, and crescent **Dark** choices with text and checkmarks.

The detail window has its own narrow status bar for keyboard guidance and transient copy confirmations, keeping instructions out of the session metadata.

## Application icon

The icon is an original assistant-bubble and magnifying-glass design stored as a ten-frame `.ico` for 16 through 256 pixel display sizes. The executable and both windows use `Assets\CopilotSessionSearch.ico`.

Regenerate the icon and PNG preview with PowerShell 7:

```powershell
Set-Location Q:\ws\CopilotSessionSearch
pwsh -File tools\Generate-AppIcon.ps1
```

## Copilot SDK usage

Stable SDK APIs provide session listing and metadata. Persisted history is read through `CopilotClient.Rpc.Sessions.ReadPersistedEventsAsync`, which is public and strongly typed but currently marked experimental by the SDK. Its `GHCP001` warning suppression is intentionally limited to `CopilotSdkSessionHistorySource`.

The application never sends a prompt, resumes a session inside the SDK, deletes a session, or writes conversation data. The **Resume in console** action starts:

```text
copilot --resume=<session-id>
```

Direct SQLite access is not currently used. It remains a possible fallback if the SDK no longer exposes the required local-history data.
