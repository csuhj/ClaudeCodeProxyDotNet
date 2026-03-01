# Phase 7 Implementation Steps — Angular SPA Dashboard

This document records every concrete step taken to implement Phase 7 of the implementation plan.

---

## Task 7.1 — Scaffold the Angular Project

1. Verified Node.js (v24.14.0) and npm (11.9.0) were available in the devcontainer.
2. Angular CLI was **not** installed globally. Installed it locally to the project using `npx`:
   ```bash
   cd src
   npx --yes @angular/cli@latest new ClaudeCodeProxyAngular \
     --routing=false --style=scss --standalone --skip-git
   ```
   - `--routing=false` — no Angular router needed for this single-view SPA
   - `--style=scss` — SCSS for nesting and variables
   - `--standalone` — Angular 17+ standalone component model (no NgModules)
   - `--skip-git` — repository already initialised at root level
3. Angular CLI scaffolded the project at `src/ClaudeCodeProxyAngular/` using Angular **21.2.0**.

---

## Task 7.2 — Configure the Angular Dev Proxy and .NET CORS

### Angular dev proxy
4. Created `src/ClaudeCodeProxyAngular/proxy.conf.json`:
   ```json
   {
     "/api": {
       "target": "http://localhost:5000",
       "secure": false,
       "changeOrigin": true
     }
   }
   ```
   This forwards all `/api/*` requests from the Angular dev server (port 4200) to the .NET backend (port 5000).

5. Updated `angular.json` — added `outputPath` to the build options (Task 7.5 concern but set here to keep config coherent):
   ```json
   "outputPath": {
     "base": "../ClaudeCodeProxy/wwwroot",
     "browser": ""
   }
   ```
   The `"browser": ""` suffix overrides Angular 17+'s default of placing files in a `browser/` subdirectory, so files land directly in `wwwroot/`.

6. Updated `angular.json` — added `proxyConfig` to the `serve` architect options:
   ```json
   "serve": {
     "options": { "proxyConfig": "proxy.conf.json" },
     ...
   }
   ```

7. Updated `package.json` `build` script to explicitly use the production configuration:
   ```json
   "build": "ng build --configuration production"
   ```

### .NET CORS
8. Added a CORS policy to `src/ClaudeCodeProxy/Program.cs` so the Angular dev server can call the stats API from a different origin:
   ```csharp
   builder.Services.AddCors(options =>
   {
       options.AddPolicy("AngularDev", policy =>
           policy.WithOrigins("http://localhost:4200")
                 .AllowAnyHeader()
                 .AllowAnyMethod());
   });
   ```

9. Added `app.UseCors("AngularDev")` to the app pipeline in `Program.cs` (placed after `UseStaticFiles`, before `MapControllers`).

---

## Task 7.3 — Create the Stats API Service

10. Updated `src/ClaudeCodeProxyAngular/src/app/app.config.ts` to provide `HttpClient`:
    ```typescript
    providers: [
      provideBrowserGlobalErrorListeners(),
      provideHttpClient(),
    ]
    ```

11. Created `src/ClaudeCodeProxyAngular/src/app/stats.service.ts`:
    - Defined the `StatsBucket` TypeScript interface matching the C# `StatsBucket` model (camelCase, as serialised by ASP.NET Core's default JSON options):
      ```typescript
      export interface StatsBucket {
        timeBucket: string;
        requestCount: number;
        llmRequestCount: number;
        totalInputTokens: number;
        totalOutputTokens: number;
      }
      ```
    - Implemented `getHourly(from?, to?)` and `getDaily(from?, to?)` using `HttpClient` with `HttpParams`, returning `Observable<StatsBucket[]>`.

---

## Task 7.4 — Build the Dashboard Components

### HourlyStats component
12. Created `src/ClaudeCodeProxyAngular/src/app/hourly-stats/hourly-stats.ts`:
    - Standalone component using Angular signals (`signal`, no RxJS state management in the component).
    - On `ngOnInit`, fetches data for the last 24 hours via `StatsService.getHourly()`.
    - Exposes `data`, `loading`, and `error` signals to the template.

13. Created `src/ClaudeCodeProxyAngular/src/app/hourly-stats/hourly-stats.html`:
    - Uses Angular 17+ `@if` / `@for` block syntax (no `*ngIf` / `*ngFor`).
    - Shows a loading message, an error banner, an empty state, or a data table.
    - Table columns: Hour (UTC), Requests, LLM Calls, Input Tokens, Output Tokens.
    - Uses `DatePipe` with `'UTC'` timezone to format the `timeBucket` timestamp.

14. Created `src/ClaudeCodeProxyAngular/src/app/hourly-stats/hourly-stats.scss` (minimal placeholder; shared table styles live in `styles.scss`).

### DailyStats component
15. Created `src/ClaudeCodeProxyAngular/src/app/daily-stats/daily-stats.ts`:
    - Same signal-based pattern as `HourlyStats`.
    - Fetches 30 days of data.
    - Adds a `computed` signal `maxRequests` (max request count across all buckets) used to scale bar widths.
    - Exposes `barWidthPercent(requestCount)` helper for the chart.

16. Created `src/ClaudeCodeProxyAngular/src/app/daily-stats/daily-stats.html`:
    - Same table as hourly (day-bucketed).
    - Below the table: a CSS flexbox bar chart rendered with `[ngStyle]` — no external charting library. Each bar's width is set as a percentage of the maximum daily request count.

17. Created `src/ClaudeCodeProxyAngular/src/app/daily-stats/daily-stats.scss`:
    - Styles for `.chart`, `.chart-row`, `.chart-label`, `.chart-bar-container`, `.chart-bar`, and `.chart-value`.
    - Uses CSS custom properties (`var(--color-*)`) defined in `styles.scss`.

### Root AppComponent
18. Rewrote `src/ClaudeCodeProxyAngular/src/app/app.ts`:
    - Imports and uses `HourlyStats` and `DailyStats` standalone components.
    - Exposes `now = new Date()` for the header timestamp.

19. Rewrote `src/ClaudeCodeProxyAngular/src/app/app.html`:
    - Sticky `<header>` with proxy name and current UTC timestamp.
    - `<main>` containing `<app-hourly-stats />` and `<app-daily-stats />`.
    - `<footer>` with a short attribution line.

20. Wrote `src/ClaudeCodeProxyAngular/src/app/app.scss`:
    - Layout styles for header, main, footer using CSS custom properties.

### Global styles
21. Wrote `src/ClaudeCodeProxyAngular/src/styles.scss`:
    - CSS custom properties (design tokens) for the dark-blue colour scheme.
    - CSS reset.
    - Shared `.stats-section`, `.state-message`, `.table-wrapper`, and `table` styles used by both components.

---

## Task 7.5 — Wire Angular Build Output into wwwroot

22. `angular.json` already configured `outputPath` in step 5 to write to `../ClaudeCodeProxy/wwwroot` with no `browser/` subdirectory.

23. Updated `src/ClaudeCodeProxy/Program.cs` to serve the Angular SPA:
    ```csharp
    app.UseDefaultFiles();   // rewrites "/" to "/index.html"
    app.UseStaticFiles();    // serves files from wwwroot; short-circuits pipeline
    app.UseCors("AngularDev");
    app.MapControllers();
    app.UseMiddleware<ProxyMiddleware>();
    ```
    - `UseDefaultFiles()` must come **before** `UseStaticFiles()` to rewrite the root path before serving.
    - Both middleware run before the proxy so the Angular files are served without ever reaching the upstream forwarding logic.
    - `MapFallbackToFile` was deliberately **not** added: the proxy middleware is the terminal handler for unmatched routes and would conflict with a catch-all endpoint. Since `--routing=false` was used, Angular client-side routing is not needed.

24. Ran the Angular production build to verify output:
    ```bash
    cd src/ClaudeCodeProxyAngular && npm run build
    ```
    Files written to `src/ClaudeCodeProxy/wwwroot/`:
    ```
    index.html
    main-2VAGMBQ5.js
    styles-LV5YU6IZ.css
    favicon.ico
    3rdpartylicenses.txt
    prerendered-routes.json
    ```

25. Confirmed the .NET solution still builds cleanly:
    ```
    dotnet build ClaudeCodeProxyDotNet.slnx
    Build succeeded. 0 Warning(s), 0 Error(s)
    ```

26. Confirmed all 75 unit tests still pass:
    ```
    dotnet test
    Passed! — Failed: 0, Passed: 75, Skipped: 0
    ```

---

## Task 7.6 — Update CLAUDE.md

27. Updated the `Structure` section to include `ClaudeCodeProxyAngular/` and corrected the `wwwroot/` description from "HTML dashboard" to "Angular SPA build output".

28. Added an **Angular SPA Dashboard** section documenting:
    - Node.js/npm version requirements.
    - How to run both servers for development (hot-reload, API proxied).
    - How to build the SPA into `wwwroot/` for production use.

---

## Files Created / Modified

| File | Action |
|---|---|
| `src/ClaudeCodeProxyAngular/` | **Created** (Angular CLI scaffold) |
| `src/ClaudeCodeProxyAngular/proxy.conf.json` | **Created** |
| `src/ClaudeCodeProxyAngular/angular.json` | **Modified** — outputPath + proxyConfig |
| `src/ClaudeCodeProxyAngular/package.json` | **Modified** — build script |
| `src/ClaudeCodeProxyAngular/src/app/app.config.ts` | **Modified** — provideHttpClient |
| `src/ClaudeCodeProxyAngular/src/app/app.ts` | **Modified** — imports components |
| `src/ClaudeCodeProxyAngular/src/app/app.html` | **Modified** — dashboard layout |
| `src/ClaudeCodeProxyAngular/src/app/app.scss` | **Modified** — header/layout styles |
| `src/ClaudeCodeProxyAngular/src/styles.scss` | **Modified** — global styles + design tokens |
| `src/ClaudeCodeProxyAngular/src/app/stats.service.ts` | **Created** |
| `src/ClaudeCodeProxyAngular/src/app/hourly-stats/hourly-stats.ts` | **Created** |
| `src/ClaudeCodeProxyAngular/src/app/hourly-stats/hourly-stats.html` | **Created** |
| `src/ClaudeCodeProxyAngular/src/app/hourly-stats/hourly-stats.scss` | **Created** |
| `src/ClaudeCodeProxyAngular/src/app/daily-stats/daily-stats.ts` | **Created** |
| `src/ClaudeCodeProxyAngular/src/app/daily-stats/daily-stats.html` | **Created** |
| `src/ClaudeCodeProxyAngular/src/app/daily-stats/daily-stats.scss` | **Created** |
| `src/ClaudeCodeProxy/Program.cs` | **Modified** — CORS + UseDefaultFiles + UseStaticFiles |
| `CLAUDE.md` | **Modified** — structure + Angular workflow |
| `docs/plans/Phase7Steps.md` | **Created** (this file) |
