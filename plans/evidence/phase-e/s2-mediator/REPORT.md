# Mediator v3 across module assemblies — spike report

**Date:** 2026-10-27 · **Plan item:** Phase E, blocker 2 (A4) · **Package:** `Mediator.SourceGenerator` 3.0.2
(`Mediator.Abstractions` 3.0.2) · **SDK:** 10.0.402 (`global.json` pins 10.0.301, `rollForward: latestFeature`)

**Question.** When `Basil.Application.Runtime` becomes one assembly per module, which arrangement of the Mediator
source generator still publishes an event declared in one module to a handler declared in another?

**Verdict.** Exactly one arrangement works: **the generator runs in the single assembly that calls `AddMediator`, and
that assembly references every module.** The generator does discover messages and handlers in referenced assemblies
(so the old premise of A4 is obsolete), but the generated code must be able to *name* the handler types, so handlers
must be `public` or `internal` with `InternalsVisibleTo` naming that one assembly. Every other arrangement fails to
build or builds and then finds nothing. The Phase E plan's fallback — `AddMediator` in the composing project — is
therefore the arrangement, not the fallback.

## 1. What the spike builds

`spikes/MediatorAssemblies` is a solution of five projects that mirrors the post-split shape:

| Project | Role | Contents |
|---|---|---|
| `Core` | shared events and eventing | `Event` base, `EventQueue`, `IEventDelivery`, `QueuedNotificationPublisher` (copies of `src/Application/Runtime/Events/`), 17 padding events |
| `ModA` | leaf module | `EventA`, `HandlerA` |
| `ModB` | module referencing `ModA` | `EventB`, `HandlerB`, `HandlerBOfA : INotificationHandler<EventA>` (a handler for another module's event) |
| `Host` | composition root | references all modules, calls `services.AddMediator(...)` with the repository's options |
| `Tests` | xunit v3 | publishes through `IPublisher`, drains the queue, asserts which handlers ran |

Variants are MSBuild properties, so one tree produces the whole matrix:

* `-p:SpikeVariant=1` — generator + `AddMediator` in `Host` only.
* `-p:SpikeVariant=2` — generator in `Core`, `ModA`, `ModB` and `Host`, `AddMediator` in `Host` only.
* `-p:SpikeVariant=3` — generator in `Core` only, `AddMediator` called from `Core`.

`-p:HandlerVisibility=public|internal|internal-ivt` makes the handlers public, internal, or internal with the declaring
module naming `Host` in `InternalsVisibleTo`.

The repository settings (`net10.0`, `Nullable`, `ImplicitUsings`, CPM versions) are inherited by importing the
repository's own `Directory.Build.props` from `spikes/MediatorAssemblies/Directory.Build.props`.

**Reproduce:** `spikes/MediatorAssemblies/run-variants.sh` rebuilds every combination (`-t:Rebuild`), runs the tests,
writes `evidence/v<n>-<visibility>.log` and the summary `evidence/matrix.tsv`.

The 17 padding events exist because Mediator only takes its dictionary path — and so only throws
`MissingMessageHandlerException` for an event it never saw — once the generating assembly sees **more than 16**
notifications (`CompilationModel.HasManyNotifications`). Below the threshold an unknown notification is **silently
dropped** (`ThrowInvalidNotification` returns when the argument is an `INotification`). The real application has far
more than 16 events, so the padding puts the spike on the real code path. (Verified both: the first run with 16
padding events silently returned; `evidence/v3-public.log` now shows the exception.)

## 2. Verdict table

`P` = publishes, handler ran. Tests per column: 6 (public) or 5 (internal\*, one test needs `HandlerA` visible).

| Variant | public handlers | internal handlers | internal + `InternalsVisibleTo("Host")` |
|---|---|---|---|
| **1** — generator + `AddMediator` in the composition root | **works** — 6/6 pass | **build fails** — `CS0122` | **works** — 5/5 pass |
| **2** — generator in every module, `AddMediator` once | **build fails** — `CS0121` + 368 `CS0436` | **build fails** — `CS0122` in `ModB` | **build fails** — `CS0122` in `ModB` |
| **3** — generator only in a shared base assembly | builds, **1/6 pass** — `MissingMessageHandlerException` | builds, **1/5 pass** | builds, **1/5 pass** |

Variant 1 public also proves, with passing tests, that:

* a handler in `ModB` for an event declared in `ModA` is found (`HandlerBOfA` ran);
* a handler registered by hand runs alongside the generated ones;
* a handler both generated and registered by hand runs **twice**;
* an event with no handler at all is accepted and does nothing;
* an event published through the **base `Event` type** — the way `EventPublisher` publishes — still reaches the
  handlers of its concrete type.

## 3. What each failure is, exactly

### 1 · internal handlers, no IVT

```
Host/obj/Debug/net10.0/Mediator.SourceGenerator/.../Mediator.g.cs(78,30): error CS0122:
'HandlerA' is inaccessible due to its protection level
'HandlerB' is inaccessible due to its protection level
'HandlerBOfA' is inaccessible due to its protection level
```

3 unique errors (18 log lines) — one per handler. The generator finds internal types without an accessibility filter,
then emits code in `Host` that instantiates them. Hand registration cannot rescue this: the assembly does not compile.

### 2 · generator in every module

Public handlers:

```
Host/Composition.cs(22,12): error CS0121: The call is ambiguous between the following methods or properties:
'Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator(...)'
```

plus 368 `CS0436` warnings in `Host`'s generated file (`NotificationHandlerWrapper<TNotification>`,
`ContainerMetadata`, `IContainerProbe`, `AssemblyReference`, the `Mediator` class itself, …) conflicting with the
*public* generated types that `Core` exported. A module that references the generator but never calls `AddMediator`
emits **public** types, because `GenerateTypesAsInternal` is read only from the `AddMediator` literal of that assembly.
The extension class itself is always generated in `Microsoft.Extensions.DependencyInjection` (not in the configurable
`Namespace`), so `Namespace` cannot remove the ambiguity — removing the generator from the other projects can.

Internal handlers (with or without IVT): `ModB`'s own generator sees `ModA`'s `HandlerA` and cannot name it:

```
ModB/obj/.../Mediator.g.cs(83,197): error CS0122: 'HandlerA' is inaccessible due to its protection level
```

IVT to `Host` does not help, because the code that needs access lives in `ModB`. In this arrangement *every* module
would need IVT to *every* other module it can reach — which is the whole graph.

### 3 · generator only in a shared base assembly

Builds cleanly, then at publish time:

```
Mediator.MissingMessageHandlerException : No handler registered for message type: ModA.EventA
Mediator.MissingMessageHandlerException : No handler registered for message type: ModB.EventB
```

All four behaviour tests fail, **including the one that registers a handler by hand** — a handler registration cannot
make an event publishable when the generator never saw the event, because the generated `Publish<T>` has no wrapper
to reach it. Only "an event that no handler observes is ignored" passes, for the wrong reason (the event is unknown,
so it is dropped or throws depending on the >16 threshold).

## 4. How discovery works in 3.0.2 (confirmed in source)

`CompilationAnalyzer.FindGlobalNamespaces` enqueues the generating assembly's global namespace **plus every referenced
assembly whose module transitively references `Mediator.Abstractions`** (breadth-first, depth ≤ 3,
`CompilationAnalyzer.cs` ~line 380–480). There is **no accessibility filter**: internal types of referenced assemblies
are discovered and then emitted into the generated file. That is why:

* discovery across modules works (variant 1, `HandlerBOfA`);
* accessibility, not discovery, is the real blocker;
* a module whose handlers are internal and whose IVT does not name the generating assembly breaks the build;
* 3-level transitive reach means the composition root should reference every module **directly** instead of relying
  on the graph: `Servers -> every module` happens to be deep enough today, but that is a coincidence, not a contract.

Generated code is emitted in **every** assembly that references the generator, whether or not it calls `AddMediator`
(`Mediator.g.cs` + `MediatorOptions.g.cs`). `AddMediator` registers the handlers, one wrapper per notification message,
the configured publisher (concrete type + `INotificationPublisher`) and the internal container metadata. Notification
handlers are resolved with `sp.GetServices<INotificationHandler<T>>()` when the mediator is first constructed, so
registrations made after `AddMediator` still count, and a hand-registered handler of a **seen** event is included.

## 5. Options that matter

| Option | Effect observed |
|---|---|
| `ServiceLifetime = Singleton` | Must equal the compile-time value in the generating assembly. `AddMediator` throws: *"Generated code for 'Singleton' lifetime, but got 'Scoped' lifetime from options. This means that the source generator hasn't seen the 'AddMediator' method call during compilation. Make sure that the 'AddMediator' method is called from the project that references the Mediator.SourceGenerator package."* Keep it in the composition root only. |
| `NotificationPublisherType = typeof(QueuedNotificationPublisher)` | The generating assembly must be able to **name the type** — see the extra run `evidence/v1-public-no-publisher-ivt.log`: without `InternalsVisibleTo`, `Host`'s generated file fails with `error CS0122: 'QueuedNotificationPublisher' is inaccessible due to its protection level` (3 sites). So the type stays `internal` **and** the declaring project adds IVT to the composition root, or it becomes `public`. |
| `GenerateTypesAsInternal = true` | Only read from the `AddMediator` literal in that assembly. It makes the generated types internal to the composition root; it does not influence whether a *referenced* module's generated types are internal (variant 2's `CS0436`). |
| `Namespace` | Renames the generated `Mediator` class namespace. The `AddMediator` extension is always in `Microsoft.Extensions.DependencyInjection`, so it cannot repair variant 2's `CS0121`. Not needed for the recommended arrangement; the spike leaves it at its default. |
| `MSG0005` | `warning MSG0005: MediatorGenerator found message without any registered handler: Core.FillerEventNN` — one per notification of the generating assembly's reference graph that has no handler (17 unique here, each logged twice by the build). It is informational; an event with no handler is legal (it just does nothing). Do **not** suppress it globally: it is the only signal that a handler silently disappeared during the module moves. |
| `Assemblies` | Exists (compile-time `AssemblyReference` list) to replace the reference scan. Not needed here: the composition root references the modules anyway, and the generated code must still compile against the handler types. Not exercised. |

## 6. Hand registration: what it can and cannot do

Tested in variant 1 (passing):

* a hand-registered handler for a **seen** event runs alongside the generated ones;
* a handler the generator also found and that is also registered by hand runs **twice** (two `GetServices` results);
* a hand-registered handler for an **unseen** event cannot work (variant 3: the publish throws before the handler is
  reached).

Consequence for `Basil.Bot`, which hand-registers `ChatHandler`, `AnticheatAnnouncer` and one `RoomAnnouncer` per
event: hand registration is fine, but **the composition root that runs the generator must not reference `Basil.Bot`**,
otherwise the generator sees those handlers, requires them to be visible (`CS0122` unless Bot grants IVT), and — once
visible — registers a second copy that runs alongside the hand-registered one. `Basil.Bot` keeps its own
`DependencyInjection.cs` as it is today and the host calls `AddBot()` itself.

## 7. Recommended arrangement for Phase E

**One generator, in one composition-root project, which references every module.**

```text
Basil.<Module>.Application      -> Basil.Common.Application (etc.), Mediator.Abstractions
                                   handlers internal, InternalsVisibleTo("Basil.Application")
                                   DependencyInjection.cs: registers the module's objects only

Basil.Application               -> every Basil.<Module>.Application
   (composition root)              Mediator.Abstractions + Mediator.SourceGenerator (PrivateAssets="all")
                                   calls AddMediator once

Basil.Host                      -> Basil.Application, Basil.Bot, Infrastructure...
                                   calls AddApplication() and AddBot(); no generator, no AddMediator
```

Why not `Basil.Host` as the generator's assembly: `tests/Support/Basil.Testing` composes through `AddApplication()`
and does not reference the host; making the host the owner would force every module to grant IVT to a *host* and force
the test support project to reference the host (inverted layering). The composition root is what both the hosts and
the test support project can reference.

### 7.1 Project files

Composition root (`src/Application/Basil.Application.csproj` — name is the owner's call, the role is what matters):

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<!-- Every module, directly: discovery walks project references, but relying on a transitive path
		     (e.g. Servers -> every module) is a coincidence, not a contract. -->
		<ProjectReference Include="..\Common\Basil.Common.Application\Basil.Common.Application.csproj" />
		<ProjectReference Include="..\Auth\Basil.Auth.Application\Basil.Auth.Application.csproj" />
		<!-- ... one per module ... -->
	</ItemGroup>
	<ItemGroup>
		<PackageReference Include="Mediator.Abstractions" />
		<!-- The only project in the repository that references the generator. -->
		<PackageReference Include="Mediator.SourceGenerator" PrivateAssets="all" />
		<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
	</ItemGroup>
</Project>
```

Every module (`src/Modules/<Module>/Basil.<Module>.Application.csproj`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<ItemGroup>
		<!-- The generated code lives in Basil.Application and instantiates this module's handlers. -->
		<InternalsVisibleTo Include="Basil.Application" />
	</ItemGroup>
	<ItemGroup>
		<PackageReference Include="Mediator.Abstractions" />
		<!-- Never Mediator.SourceGenerator: a second generator anywhere in the graph is variant 2. -->
	</ItemGroup>
</Project>
```

The project that declares `QueuedNotificationPublisher` (`Basil.Common.Application`, today
`src/Application/Runtime/Events/`) also needs `<InternalsVisibleTo Include="Basil.Application" />`, because the
composition root names the type in `options.NotificationPublisherType` and in the generated registration.

`Basil.Bot.csproj` keeps `Mediator.Abstractions` and gains nothing else; it must **not** be referenced by
`Basil.Application`.

If the owner's blocker-1 policy ("no IVT to make a build green") forbids these IVT lines, the alternative is `public`
handlers — but a public handler whose constructor takes an internal collaborator (`RoomChannels`, `Lobby`, …) produces
`CS0051`, so the module's internal surface cascades public. That is a broader change than one IVT line per module.

### 7.2 Composition code

```csharp
public static IServiceCollection AddApplication(this IServiceCollection services)
{
	services.AddEventing();          // EventQueue, EventPublisher, IEventPublisher<T> categories
	services.AddCommon();            // = each module's registration
	services.AddAuth();
	// ... every module ...

	services.AddMediator(options =>
	{
		options.ServiceLifetime = ServiceLifetime.Singleton;
		options.GenerateTypesAsInternal = true;
		options.NotificationPublisherType = typeof(QueuedNotificationPublisher);
	});

	return services;
}
```

This is today's `AddEvents()` with the `AddMediator` call moved out of the module assembly and into the composition
root; nothing else about `EventPublisher`, `EventQueue` or `IEventPublisher<T>` changes. `EventPublisher` keeps
`Lazy<IPublisher>` — publishing through the base `Event` type is covered by a passing spike test.

### 7.3 What each module's `DependencyInjection.cs` registers

* its own runtime objects, registries, contexts and root objects;
* its repositories/storage ports are registered by its Infrastructure project, as today;
* **not** `AddMediator`;
* **not** its `INotificationHandler<T>` types — the generator registers them;
* the module's `IEventPublisher<T>` category registrations stay in Common (`AddCategory<T>`), as today.

Hand registration is reserved for handlers in an assembly the composition root does not reference (today: `Basil.Bot`).

## 8. Risks to carry into the migration

1. **One `AddMediator` per container.** A second call (for example a module test project that composes on its own) adds
   a second `Mediator` registration and a second set of wrappers. With only one generator in the graph the duplicate
   `AddMediator` extension does not exist, but the DI duplicates do.
2. **Tests that add fake handlers.** A fake registered for an event whose real handler exists is an *additional*
   observer; it does not replace the real one. Assertions such as "the handler ran once" must be written against the
   real handler or count only the fake's own log.
3. **Bot's hand registrations** work only because `Basil.Application` does not reference `Basil.Bot`. Keep that edge
   absent; if the host instead makes Bot a module of the composition root, Bot's handlers must drop
   `AddSingleton<INotificationHandler<…>>` and become generated (and `RoomAnnouncer`'s shared-instance trick needs a
   redesign, because the generator registers one instance per handler interface).
4. **Every handler type in the generator's reference graph must be visible** to the composition root. A new module
   that forgets the IVT line fails the *composition root's* build with `CS0122`, not its own — the error points at
   `Basil.Application/obj/.../Mediator.g.cs`, which is confusing until this report is known.
5. **`MSG0005` will be noisy** (one per unhandled event). Leave it visible; suppress it only in the composition root
   after listing the intentional cases.
6. **`PrivateAssets="all"` on the generator** is required, otherwise the package flows transitively and a consumer can
   end up generating a second copy (variant 2's failure mode).

## 9. Evidence

| File | Contents |
|---|---|
| `evidence/matrix.tsv` | one line per combination: build result and test summary |
| `evidence/v1-public.log`, `v1-internal.log`, `v1-internal-ivt.log` | variant 1 |
| `evidence/v2-*.log` | variant 2 (`CS0121`, `CS0436`, `CS0122`) |
| `evidence/v3-*.log` | variant 3 (`MissingMessageHandlerException`) |
| `evidence/v1-public-no-publisher-ivt.log` | publisher type unreachable → `CS0122` |

Run the whole matrix with `./run-variants.sh` from `spikes/MediatorAssemblies`.
