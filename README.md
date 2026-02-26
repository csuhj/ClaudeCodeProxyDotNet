# Claude Code Proxy using .Net

This project is both a working reverse-proxy for use with Claude Code, so that prompts can be sniffed and inspected and also an example of building an application from scratch using Claude Code.  It is built with .Net 10.

- See [PromptsToCreateApplication.md](./docs/PromptsToCreateApplication.md) for the starting point and prompts used to build the application.
- This references an [InitialRequirements.md](./docs/InitialRequirements.md) document that was the initial specification for the application.  Claude then built and executed an implementation plan from this, plus a small amount of corrective prompting, which is all documented.
- There are then instructions on how to use the proxy in [HowToProxyClaudeCode.md](./docs/HowToProxyClaudeCode.md)

The project also includes VS Code devcontainer and debugging configuration to allow this to be developed in isolation inside a .Net 10 container.  How this is configured (including how to use dev-certs for HTTPS support is covered in [SettingUpVsCodeDevcontainers.md](./docs/SettingUpVsCodeDevcontainers.md))