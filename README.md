# Claude Code Proxy using .Net

This project is both a working reverse-proxy for use with Claude Code, so that prompts can be sniffed and inspected and also an example of building an application from scratch using Claude Code.  It is built with .Net 10.

- See [PromptsToCreateApplication.md](./docs/PromptsToCreateApplication.md) for the starting point and prompts used to build the application.
- This references an [InitialRequirements.md](./docs/InitialRequirements.md) document that was the initial specification for the application.  Claude then built and executed an implementation plan from this, plus a small amount of corrective prompting, which is all documented.
- There are then instructions on how to use the proxy in [HowToProxyClaudeCode.md](./docs/HowToProxyClaudeCode.md)

The project also includes VS Code devcontainer and debugging configuration to allow this to be developed in isolation inside a .Net 10 container.  How this is configured (including how to use dev-certs for HTTPS support is covered in [SettingUpVsCodeDevcontainers.md](./docs/SettingUpVsCodeDevcontainers.md))

## Build and run instructions

The application requires .Net 10 and Node.js 20 or later to build and run.  The steps required to build and run the application are:

1. Build the Proxy (from the project's root directory) with:

```bash
dotnet build
```

2. Build the Angular dashboard App from the project with:

```bash
cd src/ClaudeCodeProxyAngular
npm install
npm run build
```

3. Start up the Proxy and dashboard app with: 

```bash
dotnet run --project src/ClaudeCodeProxy --launch-profile https
```

4. Set up Claude Code to reference the proxy using instructions from [./docs/HowToProxyClaudeCode.md](./docs/HowToProxyClaudeCode.md)

5. Navigate to https://localhost:7257/ to see the dashboard for the proxy.

See the [CLAUDE.md](./CLAUDE.md) for more details.