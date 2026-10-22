---
name: project-console-cli
description: Host phase will add a Minecraft-style operator console in project Basil.Cli (XenoAtom.*); first admin is created there; no auto bootstrap
metadata:
  node_type: memory
  type: project
  originSessionId: 8cd03193-4520-4c7f-bd41-bf52f68a6113
  modified: 2026-10-19T13:01:16.439Z
---

The server creates no account by itself (the `admin`/`admin` bootstrap was built and reverted on 2026-10-19). The first administrator is created from an operator console in a new project `Basil.Cli` (Host phase, not built yet): an interactive console like a Minecraft server, off with `--noconsole`. A console command acts as BasilBot (by id `BasilBot.Id`) and skips rights checks. The CLI uses the `XenoAtom.*` libraries, not Spectre.Console.

**Why:** the user rejected hard-coded default credentials and a persisted "bootstrapped" flag; whoever sits at the console owns the machine.
**How to apply:** do not add any automatic account creation or admin bootstrap; when the Host phase starts, build the console with XenoAtom and put account creation there. See AGENTS.md "The console is the operator's entry point". Related: [[project-identity-permissions]].
