# W4: uploads judged before they are received (plan P4)

Source of truth: `plans/review/release-1.0.md` decision D27, plan P4 steps 1, 2, 3, 5, 6, and the AGENTS.md
"Streaming" invariant (the D27 target text). Read them first, and `AGENTS.md`. W1 and W3 are in the tree.

## Goal

No upload is buffered whole before it is judged. The caller is authorized before the body is read, a declared length
over the limit of that kind is refused at once, the body is read as a stream, its first bytes are checked, the
connection is cut on a mismatch, on too many bytes or on a too slow sender, and accepted bytes go straight into the
target storage's temporary file, are validated there and moved atomically into place. A refused upload is logged,
never kept. `IUploadSpool`, `FileUploadSpool`, `Quarantine` and their folders are removed.

## The uploads

| Route | Kind | Limit setting | Content check |
|---|---|---|---|
| REST `POST /v1/beatmapsets` (`BeatmapRoutes.UploadAsync`) | `.osz` | `Uploads.OszMaxSize` | ZIP magic `PK\x03\x04` |
| REST avatar (`UserRoutes`, line ~218) | image | `AvatarMaxSize` | PNG / JPEG / GIF / WebP magic, then dimensions |
| REST menu images (`MenuRoutes`) | image | the menu limit if one exists, else `AvatarMaxSize` | as above |
| Stable `osu-screenshot.php` | image | `ScreenshotMaxSize` | as above |
| Stable `osu-submit-modular-selector.php` | replay in a score form | `ScreenshotMaxSize` (today's value) | none beyond the form (replay bytes are opaque) |

## Steps

1. **Per-route kind and limit (P4.1).** Remove both `UploadLimit` classes (`Basil.Host.Rest/Http/UploadLimit.cs`,
   `Basil.Host.Stable/Support/UploadLimit.cs`) and replace them with one helper in `Basil.Host.Shared` that, for a route,
   sets the request's `IHttpMaxRequestBodySizeFeature` to that kind's limit plus a small form slack, and sets
   `IHttpMinRequestBodyDataRateFeature` to a minimum rate (e.g. 1 KiB/s after a 5 s grace; a constant, not a setting).
   Kestrel then refuses a declared `Content-Length` above the limit before reading (413) and cuts a body that grows past
   it or arrives too slowly. Each route calls it with its own kind; no route gets the `.osz` limit unless it is the
   `.osz` route.
2. **Authorize before the body (P4.1, D27).** For each REST upload, add a Mediator query answered by the object that
   owns the privilege (e.g. "may this session upload a beatmapset / set this avatar / change the menu") returning the
   object's outcome enum; the route sends it before touching the body and answers the refusal at once. Keep the real
   operation's own check (it stays the authority; the early one only avoids reading a body for nothing). For Stable,
   whose credentials travel inside the form, see step 4.
3. **Streaming multipart reader (P4.2).** A helper in `Basil.Host.Shared` built on `Microsoft.AspNetCore.WebUtilities.MultipartReader`
   replaces `ReadFormAsync` on the upload routes: it reads small text fields into a dictionary (bounded count and
   length), and hands the named file section to the caller as a stream without buffering it. It also wraps that
   stream in a counting stream that throws when more than the limit is read (a body without a declared length).
   Content sniffing: a small wrapper that reads the first bytes (≤ 16), checks them against the kind's magic, and
   then yields those bytes followed by the rest; a mismatch throws. On any failure the route calls
   `HttpContext.Abort()` (after logging one line with the route, the session if known, and the reason) instead of
   reading the rest. Use the standard library; no new package.
4. **Stable (P4.5, bancho.py behaviour, the osu! wire is fixed).** `osu-screenshot.php` and
   `osu-submit-modular-selector.php`: before reading the body, accept only a client address that has an open osu!
   session (a lookup in the session registry, allowed for hosts); otherwise answer as today's refusal without reading.
   Then read the form with the streaming reader; the credential fields are checked as today. If the file section comes
   before the fields that authenticate it, stream the file into the target storage's temporary file (bounded by the
   limit) and authenticate before it is moved into place; do not change what the client sends or receives. Find the
   real field order in bancho.py (`app/api/domains/osu.py`, `osuSubmitModularSelector` and `osuScreenshot`) and say in
   the report which case applies.
5. **Storage writes the stream (P4.3).** The Application objects (`Server.Screenshots.UploadAsync`,
   `UserProfile.SetAvatarAsync`, the beatmapset import, the menu images) stop using `IUploadSpool`. The storage port of
   each kind gets (or already has) a save that streams into its own temporary file and takes a validation callback
   (for images: the inspector's dimension check on the temporary file) before the atomic move; a refused file is
   deleted, and the refusal is the same exception or outcome as today. The beatmapset path keeps its `.incoming_`
   staging inside `Beatmapsets/`; `IBeatmapsetStorage.QuarantineAsync` and `Quarantine.Keep` become a delete plus a
   log line. Remove `IUploadSpool`, `ISpooledUpload`, `FileUploadSpool`, `Quarantine`, `DataPaths.Spool`,
   `DataPaths.Quarantine`, their registrations and the context members that carried them. The Domain/Application rules
   of AGENTS.md hold: Application calls ports, never a file API; no `MemoryStream` or `byte[]` copy of a payload.
6. **Docs (P4.6).** `AGENTS.md` "Streaming" (drop the **target** mark and the `IUploadSpool`/`Quarantine` sentence),
   `docs/for-agents/persistence.md`, `docs/for-developers/gateway.md` (only what changed), `configuration.md` (the
   `Spool` and `Quarantine` rows).

Out of scope: the Gateway's per-user upload count and byte allowance (P4.4, W10), anything else.

## Rules for you

Rider MCP (`search_symbol`, `find_usages`, `safe_delete`, `get_file_problems`, `build_solution_*`), OpenCode Go at
`http://localhost:4096` with cheap flash models for mechanical edits only. Build only
(`dotnet build Basil.slnx -c Release --nologo -v q`), no tests (deferred; ignore `tests/`), no commit, no `git add`.
AGENTS.md code rules apply (no `.ConfigureAwait(false)`, no wait between a privilege check and the change it guards
inside an object, hosts hold no business rule, user-visible strings in their reply class). A step needing an unwritten
decision: skip it, finish the rest, report.

## Report

Per step the files changed; the Stable field order you found; skipped steps and why; decisions you took; build result.
