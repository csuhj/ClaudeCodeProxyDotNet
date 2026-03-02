# Further Requirements for Claude Code Proxy

## Initial implementation
This project is still intended to work as a pass-through proxy for Claude Code, and the initial implmentation has created an application that proxies requests from Claude Code and stores them in a database to allow inspection and monitor usage.  The initial usage monitoring shows aggregate information about requests and token usage in a dashboard web app.  See [InitialRequirements.md](./InitialRequirements.md) for more details on the goal and initial requirements that have been met.

## Goal for further features
It would be good to have more detail on the requests that are being proxied so that these can be shown in the dashboard app, so that the traffic can be inspected in detail.


## Further Requirements For Version 2

7. Extend the simple Single Page App UI (and API that backs it) to be able to list all the LLM requests made in the last day.
8. In the UI, allow each request to be drilled into to show the full request body and results in a manner that is readable for a human user.
