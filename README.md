# SitTimer

A passive Windows desktop app that tracks time spent sitting at your desk. It lives in the system tray, starts and stops automatically on PC lock/unlock and sleep/wake, and shows a dashboard with per-session and daily statistics.

## Features

- **Automatic tracking** — sessions start on unlock/wake and stop on lock/sleep
- **System tray** — always-visible icon with today's total sitting time as tooltip
- **Dashboard** — view sessions by day, week, month, or year with bar charts
- **Sitting reminder** — configurable toast notification after N minutes of continuous sitting (default: 45 min)
- **Crash recovery** — if the app exits unexpectedly, the last session is closed at the last heartbeat timestamp
- **Auto-start with Windows** — registers itself in `HKCU\...\Run` on first launch

## Requirements

- Windows 10/11
- [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) (or use the self-contained publish)

## Building

```bash
dotnet build
```

### Publish (single EXE)

```bash
dotnet publish src/SitTimer.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output lands in `publish/`.

## Usage

Run `SitTimer.App.exe`. The app starts minimised to the tray — no window appears on launch.

- **Left-click** the tray icon to open the dashboard
- **Right-click** for manual Start / Stop and Quit

### Settings

Settings are stored in `%APPDATA%\SitTimer\settings.json`:

```json
{
  "ReminderMinutes": 45
}
```

Set `ReminderMinutes` to `0` to disable the sitting reminder.

## Data

Sessions are stored in a SQLite database at `%APPDATA%\SitTimer\sittimer.db`.

## Tech Stack

| | |
|---|---|
| Framework | WPF on .NET 9 |
| Database | SQLite via Entity Framework Core |
| Charts | LiveChartsCore (SkiaSharp) |
| Notifications | Windows Toast |
