# Organizing code

**Organize by responsibility, group by concept, share by necessity. Keep the same boundaries in
directories, namespaces, dependencies and APIs.**

The aim is code you can find, change and delete one feature at a time. The test of a good structure: when a
concept changes, its changes stay in one place, and when a concept is removed, one folder goes.

## Contents

- [Principles](#principles)
- [Layers and features](#layers-and-features)
- [Features](#features)
- [Common](#common)
- [Infrastructure](#infrastructure)
- [Category and hierarchy](#category-and-hierarchy)
- [Files, folders and names](#files-folders-and-names)
- [Dependencies](#dependencies)
- [APIs and namespaces](#apis-and-namespaces)
- [Deciding where something goes](#deciding-where-something-goes)
- [Keeping it healthy](#keeping-it-healthy)

## Principles

- **Feature first.** Group code by feature or business domain, not by technical kind of file.
- **Local by default.** A piece belongs to the feature responsible for it.
- **Common by necessity.** Move something to `Common` only when several features genuinely share it and
  it means the same thing to each of them.
- **Hierarchy by responsibility.** Nest domain, category and subcategory when each level is a meaningful
  group of concepts.
- **No flattening.** Don't put pieces with different responsibilities into one folder, namespace, interface or
  API just because they live in the same project.
- **Explicit boundaries.** Every folder, namespace, module and API has a clear scope.
- **Minimal abstraction.** Don't create an abstraction, base class, module or classification level only to
  avoid a little duplication.
- **Consistent concepts.** Folders, namespaces, types and APIs show the same conceptual boundaries.

## Layers and features

Use layers only where a dependency direction requires them, and then make each layer a **project**:
for example Domain, then Application, then Infrastructure, then hosts. Inside every project, slice by
**feature**. The same feature names recur in each layer, so one concept is easy to follow from model to
storage to service to transport:

```text
src/Domain/Users/                 User, Restriction, Permissions
src/Application/Storage/Users/    IUserRepository, UserQuery
src/Application/Services/Users/   IUserService, results
src/Infrastructure/Storage/Users/ PostgresUserRepository
```

The full layer map, what each layer may hold, and how service events reach handlers are in
[architecture.md](architecture.md).

A small application without separate layer projects can use a single project. The tree below is an
illustration, not a template. Create only the folders you need.

```text
Project/
├── Features/
│   ├── Orders/
│   │   ├── Payment/    Payment.cs, PaymentService.cs, ...
│   │   └── Shipment/   Shipment.cs, ShipmentService.cs, ...
│   └── Authentication/
├── Common/
│   ├── Orders/Payment/   (only what is truly shared)
│   └── Networking/
├── Infrastructure/
│   ├── Persistence/
│   └── Networking/
└── Program.cs
```

The namespace is the project name plus the folder path (`Shop.Application.Storage.Users`). It should not
mechanically copy a path that adds no meaning, but it must never contradict the folder structure.

Microsoft: [Develop ASP.NET Core MVC apps: structuring the application](https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/develop-asp-net-core-mvc-apps#structuring-the-application)
(feature folders instead of folders per file type),
[Common web application architectures](https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)
(layers as projects; why `Models/` and `Services/` folders scatter business logic).

## Features

- A feature holds its own logic, models, handlers and services. Code sits near the code that uses it.
- Divide features by real features or business domains, not by individual classes or files.
- A large feature splits into categories or sub-features.
- Other features don't reach into a feature's internals unless they need to. Mark internals `internal`.
- Features don't all need the same internal shape. The structure follows the need, not symmetry.
- Don't create `Core`, `Base` or `Shared` inside a feature until it has an independent responsibility.
- Inside a feature folder, an interface and its implementation, or several small closely related types, may
  share a file. Rigid one-class-per-file rules don't apply there.

## Common

### What belongs there

`Common` holds what several features or modules truly share:

- abstractions and contracts with a shared scope, such as `PageRequest`, `Page<T>` and `Interval<T>`, which
  every repository uses;
- shared data types and concept models;
- utilities and extension members with a precise responsibility (`TextExtensions`, `EnumExtensions`);
- shared logic that has the same meaning and business rule everywhere it is used.

Don't move code to `Common` because:

- it might be reused one day;
- two pieces of code happen to look alike;
- you want to remove every trace of duplication;
- you don't know where else to put it;
- many places call it, while it really belongs to one feature or service.

### How it is organized

- `Common` is itself split by category or by the domain its contents serve. It is never one namespace that
  collects every shared type and helper of the project.
- If shared pieces belong to different concepts, group them separately. Don't group them by file type.
- Nest further when a category holds several independent responsibilities.
- Don't create a category in `Common` only to mirror `Features`.

Example: a shared type of `Orders/Payment` doesn't go next to the shared types of `Orders/Shipment` just
because both belong to orders.

A loose file at a module's root is fine for a helper that several feature folders in that module use.
Code so widely shared that moving it into a feature folder would only add namespace churn can also stay at
the root.

## Infrastructure

- Infrastructure holds implementations that depend on technology or external systems: databases, the file
  system, networks, third-party services.
- Group it by responsibility, or by technology where that reads better.
- Don't move every shared piece into Infrastructure, and never put business logic there because many
  features use it.
- Separate a contract from its implementation when the architecture requires that boundary. Don't create
  an abstraction only for form.
- Group implementations by **what they store or serve**, not by medium. File-backed storages sit next to the
  data they handle (`Recordings/FileRecordingStorage.cs`, `Help/FileHelpArticleStorage.cs`), not in a `Files/`
  folder. Internal plumbing that every storage shares (database access, options, write queues) goes under the
  module's `Common/`.

## Category and hierarchy

- **Concept before file type.** Group by domain and category before you group by `Models`, `Services`,
  `Handlers` or any other technical kind.
- **Separate distinct concepts.** Independent business concepts get their own scope when the distinction
  matters.
- **Nested categories are fine** when each level is a meaningful boundary of responsibility.
- **No artificial hierarchy.** Don't create folders for symmetry, or a folder per file.
- **Keep together what changes together**, as long as that doesn't blur a boundary.

Prefer this:

```text
Features/Orders/Payment/
Features/Orders/Shipment/
```

over this:

```text
Features/Orders/
├── PaymentService.cs
├── ShipmentService.cs
├── PaymentModel.cs
└── ShipmentModel.cs
```

This doesn't ban every `Services/` or `Models/` folder. One is reasonable when its scope is small, clear and
useful. A UI feature split into `Views/`, `ViewModels/` and `Models/`, for example, is fine.

## Files, folders and names

- **A small type used by one type lives in that type's file**, for example `ConnectionType` in `Connection.cs`.
  Nested composite types stay inside their parent.
- **A folder name is plural when its singular would collide with a type inside it.** Use `Invoices/Invoice.cs`
  rather than `Invoice/Invoice.cs`, which makes a namespace/type name collision (CS0118).
- **A class name has at most three words.** Use `ExportQueue` or `RetryPolicy`, not
  `ReportExportJobExecutionManager`. A long name means the type does too much or its name repeats its
  namespace.
- **Names state the business meaning or responsibility.** Avoid `Misc`, `Stuff`, `Helpers`, `Manager`,
  `Processor` and `Util` unless the scope is precise. `TextExtensions` is precise, `Helpers` is not.
- **No folder holds a single file** unless that file marks a real boundary.
- **Never commit empty placeholder folders.** Create the folder when the feature is built.
- **Never nest `Common`, `Shared` or `Core` folders** without distinct responsibilities.
- **Prefer a shallow, navigable structure.** Go deeper only when depth clarifies a boundary.
- **Each module registers itself in one place at its root**, with one static class and an `AddX()` method per
  module. Name it `DependencyInjection` (`AddApplicationServices()`).
- **Bind configuration only at the composition root.** A module receives `IOptions<TOptions>` of an options
  type it owns. Core layers read no configuration. Settings changed at runtime are domain data, not
  configuration.

## Dependencies

- Features may use the public parts of `Common`. `Common` never depends on a specific feature.
- Avoid dependency cycles between features and between modules.
- A feature never reaches into another feature's internal implementation. When features must talk, define
  the contract or the communication mechanism (events, for peers; see [design.md](design.md)).
- `Common` is never the place that coordinates features.
- A shared piece that grows its own responsibility and lifecycle becomes its own module.
- Implementations are joined through dependency injection by their contracts. A consumer never references an
  implementation project to use it. The composition root references implementations only to register them.
- These rules govern real dependencies, not only file locations. Enforce the important ones with
  architecture tests.

Microsoft: [Architectural principles](https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/architectural-principles),
[Dependency injection guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines),
[Options pattern](https://learn.microsoft.com/dotnet/core/extensions/options).

## APIs and namespaces

- An API reflects the categories and business boundaries behind it. Don't build one giant interface,
  service, controller or endpoint group that holds operations on unrelated concepts.
- Give `Payment` operations and `Shipment` operations their own scope when they are independent concepts.
- **Split a service whose object has parts into one child service per part.** Expose each child as a
  property of the parent contract (`IRoomService.Participants`, `.Settings`, `.Recordings`). Callers reach
  children through the parent (`rooms.Participants.KickAsync(room, participant, by)`) and the children share
  the parent's event stream. A service with no parts stays flat, however long; size alone is no reason to
  split (see [design.md](design.md#child-services-by-part)).
- Don't gather models, requests, responses or contracts into one namespace because they share a project.
- Group endpoints by resource, use case or business area.
- Type and API names show which category they belong to. A neatly divided folder tree behind a public
  API that dumps everything into one scope is still flattening.

## Deciding where something goes

When you add a component:

1. Name the business responsibility it serves.
2. If it serves one feature, put it in that feature.
3. If it belongs to a specific category, put it in that category, not in the shared parent.
4. If several features truly share it with the same meaning, put it in the matching category of `Common`.
5. If it depends on an external system, put it in Infrastructure, or in the module that implements it.
6. If features must communicate, define the contract or the mechanism explicitly.
7. Check that the new structure creates no dependency cycle, no overly broad namespace and no flattened API.
8. While no classification need is clear yet, keep it simple. Add a level only when it is needed.

## Keeping it healthy

- When something in `Common` serves only one feature, move it back to that feature.
- When `Common` collects unrelated responsibilities, split it by category or module.
- When a feature grows too big, split it into categories or sub-features. Don't keep adding files at one level.
- When features depend tightly on each other, revisit the business boundaries and the contracts.
- Independent change, findability and clear responsibility matter more than fewer duplicated lines.
- A structural change keeps behaviour identical and introduces no unintended reverse dependency. Moving
  files means the callers, registrations and namespaces follow in the same change.
