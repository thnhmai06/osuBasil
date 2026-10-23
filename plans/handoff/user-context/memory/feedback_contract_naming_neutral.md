---
name: contract-naming-neutral
description: "User rule (2026-10-03): every business capability gets a contract in Basil.Application.Contracts, named so it survives a change of mechanism (ScanAsync, not ScanFileAsync)."
metadata:
  node_type: memory
  type: feedback
  originSessionId: a5c6af71-b5b0-44e3-bdd8-0b8defbe36c1
  modified: 2026-10-03T11:24:54.087Z
---

Contracts declare every capability with business meaning, including ones Infrastructure implements
entirely (beatmap assets, mirror, archive reading). Pure plumbing (logging, diagnostics, SSE hub, TLS,
mDNS, update check) gets no contract. Contract and member names must not reveal the mechanism: if the
work moved from the file system to a network source, the name would stay the same ("đặt là ScanAsync thay
vì ScanFileAsync").

Infrastructure only implements the contracts whose logic needs external dependencies and must not
reference `Basil.Application.Services`; it gets services through DI by contract. Background services:
Application defines the methods of one run; Infrastructure runs them continuously.

**Why:** user's design statement when approving the storage/services split (2026-10-03).

**How to apply:** check every new interface/method name in Contracts against this; record it in
AGENTS.md during phase 1. Related: [[storage-services-split]], [[storage-vs-services]].
