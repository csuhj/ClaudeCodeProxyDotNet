# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

ClaudeCodeProxyDotNet — a .NET reverse proxy for Claude Code. It forwards all traffic to a configurable upstream URL (e.g. the Anthropic API), records every request and response to a SQLite database, and extracts LLM token usage from Anthropic Messages API calls.

## Structure

```
src/
  ClaudeCodeProxy/          # Main ASP.NET Core Web API project (net10.0)
    Controllers/            # API controllers (stats endpoints)
    Data/                   # DbContext, EF Core migrations
    Middleware/             # Proxy middleware, recording middleware
    Models/                 # Entity models and DTOs
    Services/               # Business logic (token parsing, stats queries)
    wwwroot/                # Static files for the HTML dashboard
test/
  ClaudeCodeProxy.Tests/    # NUnit test project
```

## Commands

### Build
```bash
dotnet build ClaudeCodeProxyDotNet.slnx
```

### Run
```bash
dotnet run --project src/ClaudeCodeProxy
```

### Test
```bash
dotnet test
```

### EF Core Migrations
```bash
# Add a new migration
dotnet ef migrations add <MigrationName> --project src/ClaudeCodeProxy

# Apply migrations to create/update the database
dotnet ef database update --project src/ClaudeCodeProxy
```

## Configuration

The upstream base URL is read from (in priority order):
1. `ANTHROPIC_BASE_URL` environment variable
2. `Upstream:BaseUrl` in `appsettings.json`

The SQLite database path is configured under `ConnectionStrings:DefaultConnection` in `appsettings.json`.
