---
name: project-week-plan-20261021
description: Week plan 2026-10-21 for osuBasil - uniform OCap pattern, mirrored test suite, BasilBot replies; decisions and progress file
metadata:
  type: project
---

Week plan lives in `plans/week-plan-20261021.md` in the repo (checkbox progress); cron reminder fires 17:11 on 2026-10-21 to resume.

Decisions (interview 2026-10-21): `Scoped<T>` base with `Permissions?` Require(mask) (null = met); `Result<TOutcome>(Outcome, Permissions? Missing)`, each XOutcome has Ok=1; no NotAuthorized/Restricted split; granting uses effective permissions, ranking uses granted; ConfigureAwait banned (architecture test); renames Application.Storage->Persistence, Services->Capabilities, Objects->Runtime (capability ports move to Capabilities), Infrastructure.Storage->Persistence; Bot project -> Basil.Bot, bot id 1 taken over/created at startup, replies in Data/Bot/replies.json (ICU, one language, hot reload); ModerationRecord->ModerationLog; tests mirror src one project each, Testcontainers Postgres (Docker must be started), assets from C:\Users\haith\Desktop\Data; AGENTS.md split into docs/for-agents/*.

**Why:** owner wants a perfectionist, uniform pattern and tests that verify the software for them.
**How to apply:** follow the plan phases; supersedes [[project-object-scope-progress]] naming (Storage/Services/Objects) and the ConfigureAwait guidance.
