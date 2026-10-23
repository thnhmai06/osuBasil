# Memory, data and resources

**Prefer references over values. Materialize a value only when it is needed.**

Hold a reference to data instead of its whole content. A reference can be an object reference, an id, a
path, a handle or any identifier that gives access to the original data. Load and keep a value in RAM only
to process it, to access it directly, or because a deliberate cache needs it.

RAM holds what the current work needs, not everything the system could reach.

## Rules

### Reference over value

When you only need to identify, manage or reach data, store a reference to it, not its content.

```csharp
// A metadata object refers to the file; it doesn't carry the image bytes.
public sealed record Banner(string Name, string FilePath);
```

For example, an uploaded package stays unpacked on disk and its records point at its files. A downloadable
archive of it is derived data, rebuilt into the cache folder when someone asks for it.

### Load on demand, release with the owner

- Load data when it is used, not ahead of time "just in case".
- Tie each item's lifetime to its owner. A live object lives while something holds it. A list owned by an
  object lives exactly as long as that object.
- Resident data that every client uses all the time (server settings, the home page content) may load at
  startup and stay.
  That is a deliberate exception, not the default.

### Stream instead of materializing

When data can be processed in pieces, pass a `Stream` or something equivalent. Don't load it into a
`byte[]`, `string` or collection by default.

- Binary content: `Stream`.
- **Text content: `StreamReader` to read and `StreamWriter` to write** (or `TextReader`/`TextWriter`), never a
  raw `Stream`. The storage that owns the text then decides its encoding once, and every caller reads
  characters, not bytes it would have to decode itself.
- A writer that stores its content when it is disposed is a good contract for "replace this entry".
- Return `string`, `byte[]` or a collection only when the caller really needs the whole value.

```csharp
public interface IHelpArticleStorage
{
    Task<IEnumerable<string>> ListAsync(CancellationToken cancellationToken = default);

    /// <returns>The text reader, or <see langword="null" /> when no entry has that name.</returns>
    Task<StreamReader?> ReadAsync(string entry, CancellationToken cancellationToken = default);

    /// <returns>A writer whose text becomes the entry's text once the writer is disposed.</returns>
    Task<StreamWriter> CreateOrUpdateAsync(string entry, CancellationToken cancellationToken = default);
}
```

Keep a processing pipeline in its in-memory representation from start to finish, and convert to `byte[]`
only at the system boundary. A decoded image stays an image object through every crop and resize, until
the line that encodes it into the output document.

Don't apply this rule mechanically to small data, or where returning the value is simply clearer.

### Collection types in APIs

- **Parameters** take the least specialized type that works, usually `IEnumerable<T>`. Don't require
  `ICollection<T>` only to read `Count` (Microsoft collection guidelines).
- **Results that the producer can stream**, where the caller decides whether to materialize, return
  `IEnumerable<T>`. Listing files in a storage is an example.
- **Results that are complete by nature**, where callers may count or index, return `IReadOnlyList<T>` or
  `IReadOnlyCollection<T>`. A repository's result for a query is an example.
- Never return `null` for a collection. Return an empty one. When you deserialize untrusted data, normalize
  missing collections to empty (`Items ?? []`).
- Don't expose `List<T>` or `Dictionary<TKey, TValue>` in public APIs, and don't provide settable collection
  properties.
- Don't add `.ToList()` or `.ToArray()` out of habit. Materialize only for a snapshot, repeated enumeration or
  a specific API requirement.
- **Expose a collection, don't copy it.** Never materialize a collection only to hand it out
  (`[.. _channels.Values]`, `.ToList()` on a return). Return the collection itself through an interface it
  implements (`IReadOnlyDictionary<string, ChannelSession> All => _channels`), or an `IEnumerable<T>` for a
  sequence computed on the fly. Copy only where the code needs a snapshot: to change the collection while
  walking it, or to read it outside the lock that guards it. `ConcurrentDictionary.Values` and `.Keys` are
  such a copy, so a registry exposes the dictionary, not its `Values`.
- **A collection that is both read and changed is exposed twice,** with no methods that only forward to it: a
  public read-only interface and an `internal` read-write one named `Mutable{X}` (`public IReadOnlySet<User>
  Banned`, `internal ISet<User> MutableBanned`; likewise `IDictionary` and `IList`). A method stays only when
  it carries a rule of its own (an `Add` that also releases a reserved id).
- **Expose the most derived interface the value has** (`IReadOnlyDictionary`, `IReadOnlySet`,
  `IReadOnlyCollection` rather than `IEnumerable`), unless the extra property belongs to the implementation
  and not to the contract: a storage that walks files returns `IEnumerable<T>`, not `IOrderedEnumerable<T>`,
  even when it sorts them.
- **A listing without a page is complete and is never copied to be returned.** A list the repository holds (an
  owned list) comes back as it is, through its read-only interface (`IReadOnlyList<T>`). A listing computed
  when asked (filtered or ordered from memory) is an `IEnumerable<T>`, and the caller decides whether to
  materialize it.

Microsoft: [Guidelines for collections](https://learn.microsoft.com/dotnet/standard/design-guidelines/guidelines-for-collections).

### Minimize copying

- Reuse existing data, or reach it through a reference, view or memory region, instead of copying it.
- Use `ref`, `in`, `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>` or `ReadOnlyMemory<T>` where their semantics,
  lifetime and safety fit.
- Don't assume that passing by reference is always faster, or that every copy must be avoided. Measure when
  it matters.

Microsoft: [Memory\<T\> and Span\<T\> usage guidelines](https://learn.microsoft.com/dotnet/standard/memory-and-spans/memory-t-usage-guidelines).

### Values for processing and caching

- Keep a value in RAM when you process it directly or access it repeatedly.
- A cache serves a specific purpose and has a defined policy for lifetime, size or eviction.

### Say "cache" only for a cache

- "Cache" means derived, rebuildable data, for example a `Cache/` folder of repacked archives or generated
  previews. Delete it and the system rebuilds it.
- Live state held in memory is not a cache. When memory is the source of truth, call it memory, and don't
  name its holders `*Cache` or `CachedRepository`.
- When memory is the truth, a lookup that misses means "not loaded yet", never "does not exist". Check the
  persistent store before concluding that something doesn't exist.

### Memory is the truth; the database is a copy of it

For a server that keeps live state, memory is where the work happens. The database only keeps a record of
what was done.

- **No operation waits for the database unless its result comes from the database** (a try-operation: an
  identity the store assigns, a unique value). Everything else asks memory first and goes to the database
  only on a miss, to verify or to load into memory. A change is applied to the live object in memory, and its
  copy is written to the database alongside.
- **Resident data**, the records every client uses all the time (server settings, home page
  content), loads at startup and is never released. It changes only through its repository. File storages
  aren't resident and hold no file content in memory: the file system is their index, read as it is when asked.
- **On-demand data:** a repository holds one live instance per identity and returns it from every `GetAsync`,
  `GetByYAsync` and `ListAsync`. The instance lives while anything holds it (a session its user, a room its
  meeting) or while a change of it isn't stored yet, and is then released. There is no TTL. A list owned by
  an object (a user's restrictions, a meeting's events) lives as long as its owner instance: loaded whole the
  first time it is needed, then changed item by item.
- **Writes** queue a copy of the values *as they are when queued* and return at once. One queue per root
  (`User`, `Meeting`, `Recording`, `Server`) sends its writes one statement at a time, in order; a copy not sent
  yet is replaced by a newer copy of the same identity. A bounded number of connections write at once. No
  write is grouped with unrelated writes; a business operation that must store several rows together gets one
  try-operation of its own.
- **Errors:** only environment errors (lost connection, timeout, shutdown, read-only after a failover,
  deadlock, authentication, permissions) are retried, before any later write of the same root. Any other
  error fails that write alone: a try-operation throws it to its caller, a queued copy is logged as an error.
  A try-operation runs in its own transaction and settles a broken commit by asking the database whether it
  committed.
- **Appends** (sign-ins, event history) carry no business id: the database assigns it, the repository queues
  the append and returns at once, so signing in works without a database, and the append is stored exactly
  once.
- **Reads** go straight to the database on a pooled connection of their own and see only what is committed. A
  listing asks the database which rows match and returns the live instance of each. A row whose deletion is
  still queued never comes back into memory.
- **A value unique across a table:** when memory holds a record with that value, reject the change at once;
  otherwise run the insert as a try-operation and treat a unique violation as "taken". Either way throw one
  exception type and don't load the existing record.
- **A child refers to a parent of another root only once the parent is committed.** Create the parent first,
  with an awaited try-operation.
- A derived read model (a report) is projected from memory when asked. It isn't stored or rebuilt.
- Without a database, creating identities and checking unique values wait for it; everything already in
  memory keeps working.

### Typed values over raw ones

Choose deliberately between holding an object and holding an identifier. When a value has a type, keep the
type: a content hash is a `ContentHash`, never a `string`. Hold the object when you need its behaviour and
its identity. Hold an identifier when the object may not be available, for example a file a client reports
that the server may not have.

### Assets are referred to by an asset reference

Refer to an asset (an image, an audio file, a recording) with one domain type, `AssetReference`, never a
`string` or `Uri`.

- It is either the relative path of a file the system stores under its data directory (`Banners/x.png`,
  valid on every machine) or an absolute http/https address of an asset hosted elsewhere, kept as given and
  never copied.
- The value is canonical: its parsing and normalization live on the type (a readonly record struct that
  implements `IParsable<T>` and `IFormattable`).
- File storages give out the reference of what they store. The hosts serve stored files under a fixed public
  path per asset directory (a static file provider rooted at each asset directory, never at the whole data
  directory) and only distribute them. They don't manage or list.

### Asynchronous I/O

- Use asynchronous I/O (`ReadAsync`, `WriteAsync`, `CopyToAsync`, `File.ReadAllBytesAsync`) when the API
  supports it. Open file streams with asynchronous I/O enabled when you read them asynchronously.
- Consume a `ChannelReader<T>` with
  `await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))`. Don't write a
  polling or wait loop.
- Don't make CPU-bound or small in-memory work `async` without a real benefit. Async does not mean zero
  copies or less RAM.
- In library code, every `await` uses `.ConfigureAwait(false)`, including `await using` and `await foreach`
  (see [csharp-style.md](csharp-style.md#async)).

Microsoft: [Asynchronous file I/O](https://learn.microsoft.com/dotnet/standard/io/asynchronous-file-i-o),
[System.Threading.Channels](https://learn.microsoft.com/dotnet/core/extensions/channels).

### Explicit resource lifetime

- Every stream, buffer, cache and handle has a clear owner who is responsible for releasing it.
- Release resources at the right moment with `using`, `await using`, `IDisposable` or `IAsyncDisposable`.
  Use the declaration form (`using var x = ...;`) unless a narrower scope matters. In library code, an
  `await using` takes the configured form shown in [csharp-style.md](csharp-style.md#async).
- When a method returns a stream, document in its contract that the caller owns it and must dispose it.
- Don't keep data or resources longer than their use needs.

Microsoft: [Implement a Dispose method](https://learn.microsoft.com/dotnet/standard/garbage-collection/implementing-dispose),
[Implement a DisposeAsync method](https://learn.microsoft.com/dotnet/standard/garbage-collection/implementing-disposeasync).

## When to hold the value anyway

Keep a value directly in RAM when:

- the data is small and accessed often;
- the algorithm needs all of it, or random access;
- a cache cuts the access cost significantly;
- an API or protocol requires a value representation;
- holding only a reference would add complexity without a real benefit.

Every exception comes from a business requirement, the data's access pattern, an API limit or performance
evidence. Don't add abstractions only to look compliant.

## Decision rule

Before you keep a value in RAM, decide what it is for:

1. If you only need to identify or reach the data, keep a reference.
2. If you need to process it, load it when needed and avoid unnecessary copies.
3. If you access it repeatedly, consider a cache with a stated purpose and lifetime.
4. If it can be processed in pieces, stream it.
5. If it needs I/O, prefer the asynchronous API.
6. Define who owns the data and the resources, and when they are released.
