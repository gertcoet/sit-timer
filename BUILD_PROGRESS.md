# SitTimer — Build Progress

## Status Legend
- [ ] Pending
- [~] In Progress
- [x] Done

---

## Phase 1 — Solution Scaffold
- [x] Create SPEC.md
- [x] Create solution file (`SitTimer.sln`)
- [x] Create `SitTimer.Core` class library (.NET 9)
- [x] Create `SitTimer.App` WPF project (.NET 9-windows)
- [x] Add project references and NuGet packages
  - EF Core 9 + SQLite (Core)
  - LiveChartsCore.SkiaSharpView.WPF (App)
  - UseWindowsForms=true for NotifyIcon tray support

## Phase 2 — Core Library ✅
- [x] `Session.cs` model (Id, StartTime, EndTime, LastHeartbeat, Duration, IsActive)
- [x] `SitTimerDbContext.cs` with EF Core SQLite
- [x] DB via `EnsureCreated` on startup
- [x] `SessionService.cs`
  - [x] `StartSession()`
  - [x] `StopActiveSession()`
  - [x] `RecoverOrphanedSession()` (crash recovery via LastHeartbeat)
  - [x] `UpdateHeartbeat()`
  - [x] `GetSessionsForDay(date)`
  - [x] `GetSessionsForWeek(weekStart)`
  - [x] `GetSessionsForMonth(year, month)`
  - [x] `GetSessionsForYear(year)`
  - [x] `DeleteSession(id)`
  - [x] `TotalDuration()`, `GroupByDay()`, `GroupByMonth()` aggregation helpers

## Phase 3 — App Infrastructure ✅
- [x] `App.xaml.cs` — no main window, `ShutdownMode=OnExplicitShutdown`, DI container
- [x] `TrayManager.cs` — NotifyIcon, context menu (Manual Start/Stop, Quit), single-click → dashboard
- [x] `NotificationService.cs` — balloon tip notifications (attach to NotifyIcon)
- [x] `SessionMonitor.cs` — wires `SystemEvents.SessionSwitch` + `PowerModeChanged`
- [x] 60s heartbeat timer + 30s tooltip refresh timer
- [x] `StartupHelper.cs` — registry `Run` key auto-start registration

## Phase 4 — Dashboard ✅
- [x] `DashboardWindow.xaml` — dark theme, tab buttons (Day/Week/Month/Year), nav arrows
- [x] `DashboardViewModel.cs` — data loading, navigation, live 30s refresh
- [x] `SessionRowViewModel.cs` — per-row model with delete command + `RelayCommand`
- [x] Day view: session list table (Start / End / Duration / delete button)
- [x] Week/Month/Year views: LiveCharts2 bar chart with hour totals
- [x] BoolToVisibility converters wired in App.xaml

## Phase 5 — Polish & Publish [ ]
- [ ] App icon (tray + window) — currently using system placeholder
- [ ] Smoke test: lock → unlock cycle records correctly
- [ ] Smoke test: crash recovery works
- [ ] Smoke test: dashboard aggregations correct
- [ ] Self-contained publish

---

## Notes

**2026-03-30** — Initial scaffold complete. Build succeeds clean.
- Used `UseWindowsForms=true` alongside WPF to get `NotifyIcon` / `ContextMenuStrip` for the tray.
- Switched from `Microsoft.Toolkit.Uwp.Notifications` to `NotifyIcon.ShowBalloonTip` for notifications
  (simpler, no WinRT AUMID setup needed).
- `net9.0-windows` (Windows 7.0 baseline) is sufficient; no need for Windows 10+ API surface.
