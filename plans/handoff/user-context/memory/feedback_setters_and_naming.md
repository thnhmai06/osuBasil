---
name: setters-and-naming
description: "osuBasil: setters only assign + validate (throw); anything with consequences is a dedicated method with the setter's visibility restricted. One concept, one name everywhere (Timestamp, StartedAt/EndedAt, StartsAt/EndsAt, CreatedAt/UpdatedAt/DeletedAt, by)."
metadata:
  type: feedback
---

**Setters:** a setter only stores the value and throws if invalid. It does not emit events or
change other state. A change with consequences (event, cascading fields, authority check, several
fields in one event) is a dedicated method, and the setter is made `private`/`internal` so callers
cannot bypass it. This supersedes the older "setter of a forwarding property emits the event" rule
in [[feedback-runtime-references]] and the "set properties directly, no methods" rule in
[[feedback-room-api-shape]] (a method is fine when it is a real operation with consequences).

**Naming:** one concept, one name in every model, parameter, event and API. Moment something
happened = `Timestamp`; span = `StartedAt`/`EndedAt`; future window = `StartsAt`/`EndsAt`; stored
record lifecycle = `CreatedAt`/`UpdatedAt`/`DeletedAt`; caller of an operation = `by`. No
`OccurredAt`/`When`/`Since`/`LoginTime` synonyms.

**Why:** user, 2026-09-30, when choosing R28 option A: "Setter sẽ chỉ thay đổi giá trị của field và
validate + throw nếu ko hợp lệ…" and "Thống nhất tên thành Timestamp thay vì Timestamp rồi
occurredAt rồi When".

**How to apply:** when designing members, check both rules; when a name for a time/actor concept is
needed, take it from the table in `plans/application-environment-plan-20260930.md` §A2.
