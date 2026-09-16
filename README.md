# Copilot Session Search

A small Windows application for searching local GitHub Copilot CLI conversation history.

## Features

- Searches visible user and final Copilot messages using case-insensitive literal text matching.
- Reads session metadata and persisted events through the public `GitHub.Copilot.SDK`.
- Processes up to four sessions concurrently and adds matching sessions to the result list as they complete.
- Keeps results ordered by last activity, newest first.
- Shows up to three excerpts for each session in the main window.
- Opens modeless detail windows containing every matching message rendered as Markdown.
- Supports selecting and copying rendered text, plus a one-click copy of each full message.
- Cancels an active search without removing results that have already been found.
- Opens a new Windows Terminal or PowerShell window and resumes a selected session.
- Supports System, Light, and Dark Fluent themes.

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

Detail messages are rendered through `MdXaml` as selectable WPF `FlowDocument` content. Headings, tables, lists, links, inline code, and fenced code blocks receive Markdown formatting. Select text and press Ctrl+C, use the document context menu, or choose **Copy full message**.

Markdown image syntax is converted into an ordinary link before rendering, and raw HTML image elements are escaped. This prevents the viewer from automatically fetching remote or local image content.

## Keyboard and mouse

- `Ctrl+F` focuses and selects the search text.
- `Enter` in the search box starts a search.
- `Alt+S` starts a search, `Alt+C` cancels an active search, and `Escape` also cancels.
- In either result list, `Up`, `Down`, `Page Up`, `Page Down`, `Home`, and `End` change selection and bring the selected item into view.
- `Enter` on a session opens its detail window.
- `Enter` on a detail message focuses its rendered Markdown; `Escape` returns focus to the message list.
- `Ctrl+C` on a selected detail message copies the full original Markdown. When rendered text has focus, `Ctrl+C` copies the selected text instead.
- In the detail window, `Alt+C` copies the selected message, `Alt+R` resumes the session, and `Alt+O` closes the window.
- The mouse wheel over rendered Markdown scrolls the message list. Long messages keep their own scrollbar for direct scrolling.

## Theme

The theme selector defaults to **System** each time the application starts. System mode follows the current Windows app theme and updates automatically when Windows changes between light, dark, or high-contrast modes.

Selecting **Light** or **Dark** applies that Fluent theme immediately to the main window, open detail windows, controls, scrollbars, selection visuals, and rendered Markdown text. Select **System** again to resume following Windows. `Alt+T` focuses the theme selector.

Theme support uses the built-in .NET 10 WPF `Application.ThemeMode` API. This API is currently marked experimental by WPF, and its `WPF0001` warning suppression is isolated to `ThemeService`.

MdXaml emits some fixed light-theme colors, so `SelectableMarkdownViewer` normalizes headings, tables, code backgrounds, links, and other text elements against the active WPF foreground. This avoids white table rows and black headings when the application is dark.

## Copilot SDK usage

Stable SDK APIs provide session listing and metadata. Persisted history is read through `CopilotClient.Rpc.Sessions.ReadPersistedEventsAsync`, which is public and strongly typed but currently marked experimental by the SDK. Its `GHCP001` warning suppression is intentionally limited to `CopilotSdkSessionHistorySource`.

The application never sends a prompt, resumes a session inside the SDK, deletes a session, or writes conversation data. The **Resume in console** action starts:

```text
copilot --resume=<session-id>
```

Direct SQLite access is not currently used. It remains a possible fallback if the SDK no longer exposes the required local-history data.
