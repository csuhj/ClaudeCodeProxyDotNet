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

## Files Modified

| File | Change |
|---|---|
| `src/ClaudeCodeProxy/wwwroot/.gitignore` | **Created** — ignores all Angular build artefacts |
| `src/ClaudeCodeProxyAngular/package.json` | `start` script: added `--host 127.0.0.1` |
| `src/ClaudeCodeProxy/Program.cs` | CORS origins: added `http://127.0.0.1:4200` alongside `http://localhost:4200` |
| `src/ClaudeCodeProxyAngular/proxy.conf.json` | Dev proxy target: `5000` → `5051` |
| `CLAUDE.md` | Updated browser URL and proxy comment to reflect correct ports and host |
