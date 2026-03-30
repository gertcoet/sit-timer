# SitTimer — App Specification

## Overview
A passive Windows desktop app that tracks time spent sitting at the desk.
It lives in the system tray, starts/stops automatically on PC lock/unlock,
and shows a dashboard with per-session and aggregated statistics.

---

## Tech Stack

| Item | Choice |
|---|---|
| Framework | WPF on .NET 9 |
| Language | C# |
| ORM | Entity Framework Core (SQLite provider) |
| Charts | LiveChartsCore.SkiaSharpView.WPF |
| Notifications | Windows Toast (Microsoft.Toolkit.Uwp.Notifications or WinRT interop) |
| Distribution | Self-contained EXE (single folder publish) |

---

## Project Structure

```
SitTimer.sln
├── SitTimer.Core/          # Class library — models, EF Core, session logic
│   ├── Models/
│   │   └── Session.cs
│   ├── Data/
│   │   └── SitTimerDbContext.cs
│   └── Services/
│       └── SessionService.cs
└── SitTimer.App/           # WPF app — tray, dashboard, notifications
    ├── App.xaml / App.xaml.cs
    ├── TrayIcon/
    │   └── TrayManager.cs
    ├── Notifications/
    │   └── NotificationService.cs
    ├── Views/
    │   ├── DashboardWindow.xaml
    │   └── DashboardWindow.xaml.cs
    └── ViewModels/
        ├── DashboardViewModel.cs
        └── SessionRowViewModel.cs
```

---

## Data Model

**Table: Sessions**

| Column | Type | Notes |
|---|---|---|
| Id | int (PK, autoincrement) | |
| StartTime | datetime | UTC |
| EndTime | datetime? | NULL = session is active |
| LastHeartbeat | datetime | Updated every 60s while session is active |

Duration is computed: `EndTime - StartTime` (or `now - StartTime` for active).

---

## Core Behaviour

### Session Start
Triggered by:
- App launch (if PC is already unlocked)
- `SystemEvents.SessionSwitch` → `SessionSwitchReason.SessionUnlock`
- `SystemEvents.PowerModeChanged` → resume from sleep/hibernate

Action:
1. Insert new `Session` row with `StartTime = UtcNow`, `EndTime = null`
2. Start 60s heartbeat timer → update `LastHeartbeat` on active session
3. Show toast: `"Sit timer started at {HH:mm}"`

### Session Stop
Triggered by:
- `SystemEvents.SessionSwitch` → `SessionSwitchReason.SessionLock`
- `SystemEvents.PowerModeChanged` → suspend (sleep/hibernate)
- App shutdown

Action:
1. Set `EndTime = UtcNow` on active session
2. Stop heartbeat timer
3. Show toast: `"Sit timer stopped. Session: {Xh Ym}"`

### Crash Recovery (on app startup)
1. Query for any session where `EndTime IS NULL`
2. If found → set `EndTime = LastHeartbeat` (last known alive timestamp)
3. Proceed with normal startup

### Auto-Start with Windows
- Register `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → path to EXE

---

## System Tray

- **Icon**: always visible in tray
- **Tooltip**: shows today's total sitting time (updates live)
- **Single-click**: opens Dashboard window
- **Right-click context menu**:
  - Manual Start (disabled if session active)
  - Manual Stop (disabled if no active session)
  - ─────────────
  - Quit

---

## Dashboard Window

### Default view: Today
- Header: date + total time today (e.g. `Monday 30 March — 3h 45m`)
- Session list table:
  | Start | End | Duration |
  |---|---|---|
  | 09:00 | 12:30 | 3h 30m |
  | 13:00 | — | 15m (active) |
- Actions per row: Delete session

### Navigation tabs / toggle
- **Day** (default — today, navigable by date)
- **Week** (Mon–Sun, bar chart per day)
- **Month** (bar chart per day in month)
- **Year** (bar chart per month)

### Charts (LiveCharts2)
- Bar chart: X = time unit, Y = total hours
- Tooltip on hover showing exact value

---

## Notifications

| Event | Message |
|---|---|
| Session started | `Sit timer started at {HH:mm}` |
| Session stopped | `Sit timer stopped. Session: {Xh Ym}` |

Implementation: Windows Toast via `Microsoft.Windows.SDK.BuildTools` or
`Microsoft.Toolkit.Uwp.Notifications`. Requires app to have a shortcut in
Start Menu with AUMID, OR use the community workaround via `DesktopNotificationManagerCompat`.

---

## Build & Publish

```bash
dotnet publish SitTimer.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: single EXE in `publish/` folder.

---

## Out of Scope (can add later)
- Idle detection (no mouse/keyboard activity)
- Multi-machine support
- Export to CSV
- Goals / alerts ("you've been sitting 2h, take a break")
