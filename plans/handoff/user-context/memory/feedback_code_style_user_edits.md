---
name: code-style-user-edits
description: Style the user applied by hand on 2026-10-04 — beatmapset-centred contract names, I{Owner}{Thing}Storage, Domain-owned constants/factories, static where stateless, patterns
metadata:
  type: feedback
---

From the user's own cleanup of the Application code (2026-10-04), now in AGENTS.md ("Name a contract after the
object it acts on", "Knowledge about a Domain value lives on that value", "Code style"):
- Contracts named after the acted-on unit: IBeatmapsetService/Reader/Storage/Mirror, BeatmapsetImportResult.
- Byte storages `I{Owner}{Thing}Storage` (IUserAvatarStorage, IMenuBannerStorage, IMenuSeasonalsStorage);
  no medium words (Image, Background, Archive) or redundant qualifiers (ISettingsRepository, not IServer…).
- Domain knowledge on the Domain type: `ClientFlags.CheatSigns` enum member, `BeatmapObjects.NewFrom(mode)`.
- Small enum lives in its owner's file (ConnectionType in Connection.cs); stateless members `static`;
  primary constructors, property patterns, merged guards; registration class `DependencyInjection`.

**Why:** user's taste, shown by editing rather than telling.

**How to apply:** follow these in new code and in agent prompts; don't rename back. See [[contract-naming-neutral]].
