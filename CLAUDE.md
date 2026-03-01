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
    wwwroot/                # Angular SPA build output (served as static files)
  ClaudeCodeProxyAngular/   # Angular 21 SPA dashboard
    src/app/                # Components, service, styles
    proxy.conf.json         # Dev-server API proxy to .NET backend
    angular.json            # Build config (output → ../ClaudeCodeProxy/wwwroot)
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

## Angular SPA Dashboard

The dashboard is an Angular 21 standalone application in `src/ClaudeCodeProxyAngular/`.

### Requirements
- Node.js 20+ and npm 10+

### Development (hot-reload, API proxied to .NET)
Run both servers in separate terminals:
```bash
# Terminal 1 — .NET backend
dotnet run --project src/ClaudeCodeProxy

# Terminal 2 — Angular dev server (proxies /api/* to http://localhost:5000)
cd src/ClaudeCodeProxyAngular
npm start
```
Then open `http://localhost:4200` in your browser.

### Build SPA into wwwroot (production)
```bash
cd src/ClaudeCodeProxyAngular
npm run build
```
This outputs the compiled SPA directly into `src/ClaudeCodeProxy/wwwroot/`. After building, start the .NET app and open `http://localhost:5000` to see the dashboard served from the same origin.

## Configuration

The upstream base URL is configured via `Upstream:BaseUrl` in `appsettings.json`.

The SQLite database path is configured under `ConnectionStrings:DefaultConnection` in `appsettings.json`.
