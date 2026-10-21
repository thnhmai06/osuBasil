# Code style rules

Moved out of `AGENTS.md`; the rules are binding. Read this before writing or reviewing C#.

* A small type used by one type lives in that type's file (`ConnectionType` in `Connection.cs`).
* Object-oriented, not procedural: an operation goes through the object that owns the state it changes
  (`DataStorage.SaveAsync`, `RoomState.IsManagedBy`), not through a static helper that is handed the state.
* A type that is a collection implements the matching BCL interface (`DataStorage : IReadOnlyDictionary<string,
  FileInfo>`); a business value with a text form is a value type implementing `IParsable<T>` and `IFormattable`
  (`ContentReference`).
* **Collections are exposed, not copied** (most mistakes so far were in registries and storages):
  * Never materialise a collection only to hand it out (`[.. _channels.Values]`, `.ToList()`, `.ToArray()` on a
    return). Return the collection itself through an interface it implements (`IReadOnlyDictionary<string,
    GeneralChannel> All => _channels`), or an `IEnumerable<T>` for a sequence computed on the fly. Copy only
    where the code needs a snapshot (to change the collection while walking it, or to read it outside the lock that
    guards it, as `FileBeatmapsetStorage.ListAsync` does). `ConcurrentDictionary.Values` and `.Keys` are such a copy
    (taken under every lock), so a registry exposes the dictionary, not its `Values`.
  * A collection that is both read and changed is exposed twice, with no methods that only forward to it: a public
    read-only interface and an `internal` read-write one named `Mutable{X}` (`public IReadOnlySet<User> Banned`,
    `internal ISet<User> MutableBanned`; `IDictionary`/`IList` likewise). A method stays only when it carries a rule
    of its own (`Lobby.Add` releases the reserved id; `User.Add` files a connection by its kind).
  * Expose the most derived interface the value has (`IReadOnlyDictionary`, `IReadOnlySet`, `IReadOnlyCollection`
    rather than `IEnumerable`), unless the extra property belongs to the implementation and not to the contract: a
    storage that walks files returns `IEnumerable<T>`, not `IOrderedEnumerable<T>`, even when it sorts them.
* Every stored time is UTC (`DateTimeOffset.UtcNow`, `TimeProvider.GetUtcNow()`, `timestamptz` columns); only the
  presentation converts it to a local date and time. Never `DateTimeOffset.Now` or `DateTime.Now`.
* A folder that would share its name with a type in it is plural (`Multiplayer/Matches/Match.cs`), so code never
  writes `Match.Match`.
* A member that uses no instance state is `static` (`LobbyService.RoomOccupied`).
* Prefer primary constructors, property patterns (`room is { InProgress: false, Beatmap: not null }`) and
  one guard for conditions that return the same result; no redundant casts, `? true : false` or
  discarded-task lambdas (`_ => action()`, not `_ => _ = action()`).
* Application.Objects registers its objects, registries and contexts, and the internal services, in the public
  `DependencyInjection.AddApplication()` (it calls the internal `AddApplicationServices()` of Application.Services).
* **Read the IDE's suggestions to find better patterns** *(notes for the future code-principles skill)*. After a
  change, run Rider's `lint_files` / `get_file_problems` with warnings and suggestions on, and treat what it says as
  a review comment, not noise. The agent decides each case; the IDE only finds candidates. Act on:
  * **Async and cancellation.** When a call has an async overload, use it, and pass the `CancellationToken` that is
    in scope (`Method has 'async' overload with cancellation support`, `Use 'await using'`, `CancelAsync`,
    `ZipArchiveEntry.OpenAsync`, `WithCancellation` on an async stream). A private/internal method whose callers hold a
    token takes it. Do not change a public signature just to carry a token: report it.
  * **LINQ.** A `foreach` that only filters, maps, finds, counts or aggregates becomes a query when it reads as a
    sentence (`Loop can be converted into LINQ-expression`; `.OfType<T>()` to drop `null`s). Keep the loop when it
    awaits, mutates in order, has side effects, or the query would be harder to read. Never write a `ForEach`
    extension.
  * **Switch.** Prefer `return x switch { … }`, then a switch expression assigned to a variable, then a classic
    `switch` statement, then an `if`/`else` chain (`Convert into 'return' statement`, `Convert 'if' to switch`,
    `Some values of the enum are not processed`). Use property, relational and `or`/`and`/`not` patterns when they
    replace repeated tests of one value. Keep a plain `if` for one or two unrelated tests, and do not turn a
    `switch` statement into a nullable-result switch expression followed by an `if` just to silence a hint.
  * **Small ones.** Unused `using`, collection expressions, redundant type arguments (`Task.FromResult(x)`), a local
    function placed before the `return`, `static` for a member that uses no instance state, `with` for record
    copies, a variable that hides an outer one, a logging argument that is expensive when the level is disabled
    (guard it or use a source-generated logger).
  * **Ignore** the allocation hints (object, closure, delegate, boxing), `Property can be made init-only` on types that
    a serializer or Dapper fills, `init` or collection `never used/updated` reports on reflection-filled DTOs, and
    style hints that contradict the formatter. Do not remove a deserialization setter on the IDE's word alone.
  * **Explore.** Other inspections are worth reading once per area (unhandled enum values, possible unintended
    reference comparison, duplicated branches, `Merge into pattern`); adopt one as a rule when it finds a real mistake
    twice.

