# How to use the ClaudeCodeProxyDotNet as a proxy for Claude Code

In order to use this project as a proxy for Claude Code, you will need to run the following steps:

1. Start the proxy and ensure that it is serving on https.  This should start the proxy on port 7257:
```
dotnet run --project ClaudeCodeProxy --launch-profile https
```

2. In the environment Claude Code will be running, set the `ANTHROPIC_BASE_URL` environment variable to point at the proxy:

```
export ANTHROPIC_BASE_URL="https://localhost:7257/"
```

3. Claude Code runs using Node.JS.  By default, node won't trust custom certificate authoritiea, such as the dev-cert created by .Net to run the project as HTTPS.  Therefore you need to export this as a PEM file and pass this as an extra CA file to node.  Run these commands (which are similar to what is required to allow certs to be shared for developing this inside a devcontainer):

```
dotnet dev-certs https --export-path "${HOME}/.aspnet/https/aspnetapp.pem" --format PEM --no-password
export NODE_EXTRA_CA_CERTS=${HOME}/.aspnet/https/aspnetapp.pem
```
Note: If your host machine is a Windows machine, but you're running these commands in git-bash then you might want to explicitly expand the HOME environment variable to something more appropriate.  You will also need to set the environment variable to use a Windows path in this case (i.e. `C:\Users\username\.aspnet\https\aspnetapp.pem`).

4. Start Claude Code.  when it connects to the Anthropic API it will now send results through the proxy and record the results in the database.

5. Optional: If setting the CA cert doesn't work (and Claude returns and error like "Unable to connect to API: SSL certificate validation failed...") then you can temporarily disable certificate validation checks in node by setting the following environment variable.  WARNING: This will allow invalid certificates

```
export NODE_TLS_REJECT_UNAUTHORIZED=0
```