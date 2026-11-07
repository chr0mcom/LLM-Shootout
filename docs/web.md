# Web interface

A small web page to start a match and watch it. It runs next to the command line and uses the same arena, the same
protocol files and the same [endpoints](endpoints.md). There is no database and no user management: one fixed password.

```text
Browser  ──https──▶  Traefik  ──http──▶  llm-shootout-web (ASP.NET Core, port 8080)
                                              ├─ matches in a volume (same files as the command line writes)
                                              └─ model calls: OpenRouter and the cli-proxy gateway
```

## What you see

- **New match** (left): game, the two players, prompt language, whether they may talk, whether they are asked
  afterwards, who starts, a seed, and the spending limit. A player is `random`, `<endpoint>:<model>` such as
  `claude:claude-haiku-latest`, or an OpenRouter slug. The fields suggest every model that the configured endpoints
  offer; you can type anything else.
- **The match** (right): both players side by side. Spoken messages are in the lane of their seat, the private thoughts
  hang under them (dashed), the moves appear when a turn ends, then the game's own reveal and payout. Above: per seat the
  calls, tokens, cost and the score. The clock, seed and settings are in the middle.
- **Debriefing**: the answers of both players after the game.
- **Model calls**: one row per call with tokens, seconds, cost and result. *Open* shows the exact prompt, the unchanged
  answer, the reasoning and the problems of that call.
- **Earlier matches** (left): every match in the volume. Opening one replays it from `events.jsonl`. Under the match are
  three links: **Download as Excel** (workbook with the sheets *Match*, *Transcript*, *Debriefing*, *Calls* and
  *Raw answers*, ready to show or to filter), the text report, and the raw protocol.

Only one match runs at a time. *Cancel match* stops it cleanly (the protocol ends with `match_aborted`). Reloading the
page while a match runs reconnects to it and shows everything so far.

## Run it

Secrets live in one file outside the repository, `~/.config/llm-shootout/env` (mode 600):

```bash
export SHOOTOUT_WEB_PASSWORD=...          # at least 12 characters, the one password
export OPENROUTER_API_KEY=sk-or-...       # optional, enables OpenRouter models
export LLMSHOOTOUT_PROXY_API_KEY=...      # key of the cli-proxy gateway, see endpoints.md
```

```bash
docker compose build --no-cache web && docker compose up -d web      # https://shootout.stefan-bechtel.de
# without Docker, for development:
source ~/.config/llm-shootout/env
dotnet run --project src/LlmShootout.Web --urls http://127.0.0.1:5088
```

After changing the env file, run `docker compose up -d web` again; the container reads it when it is created.

| Setting | Meaning |
|---|---|
| `SHOOTOUT_WEB_PASSWORD` | the password; the application refuses to start with fewer than 12 characters |
| `Web__MatchesDirectory`, `Web__DataDirectory` | where matches and the session keys live (`/data/matches`, `/data/state` in the container, one volume) |
| `Web__MaxCostCapUsd` | upper bound for the spending limit a request may ask for (default 5) |
| `Web__DefaultMaxCostUsd` | limit when the form names none (default 1) |
| `Web__LoginAttemptsPerMinute` | login attempts per client address and minute (default 5) |
| `Web__MaxMatchesPerDay`, `Web__MaxCostPerDayUsd` | matches and money per day (UTC), kept in `/data/state/usage.json` so a restart does not reset them (defaults 30 and 10) |
| `Web__MaxOutputUsdPerMillion` | OpenRouter models that charge more per million output tokens are refused (default 40) |
| `LLMSHOOTOUT_Endpoints__<name>__…` | endpoints, see [endpoints.md](endpoints.md); `docker-compose.yml` defines `claude` and `codex` for the gateway |

## Security notes

- Everything except the login page, its stylesheet and `/health` needs the session. API calls without it get 401, pages
  redirect to `/login`. The check runs before static files are served.
- The password is compared in constant time. Failed logins are limited per client address and answered after a short delay.
- The session is a cookie that is `HttpOnly`, `Secure` and `SameSite=Strict`, valid for 12 hours of activity. Its signing
  keys are stored in the data volume (unencrypted, inside the container volume), so a restart keeps sessions.
- Requests that change something must come from the same site (`Origin` or `Sec-Fetch-Site` checked).
- Model output is untrusted. The page builds everything with `textContent`; the content security policy allows scripts
  and styles only from the site itself.
- `scripted:` players, which read files, are refused. A request can ask for at most `MaxCostCapUsd`.
- The cap per match cannot protect subscription endpoints (their calls cost nothing per call). Therefore the number of
  matches per day is limited too, and the money per day. Set a credit limit on the OpenRouter key itself as the last backstop.
- The session cookie carries a fingerprint of the password: changing the password ends every open session. Deleting
  `/data/state/keys` ends them as well.
- Request bodies are limited to 64 KB. There is deliberately no global lockout for failed logins, because it would let a
  stranger lock the operator out; the long random password and the per-address limit are the protection.
- The container publishes no port; only Traefik reaches it, over the `proxy` network.
