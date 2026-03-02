# Phase 7 Extra Steps — Post-Implementation Fixes

This document records the additional changes made after the initial Phase 7 implementation was completed.

---

## Extra Step 1 — Gitignore Angular Build Artefacts from wwwroot

**Problem:** The Angular production build writes compiled files (`index.html`, `main-*.js`, `styles-*.css`, etc.) directly into `src/ClaudeCodeProxy/wwwroot/`. These are generated artefacts and must not be committed to the repository.

**Fix:** Added a `.gitignore` inside `wwwroot/` that ignores every file except itself and the `.gitkeep` placeholder:

Created `src/ClaudeCodeProxy/wwwroot/.gitignore`:
```
# This directory is populated by `npm run build` from src/ClaudeCodeProxyAngular/.
# Build artefacts must not be committed — they are generated on demand.
*
!.gitignore
!.gitkeep
```

Also confirmed `src/ClaudeCodeProxy/wwwroot/.gitkeep` exists (it was already tracked from a prior commit) so that the empty directory is retained in git when no build has been run.

**Verified** with `git check-ignore -v` that all Angular build outputs (`index.html`, `main-*.js`, `styles-*.css`, etc.) are matched by the `*` rule, and that `.gitkeep` is not ignored.

---

## Extra Step 2 — Pin Angular Dev Server to 127.0.0.1

**Problem:** When running inside a VSCode devcontainer, the Angular dev server needs to bind to `127.0.0.1` explicitly so that VSCode's port forwarding works correctly.

**Fix:** Updated the `start` script in `src/ClaudeCodeProxyAngular/package.json`:
```json
"start": "ng serve --host 127.0.0.1"
```

Updated `CLAUDE.md` to reflect the correct browser URL:
```
Then open `http://127.0.0.1:4200` in your browser.
```

Updated the CORS policy in `src/ClaudeCodeProxy/Program.cs` to allow both origins, since the browser may send either `localhost` or `127.0.0.1` as the request origin depending on how the devcontainer port is forwarded:
```csharp
policy.WithOrigins("http://localhost:4200", "http://127.0.0.1:4200")
```

---

## Extra Step 3 — Fix Angular Dev Proxy Target Port

**Problem:** `proxy.conf.json` was pointing at port `5000`, but the .NET backend is actually configured to listen on port `5051` (as defined in `src/ClaudeCodeProxy/Properties/launchSettings.json`). This caused all `/api/*` requests from the Angular dev server to fail to reach the backend.

**Fix:** Updated `src/ClaudeCodeProxyAngular/proxy.conf.json`:
```json
{
  "/api": {
    "target": "http://localhost:5051",
    "secure": false,
    "changeOrigin": true
  }
}
```

Also updated the matching comment in `CLAUDE.md`:
```
# Terminal 2 — Angular dev server (proxies /api/* to http://localhost:5051)
```

---

## Extra Step 4 — Add Jest Unit Tests to the Angular App

**Motivation:** The Angular project was scaffolded with Vitest as the test runner (via `@angular/build:unit-test`). Jest was chosen instead because it is more widely used, has better ecosystem support (in particular `jest-preset-angular` and `@testing-library/angular`), and the Testing Library approach encourages tests that reflect real user interactions rather than implementation details.

**Packages installed:**
```bash
npm install --save-dev jest @types/jest jest-environment-jsdom jest-preset-angular \
  @testing-library/angular @testing-library/jest-dom zone.js
```

**Configuration files created:**

`jest.config.ts` — minimal Jest config using the `jest-preset-angular` preset:
```typescript
import type { Config } from 'jest';

const config: Config = {
  preset: 'jest-preset-angular',
  setupFilesAfterEnv: ['<rootDir>/setup-jest.ts'],
};

export default config;
```

`setup-jest.ts` — initialises the Angular Zone.js test environment and loads `@testing-library/jest-dom` custom matchers:
```typescript
import { setupZoneTestEnv } from 'jest-preset-angular/setup-env/zone';
import '@testing-library/jest-dom';

setupZoneTestEnv();
```

`src/test-setup.ts` — imported by `tsconfig.spec.json` so that the `@testing-library/jest-dom` type augmentations (e.g. `toBeInTheDocument`) are visible to TypeScript across all spec files without needing per-file imports:
```typescript
import '@testing-library/jest-dom';
```

**`tsconfig.spec.json` changes:**
- Replaced `"vitest/globals"` in `types` with `"jest"`.
- Added `"esModuleInterop": true` to suppress a ts-jest interop warning.
- Added `"src/test-setup.ts"` to `include` so the jest-dom type augmentation is compiled.
- Removed the `"module": "CommonJS"` override that had been added temporarily (it broke Angular's bundler module resolution).

**`package.json` changes:**
- `"test"` script changed from `ng test` to `jest` so `npm test` runs Jest.
- Previous Vitest runner preserved as `"test:vitest": "ng test"`.

**Tests written:**

*`src/app/services/stats.service.spec.ts`* — 10 tests using `TestBed` + `HttpTestingController`:
- `getHourly()`: no params, `from` only, `to` only, both params, response data forwarded correctly.
- `getDaily()`: same four cases plus response data assertion.

*`src/app/components/daily-stats/daily-stats.spec.ts`* — 11 tests using `@testing-library/angular`:
- Loading state shown while observable is pending (via `Subject`).
- Loading state hidden after data arrives.
- Table headers rendered.
- Bucket row data rendered (date, request count, LLM calls, token counts).
- Multiple bucket rows rendered.
- Chart section and chart bar rendered.
- Chart bars scaled relative to the highest request count.
- Error message shown when service throws.
- Empty state shown for an empty response.
- `getDaily` called with a 30-day date range.

*`src/app/components/hourly-stats/hourly-stats.spec.ts`* — 8 tests using `@testing-library/angular`:
- Same loading / data / error / empty states as above.
- Table headers rendered.
- Multiple bucket rows rendered.
- `getHourly` called with a 24-hour date range.

*`src/app/app.spec.ts`* — 4 tests (rewritten from the stale scaffold):
- App creates successfully.
- Brand name rendered.
- Footer text rendered.
- Both stats section headings rendered.
- `StatsService` is mocked via a provider so child components do not make real HTTP calls.

**Notes on Jest 30 + Angular 21 type setup:**
- `@types/jest` v30 declares `jest` as a *namespace* only (no `declare var jest`). To call `jest.fn()` / `jest.clearAllMocks()` without a TypeScript error, component spec files import `{ jest }` from `'@jest/globals'`.
- `@testing-library/jest-dom`'s default type export augments `jest.Matchers` (from `@types/jest`). This augmentation only takes effect when the import is compiled as part of the TypeScript project — hence `src/test-setup.ts` being included in `tsconfig.spec.json`.

---

## Extra Step 5 — Move StatsService into a `services/` Directory

**Motivation:** Separating services from components is standard Angular project structure and makes each layer of the app easier to navigate.

**Change:** Moved `stats.service.ts` and its co-located `stats.service.spec.ts` from `src/app/` into a new `src/app/services/` subdirectory.

**Import paths updated:**

| File | Old import | New import |
|---|---|---|
| `src/app/app.spec.ts` | `./stats.service` | `./services/stats.service` |
| `src/app/components/daily-stats/daily-stats.ts` | `../stats.service` | `../services/stats.service` |
| `src/app/components/daily-stats/daily-stats.spec.ts` | `../stats.service` | `../services/stats.service` |
| `src/app/components/hourly-stats/hourly-stats.ts` | `../stats.service` | `../services/stats.service` |
| `src/app/components/hourly-stats/hourly-stats.spec.ts` | `../stats.service` | `../services/stats.service` |

The service spec's own `./stats.service` import needed no change as both files moved together into the same directory.

---

## Extra Step 6 — Move Dashboard Components into a `components/` Directory

**Motivation:** Grouping components under `src/app/components/` keeps the flat `src/app/` directory tidy and makes the distinction between the root application shell (`app.ts`) and the individual feature components clear.

**Change:** Moved `src/app/daily-stats/` and `src/app/hourly-stats/` into a new `src/app/components/` subdirectory.

**Import paths updated:**

| File | Old import | New import |
|---|---|---|
| `src/app/app.ts` | `./hourly-stats/hourly-stats` | `./components/hourly-stats/hourly-stats` |
| `src/app/app.ts` | `./daily-stats/daily-stats` | `./components/daily-stats/daily-stats` |
| `src/app/components/daily-stats/daily-stats.ts` | `../services/stats.service` | `../../services/stats.service` |
| `src/app/components/daily-stats/daily-stats.spec.ts` | `../services/stats.service` | `../../services/stats.service` |
| `src/app/components/hourly-stats/hourly-stats.ts` | `../services/stats.service` | `../../services/stats.service` |
| `src/app/components/hourly-stats/hourly-stats.spec.ts` | `../services/stats.service` | `../../services/stats.service` |

Intra-component imports (`./daily-stats`, `./hourly-stats`, `./daily-stats.html`, etc.) were unaffected as they are relative to the component's own directory and moved with it.

**Final `src/app/` structure after both moves:**
```
src/app/
  components/
    daily-stats/
    hourly-stats/
  services/
    stats.service.ts
    stats.service.spec.ts
  app.ts
  app.html
  app.scss
  app.spec.ts
  app.config.ts
```

---

## Files Modified

| File | Change |
|---|---|
| `src/ClaudeCodeProxy/wwwroot/.gitignore` | **Created** — ignores all Angular build artefacts |
| `src/ClaudeCodeProxyAngular/package.json` | `start` script: added `--host 127.0.0.1`; `test` script: switched to `jest`; added `test:vitest` |
| `src/ClaudeCodeProxy/Program.cs` | CORS origins: added `http://127.0.0.1:4200` alongside `http://localhost:4200` |
| `src/ClaudeCodeProxyAngular/proxy.conf.json` | Dev proxy target: `5000` → `5051` |
| `CLAUDE.md` | Updated browser URL and proxy comment to reflect correct ports and host |
| `src/ClaudeCodeProxyAngular/jest.config.ts` | **Created** — Jest configuration |
| `src/ClaudeCodeProxyAngular/setup-jest.ts` | **Created** — Angular Zone.js test env init + jest-dom |
| `src/ClaudeCodeProxyAngular/tsconfig.spec.json` | Replaced `vitest/globals` with `jest`; added `esModuleInterop`, `src/test-setup.ts` |
| `src/ClaudeCodeProxyAngular/src/test-setup.ts` | **Created** — jest-dom type augmentation anchor |
| `src/ClaudeCodeProxyAngular/src/app/services/stats.service.ts` | **Moved** from `src/app/` |
| `src/ClaudeCodeProxyAngular/src/app/services/stats.service.spec.ts` | **Created** (new test file, co-located with service) |
| `src/ClaudeCodeProxyAngular/src/app/components/daily-stats/` | **Moved** from `src/app/daily-stats/`; import paths updated |
| `src/ClaudeCodeProxyAngular/src/app/components/daily-stats/daily-stats.spec.ts` | **Created** (new test file) |
| `src/ClaudeCodeProxyAngular/src/app/components/hourly-stats/` | **Moved** from `src/app/hourly-stats/`; import paths updated |
| `src/ClaudeCodeProxyAngular/src/app/components/hourly-stats/hourly-stats.spec.ts` | **Created** (new test file) |
| `src/ClaudeCodeProxyAngular/src/app/app.ts` | Import paths updated for moved components |
| `src/ClaudeCodeProxyAngular/src/app/app.spec.ts` | Rewritten — reflects real template; mocks `StatsService` |
