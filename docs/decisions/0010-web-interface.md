# 0010 - A small web interface next to the command line

Status: accepted (2026-11-06)

## Context
The command line is enough for the article series, but the operator wants to start a match and watch it without a
terminal, on a subdomain of their own, behind one fixed password. The first brief ruled out a web UI; this is a later,
explicit request. It must not change the arena, and nothing in it may need a database or a build step.

## Decision
- A new project `LlmShootout.Web` (ASP.NET Core minimal API, static files) references the arena and the games and
  nothing else. The command line does not depend on it.
- One match at a time, run in the background of the web process. Every protocol line goes to the browser through
  Server-Sent Events, and into the same `events.jsonl` as on the command line. The page renders the *protocol*, live or
  from a stored file, with one renderer. A late or reconnecting browser receives all lines from the start
  (`Last-Event-ID` resumes).
- No framework and no build step: plain HTML, CSS and one JavaScript file, system fonts, no external requests. Model output is only ever
  inserted as text.
- Authentication is one password from the environment and a cookie session (ASP.NET cookie authentication, keys in the data
  volume). A gate middleware in front of everything lets only the login page through; same-origin checks protect
  state-changing requests; login attempts are rate limited per client address.
- The spending limit of a request is capped by configuration. `scripted:` players are not offered.
- Secrets come from `~/.config/llm-shootout/env`, which `docker-compose.yml` reads as `env_file`. The image is built
  with `--no-cache`, runs as a non-root user, and reaches the gateway over the `proxy` network.

## Consequences
- The web process and the command line can use the same matches folder; the page lists whatever is there.
- A restart while a match runs ends it (the protocol records `cancelled`); the history survives in the volume.
- The cookie signing keys are not encrypted at rest; they protect a cookie, not data, and sit in a private volume.
- Several users or roles would need a different design; this is a one-operator tool.
