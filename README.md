# Command Dashboard

Command Dashboard is a Windows desktop tool for juggling multiple command prompts in one window. Launch as many terminals as you like, rearrange them automatically into balanced grids, and keep reusable commands at your fingertips.

## Features

- **Adaptive tiling** – automatically lays out sessions 1×1, 1×2, 1×3, 2×2, 3×3, and beyond as new terminals are opened.
- **Per-session history** – use <kbd>↑</kbd>/<kbd>↓</kbd> inside an input field to cycle through the commands you previously sent.
- **Persistent notes** – expand the Notes panel (next to the “+” button) to store reusable command snippets. Notes are saved as `.txt` files under `%APPDATA%\CmdDashboard\Notes` and reload on startup.
- **Session controls** – send input, stop, clear, copy logs, and close any tile without losing track of the rest.

## Getting started

### Prerequisites

- Windows 10/11
- [.NET SDK 8.0](https://dotnet.microsoft.com/en-us/download) or newer

### Build and run

```powershell
cd d:\Documents\programozas\CMD_Server
dotnet build CmdDashboard.sln
dotnet run --project CmdDashboard\CmdDashboard.csproj
```

## Using the app

1. Click **+** to open a terminal; the layout instantly rebalances to fill the window.
2. Type a command and press **Enter**. Use <kbd>↑</kbd>/<kbd>↓</kbd> to recall what you sent earlier in that tile.
3. Expand **Notes** to create, edit, and delete command snippets. Select one to edit its text, then **Save** to persist.
4. Drag tiles to reorder; use the inline buttons to clear output, stop the process, copy the log, or close the session.

## Keyboard shortcuts

- <kbd>Enter</kbd>: send the current command to the active terminal.
- <kbd>↑</kbd>/<kbd>↓</kbd>: navigate backward/forward through the current session’s command history.

## Notes storage

Notes are plain-text files stored per user. You can back them up or populate them manually by placing `.txt` files in `%APPDATA%\CmdDashboard\Notes` before launching the app.

## License

This project is provided without a formal license. Adapt it to your workflows as needed.

