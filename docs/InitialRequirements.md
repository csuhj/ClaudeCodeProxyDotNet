# Initial Requirements for Claude Code Proxy

## Goal
This project is intended to work as a pass-through proxy for Claude Code.

Claude Code can be configured to use a proxy with the `ANTHROPIC_BASE_URL` environment variable (see https://code.claude.com/docs/en/third-party-integrations#configure-proxies-and-gateways).  This proxy then takes requests made by Claude Code and can then pass them through to the Anthropic-hosted LLMs.  But the proxy can also be used to inspect this traffic and potentially record and monitor usage.

## Initial Requirements For Version 1

1. Build the proxy as a ASP.Net Core application in .Net
2. Take data sent to the proxy and pass it on to a configurable end server URL (i.e. the Anthropic LLM servers)
3. Record requests and results passed through the proxy into a SQLite database
4. If the requests are for Anthropic LLM calls from Claude, then also count the numbers of input and output tokens used for each request in the database.

## Stretch Requirements

5. Create an endpoint on the proxy to be able to return aggregated information from the SQLite database on the number of requests sent per hour/per day
6. Create a simple HTML user interface to view some of these results as well.

