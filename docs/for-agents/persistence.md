# Persistence rules

Repository contracts, byte storages and the Registry / Repository / Storage model. Moved out of `AGENTS.md`; the rules are binding. Read this before touching a repository, a storage, a registry or anything under a module's `Application/Persistence`, `Application/Storage` or `Application/Registry`, an `Infrastructure/Persistence` or `Infrastructure/Storage` folder, or `Infrastructure.Common`.

> **Migration status.** The old write-behind model (identity map, owned lists, `DatabaseWriter` with its per-root queues and write tickets, `MemoryRepository`, resident-data writes) is **being removed**, module by module. Until a module is migrated its code still behaves as the old model; the rules below are the target and are no longer described in the old terms.



* **Repository contracts are written per model** as `IXxxRepository` (`IMatchRepository`,
  `IScoreRepository`, …), each declaring the operations Services and the outer layers need. A relation
  between two models that is stored on its own gets its own repository rather than a column on either
  (`IRoundScoreRepository` relates a round to a score and keeps the team it was set for). Verbs:
  `CreateAsync(XData) → X` (the store assigns the identity) with `UpdateAsync(X)` for a record created that way
  (nothing is created under an id the store has not given out, except BasilBot's fixed id through `IUserRepository.ReplaceAsync`), `CreateOrUpdateAsync(X)` only for a record keyed by
  what it is about (credentials by user, a relationship by its two users, stats by user and mode), `GetAsync(id)`,
  `GetByYAsync(y)` for a unique key, `ListAsync(XQuery, PageRequest) → Page<X>` for listing and search,
  `DeleteAsync(X)`. A listing without a page is complete; it is computed when asked and is an `IEnumerable<X>`, and the caller decides
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
  become the same set or share a difficulty (the first found wins). Deleting is instant: a folder is
  removed recursively at once and a failure reaches the caller; nothing is renamed first. Anything being written (an upload, a derived file, a set folder before it moves into
  place) or set aside (the previous folder of a set while its replacement moves in, so a failed move can put it back; it is deleted right after) lies under a top-level `.incoming_` name, which is never listed or watched, so moves stay on one volume. Every
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
  what a user without that permission sees (`Deleted = false`, `Hidden = false`); `MatchQuery.LimitTo(effective, viewer)` sets
  `VisibleTo` to the viewer when they lack `RoomViewPrivate`, so private matches are listed only when the viewer took part in
  them or manages them (`private=1` then returns just those). A caller that serves match-owned data (the match, its rounds, events, report or round-score relation) to a
  user must check `ScopedLobby.CanViewAsync` first; the match repositories are plain storage and do not gate. A query used
  without `LimitTo` shows everything, so only the server's own work (a scan, startup recovery) skips it.
* **Three stores, three kinds of data.** A store holds one kind of data and is reached through a contract in the module's
  Application project; its implementation is in the module's Infrastructure project.
  * **Registry** (`Registry/`): live runtime objects in RAM (`UserRegistry`, `Lobby`, the channel registries). It is the
    truth about what is running and owns the operations on what it holds. It stores nothing durable.
  * **Repository** (`Persistence/`): durable records. Every operation awaits the database; the database is the truth.
    Small records that are read often (a user, settings, a channel) may be cached **cache-aside**: read the cache, on a
    miss read the database and fill the cache, and a write goes to the database first and then updates or drops the cache
    entry. Large or streamed data never passes through the cache (`beatmaps.objects`, images, replays, `.osz` archives).
  * **Storage** (`Storage/`): files and media (beatmapsets, replays, avatars, banners). The file system is the index.
    Images are served through ImageSharp.Web ([`assets.md`](../for-developers/assets.md)).
* **A write is awaited, and a failure reaches the caller.** There is no queue of pending writes and no copy that is
  "sent later"; a refused write leaves the cache unchanged and throws or returns its failure to the caller. A value that is
  unique across a table is checked by the insert: a unique violation (`exception.IsUniqueViolation()`) becomes
  `AlreadyExistsException` (a rename returns `false`). A business operation that stores several rows together uses one
  transaction of its own. Without a database, anything that needs it fails; objects already in a registry keep working.
* **Reads are bounded.** A listing is a query with a limit that returns a page, and the database connection is released
  before the result is sent to a client. A listing does not instantiate live objects and has no mandatory `COUNT`; the
  next page is found by a keyset where possible. A live object that exists in a registry is looked up in the registry, not
  rebuilt from the row.
* **A stored row is trusted.** Rows are written only by the server, through the Domain, so a row that fails the Domain
  rules on load is a fault of the database, not a case to work around: the read throws. The exception is configuration an
  operator edits by hand (the settings and the menu banners): a value that cannot be read is logged and the default is
  kept, or the banner is left out, so the server still starts.
* **No SQL text in Persistence.** Everything is Entity Framework Core with LINQ and row objects (`XxxRow` entities, never
  the Domain types); no Dapper, no `FromSql`/`ExecuteSql`/`SqlQuery`, no `NpgsqlCommand`. The generic EF API is what a
  module's Infrastructure project uses; what is specific to PostgreSQL (the Npgsql provider, `Database.cs`, error
  recognition, design-time factory and the migrations) belongs to `Basil.Infrastructure.Postgres` (**target**; today it sits
  in `Infrastructure.Common` and the module projects) which only the composition root references. Tests run on PostgreSQL only.
* **Module schemas.** Each module has its own `DbContext`, row configuration set and migration history
  (`__ef_migrations_<module>`). `DatabaseMigrator` applies Common, Users, Authentication, Beatmaps, Content, Scores,
  Multiplayer and Chat in that order; referencing contexts preserve foreign keys by mapping principal rows as excluded from
  their migrations. The system is not in production, so a module's migrations may be regenerated while the schema settles; see
  [`database.md`](../for-developers/database.md#migrations).
* **Streaming is mandatory.** Data reaches the consumer directly; no `MemoryStream` in Application. A read is bounded,
  loaded, the connection released, then sent. Every upload is spooled to a temporary file, validated, then moved
  atomically (see the streaming rule in `AGENTS.md`).

## How a repository is written now

The Authentication module is the pilot; copy its shape (`PostgresCredentialRepository`, `PostgresSessionRepository`).

* **One helper, `DatabaseSession`** (`Infrastructure.Common/Persistence`), injected into the repository. It has no queue
  and no retry. `ReadAsync<TContext, T>(query, ct)` runs a bounded query on a context of its own (through
  `DatabaseReader`). `WriteAsync<TContext>(change, ct)` and `WriteAsync<TContext, T>(change, ct)` create a context whose
  commit is confirmed, run the change, call `SaveChanges` and dispose; the second form returns what the change returned
  (a row count). `TransactAsync<TContext>(work, ct)` is the same inside one transaction, for a business operation that
  stores several rows. Entities the change adds or modifies are saved by the helper; `ExecuteUpdate`/`ExecuteDelete`
  run on their own.
* **Failure contract.** A write is finished when the call returns. A unique violation becomes `AlreadyExistsException`;
  every other failure (foreign key, connection, timeout) reaches the caller as the original exception. Nothing is
  swallowed or retried, so a caller that must survive a failed write handles it, and a caller that cannot await (code
  under a lock) must move the write to an event handler outside the lock.
* **Cache-aside rule.** A small record read on every request may be cached with `CacheAside<TKey, TValue>`
  (`Infrastructure.Common/Registry`): `GetAsync(key, load)` fills it on a miss (an absent record is never cached), and
  the repository calls `Remove(key)` only after the awaited write succeeded, so a refused write leaves the cache
  unchanged. A value loaded while its entry is being removed is not cached. Credentials cache the bcrypt hash by user id.
* **Concurrency rule.** A row with a single owner (a credential, a session) is written last-write-wins, awaited.
  Counters and aggregates (user stats, score totals) use column-level `ExecuteUpdate` or an atomic increment, never
  read-modify-write.
* **Records are copies; the registry holds the live object.** A repository of the Users module (`UserRepository` and
  the others) returns a fresh record from every read and keeps none; the online `User` in `UserRegistry` is the live
  truth. A change to an account is stored first and applied to the live record afterwards, so a refused write leaves the
  live state as it was. Code that holds a lock publishes an event and a handler outside the lock stores (the closed
  session is stored by `SessionEndHandler`).
