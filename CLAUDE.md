# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build SitTimer.slnx

# Run all tests
dotnet test SitTimer.slnx --configuration Release

# Run a single test
dotnet test tests/SitTimer.Tests/SitTimer.Tests.csproj --filter "Name=<TestMethodName>" --configuration Release

# Publish single self-contained exe
dotnet publish src/SitTimer.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## Architecture

Three projects:

- **SitTimer.Core** (`net9.0`) — models, EF Core DbContext, all business logic services. No WPF dependency.
- **SitTimer.App** (`net9.0-windows`) — WPF tray app; consumes Core via DI. Handles Windows events, UI, notifications.
- **SitTimer.Tests** (`net9.0`) — NUnit 4 tests for Core only; uses in-memory SQLite.

### Session lifecycle

`SessionMonitor` listens to Windows `SessionSwitch` and `PowerModeChanged` events and calls `SessionService` accordingly:

- **Lock / sleep** → `StopActiveSessionAsync()` sets `EndTime`
- **Unlock / resume** → `StartSessionAsync()` creates a new session, or resumes the previous one if the gap was < 2 minutes
- A 60-second heartbeat timer updates `LastHeartbeat` on the active session for crash recovery

On startup, `App.xaml.cs` calls `RecoverOrphanedSessionAsync()` to close any session with `EndTime == null` using its last `LastHeartbeat`.

### Break time semantics

`BreakTime` is stored on the session that *preceded* the gap, not the one that follows. Overnight breaks are nullified via `FixCrossDayBreaksAsync()` to prevent false multi-hour breaks spanning midnight.

### Dashboard

`DashboardViewModel` drives the dashboard. The day-view table uses three `SessionDisplayItem` subtypes (`StandaloneSessionItem`, `MergeGroupHeaderItem`, `MergeGroupChildItem`) selected via `SessionDisplayTemplateSelector`. Charts use LiveChartsCore/SkiaSharp.

### Data storage

- Database: `%APPDATA%\SitTimer\sittimer.db` (SQLite via EF Core)
- Settings: `%APPDATA%\SitTimer\settings.json`
- All timestamps stored as UTC; converted to local time only for display and queries

### DI setup

`App.xaml.cs` builds the container manually using `ServiceCollection` (no generic host). Uses `IDbContextFactory<SitTimerDbContext>` for thread-safe per-operation context creation.

### Testing pattern

Each test gets an isolated in-memory SQLite database via a long-lived `SqliteConnection("DataSource=:memory:")`. A `TestDbContextFactory` wraps the options and is passed to `SessionService`.
