# Post-Phase 3 Code Review Steps

This document records every step taken to address the code review comments raised after Phase 3 was completed. The review comments are tracked in `docs/plans/CodeReviewsAfterPhase3.md`.

---

## Overview

After Phase 3 a code review was performed and seven improvement tasks were identified. They cover refactoring, the repository pattern, testing, documentation organisation, project layout, and configuration cleanup.

---

## Step 1 — Refactored `ProxyMiddleware.InvokeAsync` into Private Methods

**File modified:** `src/ClaudeCodeProxy/Middleware/ProxyMiddleware.cs`

The single large `InvokeAsync` method (which already had six labelled steps in comments) was broken into six focused private methods, one per step. `InvokeAsync` now acts as a clean orchestrator.

| Method | Responsibility |
|---|---|
| `BufferRequestBodyAsync` | Reads the request body into a `MemoryStream`; returns serialised request headers JSON |
| `BuildUpstreamRequest` | Constructs the `HttpRequestMessage` for the upstream, copying method, URI, body, and headers |
| `SendToUpstreamAsync` | Sends the request; handles timeout/connection errors; returns `null` on failure after writing the error response |
| `CopyResponseHeaders` | Copies status code and all non-hop-by-hop headers to the client response; returns serialised response headers JSON |
| `WriteResponseBodyAsync` | Streams or buffers the upstream response body to the client while accumulating a copy; returns `false` if the client disconnects |
| `RecordRequestAsync` | Builds the `ProxyRequest` record from all captured data and hands it to `RecordingService` |

---

## Step 2 — Added Repository Pattern for EF Core

**Files created:**
- `src/ClaudeCodeProxy/Data/IRecordingRepository.cs`
- `src/ClaudeCodeProxy/Data/RecordingRepository.cs`

**Files modified:**
- `src/ClaudeCodeProxy/Services/RecordingService.cs`
- `src/ClaudeCodeProxy/Program.cs`

`IRecordingRepository` defines a single `AddAsync(ProxyRequest, CancellationToken)` method. `RecordingRepository` is the EF Core implementation, wrapping `ProxyDbContext`. This decouples the recording logic from EF Core and makes the persistence layer independently testable.

`RecordingService` was updated to resolve `IRecordingRepository` from the DI scope instead of accessing `ProxyDbContext` directly.

`IRecordingRepository` is registered as scoped in `Program.cs` (matching `ProxyDbContext`'s lifetime):
```csharp
builder.Services.AddScoped<IRecordingRepository, RecordingRepository>();
```

---

## Step 3 — Added Tests for `RecordingService`

**Files created:**
- `test/ClaudeCodeProxy.Tests/RecordingServiceTests.cs`
- `src/ClaudeCodeProxy/AssemblyInfo.cs`

**Files modified:**
- `src/ClaudeCodeProxy/Services/RecordingService.cs`
- `test/ClaudeCodeProxy.Tests/ClaudeCodeProxy.Tests.csproj`

To make the service testable without racing against a background `Task.Run`, the recording logic was extracted into an `internal Task RecordCoreAsync(ProxyRequest)` method. `Record()` continues to call it via `Task.Run` in production; tests call `RecordCoreAsync` directly and await it.

`[assembly: InternalsVisibleTo("ClaudeCodeProxy.Tests")]` was added in `AssemblyInfo.cs` to give the test project access to the internal method.

The test project gained three new package references:
- `Microsoft.EntityFrameworkCore.Sqlite`
- `Microsoft.Extensions.DependencyInjection`
- `Microsoft.Extensions.Logging.Abstractions`

`RecordingServiceTests` uses a real in-memory SQLite database. A single `SqliteConnection` is kept open for the lifetime of each test so that all DI scopes share the same underlying database. The test verifies that `RecordCoreAsync` persists a `ProxyRequest` with the correct field values.

---

## Step 4 — Moved Plan Documents into `docs/plans/`

**Directory created:** `docs/plans/`

**Files moved into `docs/plans/`:**
- `ImplementationPlan-v1.md`
- `Phase1Steps.md`
- `Phase2Steps.md`
- `Phase2ExtraSteps.md`
- `Phase3Steps.md`
- `CodeReviewsAfterPhase3.md`

**File modified:** `docs/PromptsToCreateApplication.md`

All markdown links in `PromptsToCreateApplication.md` that pointed to the moved files were updated from `./Foo.md` to `./plans/Foo.md`. Plain-text occurrences inside literal prompt code blocks were left unchanged (they are a historical record).

---

## Step 5 — Moved Projects into `src/` and `test/` Directories

**Directories created:** `src/`, `test/`

**Projects moved:**
- `ClaudeCodeProxy/` → `src/ClaudeCodeProxy/`
- `ClaudeCodeProxy.Tests/` → `test/ClaudeCodeProxy.Tests/`

**Files modified:**
- `ClaudeCodeProxyDotNet.slnx` — both `<Project Path>` entries updated
- `test/ClaudeCodeProxy.Tests/ClaudeCodeProxy.Tests.csproj` — `ProjectReference` path updated from `../ClaudeCodeProxy/…` to `../../src/ClaudeCodeProxy/…`
- `CLAUDE.md` — structure diagram and all `--project` path examples updated

---

## Step 6 — Removed `ANTHROPIC_BASE_URL` from Proxy Configuration

**Files modified:**
- `src/ClaudeCodeProxy/Program.cs`
- `CLAUDE.md`

`ANTHROPIC_BASE_URL` is the environment variable that users set to point Claude Code *at* this proxy — it is not a valid way to configure the proxy's own upstream target. The three-line block in `Program.cs` that read this env var and injected it into `Upstream:BaseUrl` was removed. The startup error message was updated to reference only `appsettings.json`.

The Configuration section in `CLAUDE.md` was simplified accordingly.

References to `ANTHROPIC_BASE_URL` in `docs/HowToProxyClaudeCode.md`, `docs/InitialRequirements.md`, and `docs/test-cert.js` were intentionally left unchanged — they correctly describe using the variable to point Claude Code at the proxy.

---

## Summary of Files Changed

| File | Change |
|---|---|
| `src/ClaudeCodeProxy/Middleware/ProxyMiddleware.cs` | Refactored `InvokeAsync` into 6 private methods |
| `src/ClaudeCodeProxy/Data/IRecordingRepository.cs` | **Created** — repository interface |
| `src/ClaudeCodeProxy/Data/RecordingRepository.cs` | **Created** — EF Core implementation |
| `src/ClaudeCodeProxy/Services/RecordingService.cs` | Uses repository; exposes `internal RecordCoreAsync` |
| `src/ClaudeCodeProxy/AssemblyInfo.cs` | **Created** — `InternalsVisibleTo` for test project |
| `src/ClaudeCodeProxy/Program.cs` | Registers repository; removed `ANTHROPIC_BASE_URL` handling |
| `test/ClaudeCodeProxy.Tests/RecordingServiceTests.cs` | **Created** — in-memory SQLite test |
| `test/ClaudeCodeProxy.Tests/ClaudeCodeProxy.Tests.csproj` | Added EF/DI/Logging packages; updated project reference path |
| `ClaudeCodeProxyDotNet.slnx` | Updated project paths to `src/` and `test/` |
| `CLAUDE.md` | Updated structure, run commands, configuration section |
| `docs/PromptsToCreateApplication.md` | Updated links to moved plan files |
| `docs/plans/` | **Created** — plan/phase/review documents moved here |
