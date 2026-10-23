# Persistence rules

Repository contracts, byte storages and the memory-first model. Moved out of `AGENTS.md`; the rules are binding. Read this before touching a repository, a storage, `DatabaseWriter` or anything under `Application.Persistence` / `Infrastructure.Persistence`.

* **Repository contracts are written per model** as `IXxxRepository` (`IMatchRepository`,
  `IScoreRepository`, …), each declaring the operations Services and the outer layers need. A relation
  between two models that is stored on its own gets its own repository rather than a column on either
  (`IRoundScoreRepository` relates a round to a score and keeps the team it was set for). Verbs:
  `CreateAsync(XData) → X` (the store assigns the identity) with `UpdateAsync(X)` for a record created that way
  (nothing is created under an id the store has not given out, except BasilBot's fixed id through `IUserRepository.ReplaceAsync`), `CreateOrUpdateAsync(X)` only for a record keyed by
  what it is about (credentials by user, a relationship by its two users, stats by user and mode), `GetAsync(id)`,
  `GetByYAsync(y)` for a unique key, `ListAsync(XQuery, PageRequest) → Page<X>` for listing and search,
  `DeleteAsync(X)`. A listing without a page is complete and is never copied to be returned: a list the
  repository holds (an owned list) comes back as it is, through its read-only interface (`IReadOnlyList<X>`); a
  listing computed when asked (filtered or ordered from memory) is an `IEnumerable<X>`, and the caller decides
  whether to materialise it. Soft deletion is a service setting `DeletedAt` and calling
  `UpdateAsync`. `Save` is only for byte storages (`IXxxStorage`: `SaveAsync`, `OpenAsync`,
  `DeleteAsync`, plus `ListAsync` and `RenameAsync` where the content is managed); `OpenAsync` serves the server's
  own reads, never clients, who get stored assets from the asset host. A storage lists with `IEnumerable<X>` (the
  caller decides whether to materialise it). `IBeatmapsetStorage` keeps each set in `Data/Beatmapsets/{id} {Artist} - {Title}/`: `archive.osz`
  exactly as given is the truth about the set (`Beatmapset.Fingerprint` records it), and
  `archive-novideo.osz`, `versions/{md5}.osu`, `backgrounds/`, `audio/` (with `preview_{audio}`), `video/` and
  `storyboard.osb` are made from it when the set is indexed; nothing else is extracted, and reading never makes a file
  (a missing one reads as absent). Three kinds of type, three owners: the storage holds raw content (`BeatmapsetSource`: a
  `BeatmapsetArchive` `.osz` or a `BeatmapsetFolder` of one set's files), `IBeatmapsetReader` understands it
  (`BeatmapsetContent`), and the repositories hold the records (`Beatmapset`, `Beatmap`; created from `BeatmapsetData`
  through `IBeatmapsetRepository.CommitAsync`). Raw content is identified by a `ContentFingerprint`: its `ContentStamp`
  (size, modification time) is known without reading, the MD5 is taken (`BeatmapsetArchive.FingerprintAsync`) only when
  the stamp differs from the one recorded on the set; a `Beatmap` is identified by the MD5 of its `.osu`. One import is
  one beatmapset: `Beatmapsets.ImportAsync` takes one `.osz`; a folder exists only when placed by hand and is
  packed into an `.osz` first. Difficulties lie at the top of an archive. There are no collections: a `.zip`, a loose
  `.osu` or any other file placed by hand is not a source and is left as it is, as is an invalid source; an upload
  that is rejected leaves nothing, and its set and beatmaps are stored in one transaction. Two sources of one scan never
  become the same set or share a difficulty (the first found wins). A folder being deleted is
  renamed with `.deleted_` first; anything being written (an upload, a derived file, a set folder before it moves into
  place) lies under a top-level `.incoming_` name, which is never listed or watched, so moves stay on one volume. Every
  name written from untrusted input avoids the names Windows keeps for devices (`CON`, `NUL.txt`, `COM1`, ...): a
  derived file gets a `_` prefix. `Beatmapsets.ScanAsync()` runs at startup before clients are served: it checks every entry
  (size and modification time, then the hash), indexes what changed, makes missing files again, and stores every
  record change in one `IBeatmapsetRepository.CommitAsync` transaction; `ScanAsync(entry)` handles each change the
  storage watcher reports. `IBeatmapsetReader` reads an archive into a `BeatmapsetContent` whose files open on demand
  (no `byte[]` copies) and owns the input checks; only the beatmapset service uses it. `IBeatmapAssets` only makes
  derived files at index time (audio previews). `IBeatmapAnalyser.AnalyzeAsync` reads a beatmap's `.osu` once
  and keeps the understood file as long as that `Beatmap` instance lives (again when its hash changes), so changing mods
  reads no file; the Domain holds no such runtime content, and metadata stays in the database. A
  storage of text reads and writes it through `StreamReader`/`StreamWriter` (`IFaqStorage.ReadAsync`,
  `IFaqStorage.CreateOrUpdateAsync`) instead of a raw `Stream`. Extract a shared
  interface only when several repositories genuinely share an operation, and keep the `IXxxRepository`
  contract callers depend on; no parallel generic ports (`ICreatable`) and no generic query system
  (`Query<T>`, `SortOptions`). Key normalization (for example `UserData.NormalizedName`, case-insensitive with a space equal to an underscore) lives on the
  Domain value (`UserData.SafeName`); the server writes the normalized key it looks records up by. A repository filters only by explicit criteria in its query record.
  A query record searched with text (`UserQuery`, `BeatmapQuery`, `MatchQuery`) is `IParsable<T>`: every criterion, a
  state included, is written in the search text (`deleted=1`,
  `hidden=0`, `ended=0`, `private=1`, enum masks as numbers such as `permission=4`), and a state is a `bool?` (`null` for both,
  written `*`) whose default, kept when the text has no token for it, is `null` (both). Every caller narrows a query to the asker
  before using it, `UserQuery.Parse(text).LimitTo(effective)`: `LimitTo` sets each state the asker may not see to
  what a user without that permission sees (`Deleted = false`, `Hidden = false`, `Private = false`). A query used
  without `LimitTo` shows everything, so only the server's own work (a scan, startup recovery) skips it.
* **Memory is the truth; the database is a copy of it.** Memory is where the server works; the database only keeps
  a record of what was done. **No operation waits for the database unless its result comes from the database**
  (a try-operation: an identity the store assigns, a unique value). Everything asks memory first and goes to the
  database only on a miss, to verify or to load into memory; a miss means "not loaded", never "does not exist". A
  change is applied to the live object in memory, and its copy is written to the database alongside. "Cache" means
  only the on-disk `Cache/` directory of derived, rebuildable files (repacked `.osz` archives, audio previews); data
  in memory is never called a cache.
  * **Resident data**, the database records every client uses (`ServerSettings`, general channels, menu banners), is
    loaded at startup and never released; it changes only through its repository, never in the database behind the
    server's back. A change is written first: `CreateOrUpdateAsync`/`DeleteAsync` return once the database has
    committed it, and only then does the resident copy follow (`ResidentWrites`), so a refused write leaves memory
    unchanged and reaches the caller; of concurrent changes of one identity, memory ends equal to the last one
    committed. Reads come from the resident copy. File storages are not resident and hold no file content in memory: the file system is their
    index, read as it is when asked.
  * **On-demand data:** a repository holds one live instance per identity, returned by every `GetAsync`,
    `GetByYAsync` and `ListAsync`. It lives while anything holds it (a session its user, a room its match) or while
    a change of it is not stored yet, and is then released; there is no TTL. A list owned by an object (a user's
    restrictions and relationships, a set's beatmaps, a match's rounds and events) lives as long as
    its owner instance: it is loaded whole the first time it is needed, then changed item by item.
  * **Writes:** for the memory-first objects (users, matches, beatmaps, scores), `CreateOrUpdateAsync`/`DeleteAsync` queue
    a copy of the values **as they are when queued** and return at once; only a try-operation waits for the database,
    because its outcome comes from it. Resident data (above) and records that no business need keeps as live objects
    await the write instead: a relation recorded once and never changed (`round_scores`) lists from the database, and a
    round (`rounds`) is stored when `CreateOrUpdateAsync` returns, its write never pins the live instance (a room's
    round is the instance `ListAsync` returns while it is held). `DatabaseWriter` keeps
    one queue per root (`Root.User`, `Root.Match`, `Root.Beatmapset`, `Root.Server`) and sends its writes one
    statement at a time, in order; a copy not sent yet is replaced by a newer copy of the same identity. At most
    `Basil:Database:WriteConnections` connections (8 by default, see [`plans/evidence/postgresql-direct-writes-20261008`](plans/evidence/postgresql-direct-writes-20261008/results.md)) write at once. No write is grouped with unrelated
    writes; a business operation that must store several rows together gets one try-operation of its own. A score
    belongs to its player (`Root.User`); which round it was played in is a relation of its own
    (`round_scores`), listed with the round.
  * **Errors:** only environment errors (lost connection, timeout, server shutting down, read-only after a failover,
    deadlock, authentication, permissions) are retried, before any later write of the same root. Any other error
    fails that write alone: a try-operation throws it to its caller, a copy is logged as an error. A try-operation
    runs in its own transaction and settles a broken commit with a write ticket row stored in the same transaction; its cancellation token counts
    only while it waits for its turn. Appends (logins, match events) carry no business id: the database assigns it,
    the repository queues the append and returns at once (logging in works without a database), and
    `DatabaseWriter.AppendAsync` runs each append in its own transaction settled the same way, so it is stored
    exactly once.
  * **No SQL in Persistence.** Everything is Entity Framework Core with LINQ and row objects (`XxxRow` entities, never the Domain types); no Dapper, no `FromSql`/`ExecuteSql`/`SqlQuery`, no `NpgsqlCommand`. A snapshot write builds its row when it is queued and applies it with `RowWrites.UpsertAsync`/`ReplaceAsync` (or `ExecuteUpdate` for a few columns); a row with a store-generated key is created inside the lambda so a retry starts clean. Unique violations are recognised through `exception.IsUniqueViolation()`, which looks through Entity Framework wrappers.
  * **Reads** go straight to PostgreSQL on a context of their own (a pooled connection, no change tracking: memory is the truth, not the context) and see only what is committed. A listing
    asks the database which rows match and returns the live instance of each; a row whose deletion is still queued
    never comes back into memory.
  * **A stored row is trusted.** Rows are written only by the server, through the Domain, so a row that fails the Domain rules on load is a fault of the database, not a case to work around: the read throws. The exception is configuration an operator edits by hand (the settings and the menu banners): a value that cannot be read is logged and the default is kept, or the banner is left out, so the server still starts.
  * **A value unique across a table** (a user's safe name, a beatmap's hash or osu! id, a score's checksum, an id the
    store assigns): when memory holds a record with that value the repository rejects the change at once; otherwise
    the insert runs as a try-operation and a unique violation from the database means the value is taken. Either way
    the repository throws `AlreadyExistsException` (a rename returns `false`) and does not load the existing record.
  * A derived read model (the match report) is projected from memory when asked; it is not stored or rebuilt.
  * A child refers to a parent of another root only once the parent is committed (such a parent is created by an
    awaited try-operation); parents are created before their children. One database belongs to one Basil process. An
    operation's lambda uses only its connection and transaction and never queues another operation.
  * Without a database, creating identities and checking unique values wait for it; everything already in memory
    keeps working.
