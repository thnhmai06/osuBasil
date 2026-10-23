# C# style

This file covers how code is *expressed*: syntax, conditions, expressions, loops, members. Where code goes
is in [organization.md](organization.md). What it should do is in [design.md](design.md).

## Contents

- [Order of priority](#order-of-priority)
- [Formatting belongs to the IDE](#formatting-belongs-to-the-ide)
- [Conditional expressions and if](#conditional-expressions-and-if)
- [Pattern matching](#pattern-matching)
- [LINQ and collections](#linq-and-collections)
- [Null operators and short forms](#null-operators-and-short-forms)
- [Types and members](#types-and-members)
- [Async](#async)
- [Exceptions in code](#exceptions-in-code)
- [Researching a new convention](#researching-a-new-convention)

## Order of priority

When several ways of writing something are equivalent, choose in this order:

1. **Correct.** It keeps the meaning and the behaviour.
2. **Clear.** It is easy to read.
3. **Consistent.** Equivalent cases are written the same way.
4. **Idiomatic and concise.** It uses the language's features and avoids verbose syntax.

Don't change syntax only to save lines if the result is harder to read. Don't force one syntax on cases whose
logic differs.

When you convert between `if`, early return and `?:`, keep the following unchanged. If you can't guarantee
all of it, keep the more explicit form.

- the returned result;
- the order in which expressions are evaluated;
- how many times methods with side effects are called;
- the exception flow and the nullability checks;
- the business behaviour.

## Formatting belongs to the IDE

The IDE's formatter decides layout: brace placement, line wrapping, blank lines, member order, expression
or block bodies, parentheses in mixed `&&`/`||`, and named literal arguments. thnhmai06 runs the IDE
cleanup (Rider) on the code. It applies the repository's `.editorconfig` and IDE settings.

- Don't hand-format to your own taste, and don't "fix" layout the formatter produced.
- Format only the code you touch: use the IDE's reformat or cleanup, or `dotnet format` on those files. Keep
  formatting churn out of code you didn't change.
- A repository without an `.editorconfig` follows Microsoft's defaults: the C# coding conventions, 4-space
  indentation, Allman braces, file-scoped namespaces, `using` directives outside the namespace. A good start
  is the [`dotnet/docs` `.editorconfig`](https://github.com/dotnet/docs/blob/main/.editorconfig).

Microsoft: [Common C# code conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions),
[C# identifier naming rules and conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/identifier-names),
[Code-style rules](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/).

## Conditional expressions and if

### Use `?:` for a simple choice between two values

When a condition only chooses one of two values or results, use a conditional expression, as long as it
stays readable.

```csharp
return user is not null ? CreateProfile(user) : null;
```

rather than

```csharp
if (user is not null)
{
    return CreateProfile(user);
}
else
{
    return null;
}
```

and rather than splitting the same choice into a guard and a return:

```csharp
if (order is null)
    return null;

return $"{order.CustomerName} - {order.Total}";
```

A method that only chooses between two results is one `?:`:

```csharp
return order is not null ? $"{order.CustomerName} - {order.Total}" : null;
```

The early-return form is for a method that does work after the check (below), not for a single choice.

### Put the normal case in the `?` branch

When a conditional expression returns a result, the normal case goes in the `?` branch and the fallback in
the `:` branch, if the method's contract allows it.

```csharp
return fileName is not null ? await storage.OpenAsync(fileName, cancellationToken).ConfigureAwait(false) : null;
```

not

```csharp
return fileName is null ? null : await storage.OpenAsync(fileName, cancellationToken).ConfigureAwait(false);
```

This is a presentation convention of thnhmai06, not a rule of C#. Older code may still put the fallback
first. Follow the convention in new code, and in code you are already changing.

### Use an early return to separate a flow

When a method must check its input or deal with a special case before its main logic, use `if` with an
early return. Don't squeeze the whole method into one `?:`.

```csharp
if (user is null)
    return null;

ValidateUser(user);
UpdateLastAccess(user);

return CreateProfile(user);
```

Merge guards that return the same result into one guard:

```csharp
if (room is null || !room.IsOpen)
    return JoinResult.RoomNotFound;
```

### Keep one style within a chain

When a chain of `if` and early `return` above already makes **the same kind of decision**, write the last
branch as another `if`, not as a trailing `?:`. "The same kind" means the condition plays the same role as
the ones above, such as another tier of the same choice.

Inconsistent: the last condition is another tier of the same discount choice.

```csharp
if (IsVip)
    return VipDiscount;

return IsBig ? BigOrderDiscount : 0m;
```

Consistent:

```csharp
if (IsVip)
    return VipDiscount;

if (IsBig)
    return BigOrderDiscount;

return 0m;
```

A trailing `?:` is fine when the `if`s above do a different job, for example guards that reject invalid
input. The final choice between two values is then a step of its own:

```csharp
if (user is null)
    return null;

return user.IsActive ? CreateProfile(user) : CreateFallbackProfile();
```

Decide from the structure of the whole method, not line by line. When the whole method is only a choice
between two results, start with `?:`.

### Don't force `?:`

When the branches contain several statements or separate logic, use `if`/`else`:

```csharp
if (isValid)
{
    ApplyChanges();
    return CreateResult();
}

LogFailure();
return CreateError();
```

Never write `condition ? true : false`, or `condition ? false : true`. Write `condition` or `!condition`.

## Pattern matching

Prefer patterns to chains of comparisons and casts:

```csharp
if (room is { IsLocked: false, Presenter: not null })
    ...

if (asset is not (AssetKind.Thumbnail or AssetKind.Audio))
    return null;

if (e.UnsavedRecording is { } recording)
    await recordings.CreateOrUpdateAsync(recording, cancellationToken).ConfigureAwait(false);
```

Use a `switch` expression to map one value to another. Use a `switch` statement with type patterns to
dispatch actions over a category of events.

## LINQ and collections

### Use LINQ for simple data transformation

When the point is to filter, project, sort, group or aggregate, use LINQ if it reads well:

```csharp
var activeNames = users
    .Where(user => user.IsActive)
    .Select(user => user.Name);
```

### Don't force LINQ onto complex logic

If the pipeline has nested lambdas, tangled conditions or state that is hard to follow, use `foreach` or
another more explicit form. The goal is clear intent, not turning every loop into LINQ.

### Don't hide side effects in LINQ

`Where`, `Select` and the other operators express data transformation. Use a loop for actions.

```csharp
// No
users.Where(user =>
{
    user.IsProcessed = true;
    return user.IsActive;
}).ToArray();

// Yes
foreach (var user in users)
    user.IsProcessed = true;

var activeUsers = users.Where(user => user.IsActive);
```

### LINQ is not concurrency

Plain LINQ processes nothing in parallel. Write sequential work as a loop:

```csharp
foreach (var user in users)
    await ProcessAsync(user, cancellationToken).ConfigureAwait(false);
```

Write concurrent work explicitly. The concurrency comes from starting all the tasks before awaiting them,
not from `Select`:

```csharp
await Task.WhenAll(users.Select(user => ProcessAsync(user, cancellationToken))).ConfigureAwait(false);
```

### Don't materialize out of habit

Don't add `ToList()` or `ToArray()` without a reason: a snapshot, enumerating several times, or an API that
needs a concrete collection.

```csharp
foreach (var user in users.Where(user => user.IsActive))
    Process(user);
```

### Use collection expressions

When the language version and the target type allow it, use collection expressions:

```csharp
int[] values = [1, 2, 3];
int[] combined = [.. first, .. second];
return Task.FromResult<IReadOnlyList<string>>([.. names.Order(StringComparer.OrdinalIgnoreCase)]);
```

Don't apply this mechanically when the target type or the context makes another form clearer.

## Null operators and short forms

Use the direct syntax when it doesn't hurt clarity:

```csharp
var displayName = user.DisplayName ?? "Unknown";
var profileName = user?.Profile?.Name;
items ??= [];
var name = value ?? throw new ArgumentNullException(nameof(value));
```

Don't replace a simple expression with a longer chain of checks unless that really reads better.

## Types and members

### Records or classes: by role

- **Data that is passed around** (DTOs, events, results, value objects, query records) is an immutable
  `record`.
- **A live entity whose state changes** is a class. Its properties have setters that assign and validate,
  and you change it in place on the instance. Don't create a copy-with method (`WithTitle(...)`) to change a
  live object.
- **A service** is a `sealed class`, or `internal sealed` when only dependency injection creates it.
- A class not designed for inheritance is `sealed`.

### Properties and methods

These follow Microsoft's property design guidelines and thnhmai06's setter rule.

- A property is a smart field. A getter is cheap, has no preconditions and doesn't throw. A getter that
  could throw should be a method.
- **A setter only stores the value. It throws if the value is invalid and keeps the previous value when it
  throws.** It never emits an event or changes other state. A change with consequences is a method that
  performs a real operation (see [design.md](design.md#setters-and-operations)). Restrict the setter's
  visibility so callers can't go around that method.
- Use a get-only property when callers shouldn't change the value. Never write a set-only property, or a
  setter more visible than its getter.
- Don't provide settable collection properties.
- A member that uses no instance state is `static`. A class whose members all end up static is a
  `static class`.
- A forwarding property derives a value instead of copying it: `public string Title => Meeting.Title;`.
- **Object-oriented, not procedural.** An operation goes through the object that owns the state it changes
  (`storage.SaveAsync`, `room.IsManagedBy(user)`), not through a static helper that is handed the state.
- **A type that is a collection implements the matching BCL interface** (`DataStorage :
  IReadOnlyDictionary<string, FileInfo>`). A business value with a text form is a value type that implements
  `IParsable<T>` and `IFormattable` (`AssetReference`).
- **Every stored time is UTC** (`TimeProvider.GetUtcNow()`, `DateTimeOffset.UtcNow`, `timestamptz` columns).
  Only the presentation converts it to a local date and time. Never `DateTimeOffset.Now` or `DateTime.Now`.
Microsoft: [Property design](https://learn.microsoft.com/dotnet/standard/design-guidelines/property),
[Member design](https://learn.microsoft.com/dotnet/standard/design-guidelines/member).

### Constructors and declarations

- Use primary constructors for services and other classes that only capture dependencies:
  `internal sealed class EventPump<T>(IEventPublisher<T> source, ILogger<EventPump<T>> logger)`. Parameters
  are camelCase. Record primary constructor parameters are PascalCase.
- For data types, prefer a positional record or `required`/`init` properties to a long hand-written
  constructor. A type that must guarantee its own validity checks its values in the record's constructor or
  in its `init` accessors.
- Use file-scoped namespaces, with `using` directives outside the namespace.
- Use C# 14 extension blocks (`extension(IServiceCollection services) { ... }`) for sets of extension
  members, including the module's `AddX()` registration.

### Small things

- Use `#region` and `#endregion` to separate logical sections inside a long file. Don't use `//` banner
  comments for that.
- Write no redundant casts.
- Write no discarded-task lambdas. Use `_ => action()`, not `_ => _ = action()`.
- Use `Func<>` and `Action<>` instead of custom delegate types (Microsoft convention).
- Use raw string literals for multi-line text, and interpolation for short concatenation.
- Use the language keywords for types (`string`, `int`), not the CLR names.
- Name with full words, not abbreviations: `Privilege`, not `Priv`. Prefer readability over brevity,
  following Microsoft's naming guidelines. Names don't repeat the type (`From`, not `FromUserId`, when it
  holds a reference). See [design.md](design.md#naming).

## Async

- Use `async`/`await` for I/O-bound work. Return `ValueTask` where the surrounding code does, typically for
  hot paths and interface methods that often complete synchronously.
- **In library code, every `await` uses `.ConfigureAwait(false)`.** That includes `await using` and
  `await foreach`, which CA2007 also reports (verified with the .NET 10 SDK). "Library code" means every
  project that isn't the executable host or UI. The host and UI layers don't need it.

  ```csharp
  await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

  var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
  await using (file.ConfigureAwait(false))
  {
      ...
  }

  await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
      ...
  ```

  `ConfigureAwait` on a disposable returns a wrapper, so declare the resource first and configure it in the
  `await using`. The declaration form `await using var _ = file.ConfigureAwait(false);` also works, when the
  resource lives to the end of the method.
- `CancellationToken cancellationToken = default` is the last parameter of every asynchronous public member,
  and you pass it through to every call.
- Catch cancellation only when it is yours:
  `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)`.

Microsoft: [CA2007](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2007),
[Task-based asynchronous pattern](https://learn.microsoft.com/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap).

## Exceptions in code

How a member reports failure is a design decision; see [design.md](design.md#errors). In code:

- Catch only what you can handle. Don't catch `Exception` without a filter or a clear "log and continue"
  boundary, such as an event pump that must survive a failing handler.
- Throw specific exception types with useful messages. Validate arguments with the guard helpers
  (`ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfNegative`).
- Never throw from an exception filter. Avoid throwing explicitly from `finally`.

Microsoft: [Exception throwing](https://learn.microsoft.com/dotnet/standard/design-guidelines/exception-throwing),
[Using standard exception types](https://learn.microsoft.com/dotnet/standard/design-guidelines/using-standard-exception-types).

## Researching a new convention

The rules above are not complete. When a style choice is unclear:

1. Identify the language version and the target framework.
2. Check `.editorconfig`, compiler diagnostics, IDE inspections and analyzers.
3. Look up the official documentation: Microsoft's C# coding conventions and Framework Design Guidelines
   first. [microsoft-docs.md](microsoft-docs.md) lists them by topic.
4. Compare the equivalent forms for clarity, consistency and fit with the language.
5. Generalize the answer into a principle with an example, and propose it as an addition (see "When
   something isn't covered" in SKILL.md).

Never claim that a convention is a language or IDE requirement unless you verified it.
