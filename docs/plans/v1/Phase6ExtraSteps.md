# Phase 6 Extra Steps — Refactor StatsService to Use RecordingRepository

## Motivation

The initial Phase 6 implementation had `StatsService` depend directly on `ProxyDbContext`. This bypassed the repository abstraction that the rest of the codebase uses and gave the service layer direct access to the EF Core context. This extra step moves the database query into `RecordingRepository` (behind `IRecordingRepository`) so that:

- `StatsService` has no direct dependency on EF Core or `ProxyDbContext`
- The query can be independently tested at the repository level if needed
- The layering is consistent with how `RecordingService` accesses data

---

## Step 1 — Add `StatsProjection` record to the Data namespace

**File created:** `src/ClaudeCodeProxy/Data/StatsProjection.cs`

`StatsProjection` is the lightweight DTO returned by the new repository query method. It carries exactly the four fields `StatsService` needs to group and aggregate:

```csharp
public sealed record StatsProjection(
    DateTime Timestamp,
    bool HasLlm,
    int InputTokens,
    int OutputTokens);
```

It is placed in the `Data` namespace and made `public` because it appears in the signature of a `public` interface method — making it `internal` would cause a CS0051 inconsistent accessibility compiler error.

The `private sealed class RequestProjection` that was previously nested inside `StatsService` is removed entirely; `StatsProjection` replaces it.

---

## Step 2 — Extend `IRecordingRepository` with a query method

**File modified:** `src/ClaudeCodeProxy/Data/IRecordingRepository.cs`

Added one new method to the interface:

```csharp
Task<List<StatsProjection>> GetStatsProjectionsAsync(
    DateTime from, DateTime to, CancellationToken ct = default);
```

The `[from, to)` half-open interval contract (inclusive `from`, exclusive `to`) matches the behaviour already implemented in `StatsService` and documented in `IStatsService`.

---

## Step 3 — Implement the query in `RecordingRepository`

**File modified:** `src/ClaudeCodeProxy/Data/RecordingRepository.cs`

Added `using Microsoft.EntityFrameworkCore` (needed for `.ToListAsync`) and the new method:

```csharp
public async Task<List<StatsProjection>> GetStatsProjectionsAsync(
    DateTime from, DateTime to, CancellationToken ct = default)
{
    return await _db.ProxyRequests
        .Where(r => r.Timestamp >= from && r.Timestamp < to)
        .Select(r => new StatsProjection(
            r.Timestamp,
            r.LlmUsage != null,
            r.LlmUsage != null ? r.LlmUsage.InputTokens : 0,
            r.LlmUsage != null ? r.LlmUsage.OutputTokens : 0))
        .ToListAsync(ct);
}
```

The EF Core `Select` projection (with its implicit LEFT JOIN via the `LlmUsage` navigation property) moved here unchanged from the `FetchProjectedDataAsync` private helper that previously lived in `StatsService`.

---

## Step 4 — Refactor `StatsService` to inject `IRecordingRepository`

**File modified:** `src/ClaudeCodeProxy/Services/StatsService.cs`

Changes:
- Constructor parameter changed from `ProxyDbContext db` → `IRecordingRepository repository`
- `using ClaudeCodeProxy.Data` retained; `using Microsoft.EntityFrameworkCore` removed (no longer needed)
- `FetchProjectedDataAsync` private helper removed; both public methods now call `_repository.GetStatsProjectionsAsync(from, to)` directly
- `RequestProjection` private inner class removed (replaced by `StatsProjection` from the Data layer)

`StatsService` now has zero EF Core dependencies.

`Program.cs` required **no changes** — `StatsService` is already registered as scoped, and `IRecordingRepository`/`RecordingRepository` are also scoped, so the DI wiring resolves correctly.

---

## Step 5 — Update `StatsServiceTests`

**File modified:** `test/ClaudeCodeProxy.Tests/Services/StatsServiceTests.cs`

Two changes to the test class:

1. **Setup**: A `RecordingRepository` is now instantiated from the in-memory `ProxyDbContext` and passed to `StatsService`:
   ```csharp
   _repository = new RecordingRepository(_db);
   _sut = new StatsService(_repository);
   ```

2. **`SeedAsync` helper**: Changed from `_db.ProxyRequests.AddRange(...) + SaveChangesAsync()` to calling `_repository.AddAsync(request)` for each request. This exercises the actual repository write path, making the test a true integration test of both the `AddAsync` write side and the `GetStatsProjectionsAsync` read side.

No test assertions or test cases changed — the behaviour under test is identical.

---

## Verification

```
dotnet build ClaudeCodeProxyDotNet.slnx   # 0 warnings, 0 errors
dotnet test                                 # 75/75 passed
```
