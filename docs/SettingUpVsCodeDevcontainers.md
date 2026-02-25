# Setting up this project to use VS Code Devcontainers

These instructions are for developers setting up this project rather than for processing by any AI coding agents.  This will help people unfamiliar with using devcontainers in VS Code get up and running with this project and includes instructions on how it was set up.  For more general documentation on devcontainers see the [MS docs](https://code.visualstudio.com/docs/devcontainers/containers)

## Motivation
Dev containers provide a way for VS Code to use a well-specified but isolated environment to develop inside.  This is particularly useful when using AI coding agents (such as Claude Code) which in principle could run amok and do anything to your dev environment.

A container with the appropriate dev tools is configured and set up and then all development is run inside the container.  However, VS Code runs on the host machine and connects through to the files inside the container to provide a first-class editing and debugging experience.

## Setup

To create a new devcontainer for an already existing project.  This example will be a .Net application

1. Ensure you have a container environment (such as Docker Desktop) installed on your host machine.
2. For Windows hosts using Docker Desktop, ensure that this is running in "Linux containers" mode.
3. Open VS Code on your host machine.
4. Open the project folder.
5. Press the "><" button at the bottom left of the VS Code UI and choose "Reopen in container"
6. Choose the "Add configuration to workspace" option, then "(C#) .Net" and then the default version (currently "10.0-noble").
7. You are asked to add additional features.  For example you could choose ".Net CSharpier"
8. You are asked to add a dependabot YAML file, if you want.
9. This will create a `.devcontainer/devcontainer.json` file with the above options in it and start pulling and running the appropriate container.
10. Once the container has started the "><" button in VS Code will show you are connected to a container.  If you open a terminal you will see you are running inside the isolated container, with the project mounted to `/workspaces/<projectdir>`

## Temporarily opening ports for testing

If you want to run an application that accepts incomming network connections then ports can be forwarded from the devcontainer to the host by using the "Forward a Port" command in the VS Code command palette.

Ports can also be added to the `devcontainer.json` file to permanently forward them:

```
"forwardPorts": [3000, 3001]
```

## Using Claude Code

To then use Claude Code in the devcontainer, you will need to install this inside the container:

1. Install the Claude Code VS Code extension, if you don't already have it.
2. Open a terminal in VS Code and check you are inside the devcontainer.
3. Run `curl -fsSL https://claude.ai/install.sh | bash`
4. Run `claude` to start it up and follow the authentication instructions.
5. You should then be able to quit Claude Code on the terminal and use the VS Code Extension.
