using System.Text;

namespace InternalAccess;

internal static class Program
{
	internal static readonly string[] ModuleOrder =
	[
		"Anticheat", "Beatmaps", "Chat", "Common", "Content", "Events", "Multiplayer", "Scores", "Servers",
		"Users", "Root(Runtime)", "Root(Capabilities)", "Generated"
	];

	private static int Main(string[] args)
	{
		var root = FindRoot();
		var runtime = FindAssembly(root, "src/Application/Runtime", "Basil.Application.Runtime.dll");
		var capabilities = FindAssembly(root, "src/Application/Capabilities", "Basil.Application.Capabilities.dll");

		Console.WriteLine($"runtime      : {runtime}");
		Console.WriteLine($"capabilities : {capabilities}");

		using var runtimeModel = new AssemblyModel(runtime);
		using var capabilitiesModel = new AssemblyModel(capabilities);

		var scanner = new Scanner(runtimeModel, capabilitiesModel);
		scanner.Scan(runtimeModel, Scanner.ModuleOf);
		scanner.Scan(capabilitiesModel, Scanner.ModuleOf);

		// Assemblies outside the two scanned ones that reference them: Bot, the test support library, and tests.
		var externals = new List<(AssemblyModel Model, string Label)>();
		foreach (var (path, label) in ExternalAssemblies(root))
		{
			try
			{
				externals.Add((new AssemblyModel(path), label));
			}
			catch (Exception exception)
			{
				Console.WriteLine($"skipped {path}: {exception.Message}");
			}
		}

		foreach (var (model, label) in externals)
			scanner.Scan(model, (_, _) => label);

		var report = Report.Build(root, scanner, externals.Select(x => x.Label).ToList());
		var output = Path.Combine(root, "spikes/InternalAccess/report.md");
		File.WriteAllText(output, report);

		Console.WriteLine();
		Console.WriteLine($"methods scanned: {scanner.MethodsScanned}, hits: {scanner.Hits.Count}");
		Console.WriteLine($"report: {output}");
		if (scanner.Ambiguities.Count > 0)
		{
			Console.WriteLine($"unresolved references: {scanner.Ambiguities.Count}");
			foreach (var line in scanner.Ambiguities.Distinct().Take(40)) Console.WriteLine("  " + line);
		}

		foreach (var line in scanner.Failures.Distinct()) Console.WriteLine("  failure: " + line);
		return 0;
	}

	private static string FindRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Basil.slnx")))
			directory = directory.Parent;
		return directory?.FullName ?? Directory.GetCurrentDirectory();
	}

	internal static string FindAssembly(string root, string project, string fileName)
	{
		var directory = Path.Combine(root, project, "bin");
		var candidates = Directory.Exists(directory)
			? Directory.GetFiles(directory, fileName, SearchOption.AllDirectories).ToList()
			: [];
		if (candidates.Count == 0)
			throw new FileNotFoundException($"No build of {project} under {directory}. Build it first.");
		return candidates.OrderByDescending(File.GetLastWriteTimeUtc).First();
	}

	private static IEnumerable<(string Path, string Label)> ExternalAssemblies(string root)
	{
		foreach (var (project, file, label) in new[]
		         {
			         ("src/Bot", "Basil.Bot.dll", "Basil.Bot"),
			         ("tests/Support/Basil.Testing", "Basil.Testing.dll", "Basil.Testing"),
			         ("tests/Application/Runtime", "Basil.Application.Runtime.Tests.dll",
				         "Basil.Application.Runtime.Tests"),
			         ("tests/Bot", "Basil.Bot.Tests.dll", "Basil.Bot.Tests"),
			         ("tests/Infrastructure/Runtime", "Basil.Infrastructure.Runtime.Tests.dll",
				         "Basil.Infrastructure.Runtime.Tests"),
			         ("tests/Architecture", "Basil.ArchitectureTests.dll", "Basil.ArchitectureTests")
		         })
		{
			var directory = Path.Combine(root, project, "bin");
			if (!Directory.Exists(directory)) continue;
			var path = Directory.GetFiles(directory, file, SearchOption.AllDirectories).FirstOrDefault();
			if (path is not null) yield return (path, label);
		}
	}
}

internal static class Report
{
	internal static string Build(string root, Scanner scanner, IReadOnlyList<string> externalAssemblies)
	{
		var dispositions = Dispositions.Load(Path.Combine(root, "spikes/InternalAccess/dispositions.tsv"));
		var hits = scanner.Hits;
		var inside = hits.Where(IsScanned).ToList();

		var members = inside
			.Where(hit => hit.DeclaringModule != "Generated")
			.GroupBy(hit => new
			{
				hit.DeclaringAssembly, hit.DeclaringModule, hit.DeclaringType, hit.Member, hit.Kind, hit.Access
			})
			.Select(group => new MemberRow(
				group.Key.DeclaringAssembly,
				group.Key.DeclaringModule,
				group.Key.DeclaringType,
				group.Key.Member,
				group.Key.Kind,
				group.Key.Access,
				group.Count(),
				group.GroupBy(hit => (hit.UsingModule, hit.UsingSite))
					.Select(site => new UseSite(site.Key.UsingModule, site.Key.UsingSite, site.Count()))
					.OrderBy(site => ModuleRank(site.Module))
					.ThenBy(site => site.Site, StringComparer.Ordinal)
					.ToList()))
			.OrderBy(row => ModuleRank(row.Module))
			.ThenBy(row => row.Type, StringComparer.Ordinal)
			.ThenBy(row => row.Member, StringComparer.Ordinal)
			.ToList();

		var builder = new StringBuilder();
		builder.AppendLine("# Internal access inventory: Basil.Application.Runtime and Basil.Application.Capabilities");
		builder.AppendLine();
		builder.AppendLine("Spike S1 of Phase E (`plans/phase-e-plan-20261028.md`). Read-only analysis: it names every");
		builder.AppendLine("assembly-visible member (internal, protected internal, private protected) that is used from a");
		builder.AppendLine("different module, so that after the split each one is either made public, moved, or listed.");
		builder.AppendLine();
		builder.AppendLine("## What was scanned");
		builder.AppendLine();
		builder.AppendLine("| item | value |");
		builder.AppendLine("|---|---|");
		builder.AppendLine("| assemblies | `Basil.Application.Runtime.dll`, `Basil.Application.Capabilities.dll` (Debug build) |");
		builder.AppendLine($"| method bodies scanned | {scanner.MethodsScanned} |");
		builder.AppendLine($"| member and type references reported | {inside.Count} inside the two assemblies, {hits.Count - inside.Count} from the assemblies built outside them |");
		builder.AppendLine($"| distinct members and types | {members.Count} |");
		builder.AppendLine(
			$"| other assemblies scanned for references | {(externalAssemblies.Count == 0 ? "none built" : string.Join(", ", externalAssemblies.Select(name => "`" + name + "`")))} |");
		builder.AppendLine();
		builder.AppendLine(
			"Method: the IL of every method body is walked instruction by instruction; `call`, `callvirt`, `newobj`,");
		builder.AppendLine(
			"`ldfld`, `ldflda`, `stfld`, `ldsfld`, `stsfld`, `ldftn`, `ldvirtftn`, `ldtoken` and every type in a");
		builder.AppendLine(
			"method, field or local signature (including generic instantiations) are resolved to a definition in the");
		builder.AppendLine(
			"two assemblies. A reference is reported when its definition is assembly-visible and its declaring type's");
		builder.AppendLine(
			"module differs from the module of the method that names it. Modules are the first namespace segment");
		builder.AppendLine(
			"under `Basil.Application.Runtime` / `Basil.Application.Capabilities`; `Connections` is counted as `Users`");
		builder.AppendLine(
			"(plan E4: one project). Two labels are not modules: `Root(Runtime)` is the composition in");
		builder.AppendLine(
			"`src/Application/Runtime/DependencyInjection.cs`, which stays a composing project (E6), and");
		builder.AppendLine(
			"`Root(Capabilities)` is `src/Application/Capabilities/DependencyInjection.cs`, which is split into one");
		builder.AppendLine(
			"registration method per module (E3..E5). `Generated` marks types the Mediator source generator or the C#");
		builder.AppendLine(
			"compiler emitted into the assembly (`Mediator.*`, `Microsoft.Extensions.DependencyInjection.*`,");
		builder.AppendLine("`System.Text.RegularExpressions.Generated.*`, `<PrivateImplementationDetails>`): they belong to no");
		builder.AppendLine("module and are regenerated into whichever assembly compiles them.");
		builder.AppendLine();
		builder.AppendLine("## InternalsVisibleTo today");
		builder.AppendLine();
		builder.AppendLine("| assembly | grants internals to | declared in |");
		builder.AppendLine("|---|---|---|");
		builder.AppendLine(
			"| `Basil.Application.Runtime` | `Basil.Application.Runtime.Tests` | `src/Application/Runtime/Basil.Application.Runtime.csproj` |");
		builder.AppendLine(
			"| `Basil.Application.Capabilities` | `Basil.Application.Runtime` | `src/Application/Capabilities/Basil.Application.Capabilities.csproj` |");
		builder.AppendLine();
		builder.AppendLine(
			"`Basil.Bot` and `tests/Support/Basil.Testing` are not in either list, and the scan of their built");
		builder.AppendLine(
			"assemblies finds no reference to an assembly-visible member. `Basil.Application.Runtime.Tests` is");
		builder.AppendLine(
			"the one consumer that does see internals today; every such use is listed under");
		builder.AppendLine(
			"\"References from outside the two assemblies\", with the module that declares the member, because");
		builder.AppendLine("after the split that module's project must carry the grant.");
		builder.AppendLine();

		AppendSummary(builder, members, inside, dispositions);
		AppendTables(builder, members, dispositions);
		AppendPromotions(builder, members, dispositions, root);
		AppendGenerated(builder, inside);
		AppendInterfaceSection(builder, root);
		AppendExternalSection(builder, hits, externalAssemblies);
		AppendLimits(builder);
		AppendRegenerate(builder);

		if (scanner.Ambiguities.Count > 0)
		{
			builder.AppendLine("## Appendix: references the resolver could not pin to one definition");
			builder.AppendLine();
			foreach (var line in scanner.Ambiguities.Distinct().OrderBy(line => line, StringComparer.Ordinal))
				builder.AppendLine($"* {line}");
			builder.AppendLine();
		}

		return builder.ToString();
	}

	private static void AppendSummary(StringBuilder builder, List<MemberRow> members, IReadOnlyList<Hit> hits,
		IReadOnlyDictionary<string, (string Disposition, string Reason)> dispositions)
	{
		builder.AppendLine("## Summary");
		builder.AppendLine();
		builder.AppendLine("### Uses per module pair");
		builder.AppendLine();
		builder.AppendLine("| declaring module | used from module | distinct members | uses |");
		builder.AppendLine("|---|---|---:|---:|");
		foreach (var pair in hits
			         .Where(hit => hit.DeclaringModule != "Generated")
			         .GroupBy(hit => (hit.DeclaringModule, hit.UsingModule))
			         .OrderBy(group => ModuleRank(group.Key.DeclaringModule))
			         .ThenBy(group => ModuleRank(group.Key.UsingModule)))
			builder.AppendLine(
				$"| {pair.Key.DeclaringModule} | {pair.Key.UsingModule} | " +
				$"{pair.Select(hit => (hit.DeclaringType, hit.Member)).Distinct().Count()} | {pair.Count()} |");
		builder.AppendLine();

		var silent = Program.ModuleOrder
			.Where(module => module is not ("Generated" or "Root(Runtime)" or "Root(Capabilities)"))
			.Except(hits.Select(hit => hit.DeclaringModule))
			.ToList();
		if (silent.Count > 0)
		{
			builder.AppendLine(
				$"Modules with no cross-module use of an assembly-visible member: {string.Join(", ", silent.Select(module => "`" + module + "`"))}.");
			builder.AppendLine();
		}

		builder.AppendLine("### 15 most used members");
		builder.AppendLine();
		builder.AppendLine("| member | module | kind | uses | from modules |");
		builder.AppendLine("|---|---|---|---:|---|");
		foreach (var row in members.OrderByDescending(row => row.Count)
			         .ThenBy(row => row.Module, StringComparer.Ordinal)
			         .ThenBy(row => row.Type, StringComparer.Ordinal)
			         .ThenBy(row => row.Member, StringComparer.Ordinal)
			         .Take(15))
			builder.AppendLine(
				$"| `{Name(row)}` | {row.Module} | {Kind(row.Kind)} | {row.Count} | " +
				$"{string.Join(", ", row.Sites.Select(site => site.Module).Distinct().OrderBy(ModuleRank))} |");
		builder.AppendLine();

		var byDisposition = members
			.GroupBy(row => dispositions.TryGetValue(Name(row), out var entry) ? entry.Disposition : "?")
			.ToDictionary(group => group.Key, group => group.Count());
		builder.AppendLine("### Proposed dispositions");
		builder.AppendLine();
		builder.AppendLine("| disposition | members |");
		builder.AppendLine("|---|---:|");
		foreach (var (disposition, count) in byDisposition.OrderBy(pair => pair.Key, StringComparer.Ordinal))
			builder.AppendLine($"| {disposition} | {count} |");
		builder.AppendLine();
		builder.AppendLine("`move` means the behaviour exercised at the call site belongs to the module that owns the member:");
		builder.AppendLine("it moves there, the call disappears and the member stays internal. `public` means the member is");
		builder.AppendLine("part of the module's contract for a caller that stays where it is. `ivt` means the member stays");
		builder.AppendLine("internal and the consumer is granted internals; no member needs it, because every cross-module use");
		builder.AppendLine("below is either a registration that moves into the owning module or a contract that becomes public.");
		builder.AppendLine();

		builder.AppendLine("### Uses that are not composition or code generation");
		builder.AppendLine();
		builder.AppendLine(
			"The tables above also count registrations (`Root(Runtime)`, `Root(Capabilities)`) and the code the");
		builder.AppendLine(
			"Mediator generator emits (`Generated`). These rows are what one module really reaches for in the");
		builder.AppendLine("module that declares the member.");
		builder.AppendLine();
		builder.AppendLine("| member | module | used from | disposition |");
		builder.AppendLine("|---|---|---|---|");
		foreach (var row in members
			         .Where(row => row.Sites.Any(site =>
				         site.Module is not ("Root(Runtime)" or "Root(Capabilities)" or "Generated")))
			         .OrderBy(row => ModuleRank(row.Module))
			         .ThenBy(row => row.Type, StringComparer.Ordinal)
			         .ThenBy(row => row.Member, StringComparer.Ordinal))
			builder.AppendLine(
				$"| `{Name(row)}` | {row.Module} | " +
				$"{string.Join(", ", row.Sites.Select(site => site.Module).Where(module => module is not ("Root(Runtime)" or "Root(Capabilities)" or "Generated")).Distinct().OrderBy(ModuleRank))} | " +
				$"{DispositionOf(row, dispositions)} |");
		builder.AppendLine();
	}

	private static void AppendTables(StringBuilder builder, List<MemberRow> members,
		IReadOnlyDictionary<string, (string Disposition, string Reason)> dispositions)
	{
		builder.AppendLine("## Members used across modules");
		builder.AppendLine();
		builder.AppendLine(
			"One row per member. `access` is the declared accessibility of the member and of its declaring type.");
		builder.AppendLine(
			"`used by` lists the module and the method that names it, with the number of references in that method.");
		builder.AppendLine(
			"Counts include signature references and the synthesized members of a record (`Equals`, `GetHashCode`,");
		builder.AppendLine(
			"`Deconstruct`, property accessors), so a context held as a field of a record counts once per synthesized");
		builder.AppendLine("member; the call site, not the count, decides the disposition.");
		builder.AppendLine("`disposition` is one word, then the reason.");
		builder.AppendLine();

		foreach (var module in members.Select(row => row.Module).Distinct().OrderBy(ModuleRank))
		{
			builder.AppendLine($"### {module} (declaring module)");
			builder.AppendLine();
			builder.AppendLine("| declaring type.member | kind | access | used by (module, type.method) | count | disposition |");
			builder.AppendLine("|---|---|---|---|---:|---|");
			foreach (var row in members.Where(row => row.Module == module))
			{
				var disposition = dispositions.TryGetValue(Name(row), out var entry)
					? $"{entry.Disposition} — {entry.Reason}"
					: "? — not reviewed";
				var usedBy = string.Join("<br>",
					row.Sites.Select(site => $"{site.Module}: `{site.Site}` ({site.Count})"));
				builder.AppendLine(
					$"| `{Name(row)}` | {Kind(row.Kind)} | {row.Access} | {usedBy} | {row.Count} | {disposition} |");
			}

			builder.AppendLine();
		}
	}

	private static void AppendPromotions(StringBuilder builder, List<MemberRow> members,
		IReadOnlyDictionary<string, (string Disposition, string Reason)> dispositions, string root)
	{
		var typeAccess = TypeAccess.Find(root);
		var alreadyPublic = members
			.Where(row => row.Kind == MemberKind.Type && DispositionOf(row, dispositions) == "public")
			.Select(row => row.Type)
			.ToHashSet(StringComparer.Ordinal);

		var promotions = members
			.Where(row => row.Kind != MemberKind.Type && DispositionOf(row, dispositions) == "public")
			.GroupBy(row => row.Type, StringComparer.Ordinal)
			.Where(group => !alreadyPublic.Contains(group.Key))
			.Where(group => typeAccess.TryGetValue(group.Key, out var access) && access is not null)
			.OrderBy(group => group.Key, StringComparer.Ordinal)
			.ToList();

		builder.AppendLine("### Declaring types that must become public with a member");
		builder.AppendLine();
		if (promotions.Count == 0)
		{
			builder.AppendLine("None: every `public` member above is declared by a type that is already public.");
			builder.AppendLine();
			return;
		}

		builder.AppendLine(
			"A `public` member is only legal when its declaring type is at least as accessible. These types are");
		builder.AppendLine("`internal` today and declare a member marked `public` above, so they become `public` in the same");
		builder.AppendLine("change.");
		builder.AppendLine();
		builder.AppendLine("| declaring type | module | access | public members |");
		builder.AppendLine("|---|---|---|---|");
		foreach (var group in promotions)
			builder.AppendLine(
				$"| `{group.Key}` | {group.First().Module} | {typeAccess[group.Key]} | " +
				$"{string.Join(", ", group.Select(row => "`" + row.Member + "`").OrderBy(name => name, StringComparer.Ordinal))} |");
		builder.AppendLine();
	}

	private static void AppendGenerated(StringBuilder builder, IReadOnlyList<Hit> hits)
	{
		var generated = hits.Where(hit => hit.DeclaringModule == "Generated").ToList();
		builder.AppendLine("## Types emitted into the assembly (not module members)");

		builder.AppendLine();
		if (generated.Count == 0)
		{
			builder.AppendLine("None.");
			builder.AppendLine();
			return;
		}

		builder.AppendLine(
			"These are referenced from a module but are not declared by a module: the Mediator source generator and");
		builder.AppendLine(
			"the C# compiler emit them into whichever assembly compiles them, so the split regenerates them instead of");
		builder.AppendLine("moving them. They are listed so that nothing is hidden, and carry no disposition.");
		builder.AppendLine();
		builder.AppendLine("| emitted type.member | kind | access | used by (module, type.method) | count |");
		builder.AppendLine("|---|---|---|---|---:|");
		foreach (var group in generated
			         .GroupBy(hit => (hit.DeclaringType, hit.Member, hit.Kind, hit.Access))
			         .OrderBy(group => group.Key.DeclaringType, StringComparer.Ordinal))
		{
			var usedBy = string.Join("<br>",
				group.GroupBy(hit => (hit.UsingModule, hit.UsingSite))
					.Select(site => $"{site.Key.UsingModule}: `{site.Key.UsingSite}` ({site.Count()})"));
			builder.AppendLine(
				$"| `{group.Key.DeclaringType}.{group.Key.Member}` | {Kind(group.Key.Kind)} | {group.Key.Access} | {usedBy} | {group.Count()} |");
		}

		builder.AppendLine();
	}

	private static void AppendInterfaceSection(StringBuilder builder, string root)
	{
		builder.AppendLine("## Internal types that implement an interface from another module");
		builder.AppendLine();
		var lines = InterfaceScan.Find(root);
		if (lines.Count == 0)
			builder.AppendLine(
				"None: no assembly-visible type of one module implements an interface declared in another module.");
		else
		{
			builder.AppendLine("| internal type | module | interface | interface module |");
			builder.AppendLine("|---|---|---|---|");
			foreach (var line in lines) builder.AppendLine(line);
		}

		builder.AppendLine();
	}

	private static void AppendExternalSection(StringBuilder builder, IReadOnlyList<Hit> hits,
		IReadOnlyList<string> externalAssemblies)
	{
		builder.AppendLine("## References from outside the two assemblies");
		builder.AppendLine();
		if (externalAssemblies.Count == 0)
		{
			builder.AppendLine("No external assembly was built when the scan ran.");
			builder.AppendLine();
			return;
		}

		var external = hits.Where(hit => !IsScanned(hit)).ToList();
		builder.AppendLine("| consumer assembly | uses | distinct members |");
		builder.AppendLine("|---|---:|---:|");
		foreach (var consumer in externalAssemblies.OrderBy(name => name, StringComparer.Ordinal))
		{
			var uses = external.Where(hit => hit.UsingAssembly == consumer).ToList();
			builder.AppendLine(
				$"| `{consumer}` | {uses.Count} | {uses.Select(hit => (hit.DeclaringType, hit.Member)).Distinct().Count()} |");
		}

		builder.AppendLine();
		if (external.Count == 0)
		{
			builder.AppendLine("No built consumer references an assembly-visible member of Runtime or Capabilities.");
			builder.AppendLine();
			return;
		}

		builder.AppendLine(
			"A consumer sees an internal member only through an `InternalsVisibleTo` grant. After the split the");
		builder.AppendLine(
			"grant moves to the module project that declares the member, so the module column names the project");
		builder.AppendLine("that must grant it to that consumer.");
		builder.AppendLine();
		builder.AppendLine(
			"Not every member in these tables appears in the inventory above: a consumer also uses internal");
		builder.AppendLine(
			"members that no module uses across a boundary (a finder, a queue drain, a score entry point), and");
		builder.AppendLine("the module that ends up declaring those must grant them too.");
		builder.AppendLine();

		foreach (var consumer in external.Select(hit => hit.UsingAssembly).Distinct()
			         .OrderBy(name => name, StringComparer.Ordinal))
		{
			builder.AppendLine($"### {consumer}");
			builder.AppendLine();
			builder.AppendLine("| declaring type.member | module | kind | access | uses |");
			builder.AppendLine("|---|---|---|---|---:|");
			foreach (var group in external.Where(hit => hit.UsingAssembly == consumer)
				         .GroupBy(hit => (hit.DeclaringModule, hit.DeclaringType, hit.Member, hit.Kind, hit.Access))
				         .OrderBy(group => ModuleRank(group.Key.DeclaringModule))
				         .ThenBy(group => group.Key.DeclaringType, StringComparer.Ordinal)
				         .ThenBy(group => group.Key.Member, StringComparer.Ordinal))
			{
				var name = group.Key.Kind == MemberKind.Type
					? group.Key.DeclaringType
					: $"{group.Key.DeclaringType}.{group.Key.Member}";
				builder.AppendLine(
					$"| `{name}` | {group.Key.DeclaringModule} | {Kind(group.Key.Kind)} | {group.Key.Access} | {group.Count()} |");
			}

			builder.AppendLine();
		}
	}

	private static void AppendLimits(StringBuilder builder)
	{
		builder.AppendLine("## What this scan cannot see");
		builder.AppendLine();
		builder.AppendLine(
			"* **Reflection.** `Type.GetType`, `GetMethod`, `Activator.CreateInstance`, `MakeGenericType` and");
		builder.AppendLine(
			"  `Assembly.GetTypes` name a type in a string or a `Type` value whose use is invisible in IL. The");
		builder.AppendLine(
			"  solution builds a container with `ValidateOnBuild`, so a missing registration would surface at run");
		builder.AppendLine(
			"  time, not here; `typeof(` and `nameof(` on internal types need a separate grep.");
		builder.AppendLine(
			"* **Expression trees.** A lambda converted to `Expression<...>` compiles to data, not to `call`");
		builder.AppendLine(
			"  instructions; `Expression.Property`, `Expression.Call` and the `Expression<T>` body are invisible.");
		builder.AppendLine(
			"* **Generated code that is not in the assembly.** The Mediator generator's output is scanned because it");
		builder.AppendLine(
			"  ends up in the assembly, but the generator's own resolution rules (which handler a message maps to,");
		builder.AppendLine(
			"  and which assembly's `AddMediator` wins) are not visible; `GenerateTypesAsInternal` is a build-time");
		builder.AppendLine("  decision, and the split changes which assembly generates the registrations (spike S2).");
		builder.AppendLine(
			"* **`dynamic`, `DispatchProxy`, `Reflection.Emit` and serializers** that construct members from metadata");
		builder.AppendLine("  at run time (System.Text.Json with a non-public constructor or setter, for example).");
		builder.AppendLine(
			"* **`nameof` and string literals** naming a member, and XML docs, are not references.");
		builder.AppendLine(
			"* **Delegates.** `ldftn` is attributed to the method that creates the delegate, which is where the");
		builder.AppendLine("  compile error appears; the later invocation is not attributed to the declaring module.");
		builder.AppendLine(
			"* **A use from a type the scan cannot place.** Types outside both project namespaces are reported as");
		builder.AppendLine(
			"  `Generated`; a third-party type compiled into the assembly would be hidden there.");
		builder.AppendLine(
			"* **Attributes and attribute arguments** are metadata, not instructions: a type or member named only in");
		builder.AppendLine("  an attribute is not reported.");
		builder.AppendLine(
			"* **`InternalsVisibleTo` consumers outside the build** (the Bot and host test projects, load tests) are");
		builder.AppendLine(
			"  only scanned when their assemblies are built; the hosts do not build today, so `src/Hosts` is not");
		builder.AppendLine("  covered.");
		builder.AppendLine(
			"* **The inverse direction of a signature**: a public member that would leak an internal type is a");
		builder.AppendLine("  compiler error at split time, not a use, so it is not counted here.");
		builder.AppendLine(
			"* **Whether a disposition compiles.** A `move` is a reading of the call site, not a built result: it");
		builder.AppendLine(
			"  names where the behaviour belongs, and E3..E6 must verify that the receiving module can expose it");
		builder.AppendLine("  without a new internal use in the other direction.");
		builder.AppendLine();
	}

	internal static bool IsScanned(Hit hit) =>
		hit.UsingAssembly is "Basil.Application.Runtime" or "Basil.Application.Capabilities";

	private static void AppendRegenerate(StringBuilder builder)
	{
		builder.AppendLine("## How to regenerate");
		builder.AppendLine();
		builder.AppendLine("The project is not in `Basil.slnx`. Build the assemblies whose references are read, one at a time,");
		builder.AppendLine("then run the tool; it rewrites this file from `dispositions.tsv`.");
		builder.AppendLine();
		builder.AppendLine("```bash");
		builder.AppendLine("dotnet build src/Application/Runtime --nologo -v q");
		builder.AppendLine("dotnet build src/Application/Capabilities --nologo -v q");
		builder.AppendLine("dotnet build src/Bot --nologo -v q");
		builder.AppendLine("dotnet build tests/Support/Basil.Testing --nologo -v q");
		builder.AppendLine("dotnet build tests/Application/Runtime --nologo -v q");
		builder.AppendLine("dotnet run --project spikes/InternalAccess");
		builder.AppendLine("```");
		builder.AppendLine();
	}

	internal static int ModuleRank(string module) =>
		Array.IndexOf(Program.ModuleOrder, module) is var index && index >= 0
			? index
			: Program.ModuleOrder.Length;

	internal static string Name(MemberRow row) =>
		row.Kind == MemberKind.Type ? row.Type : $"{row.Type}.{row.Member}";

	internal static string DispositionOf(MemberRow row,
		IReadOnlyDictionary<string, (string Disposition, string Reason)> dispositions) =>
		dispositions.TryGetValue(Name(row), out var entry) ? entry.Disposition : "?";

	private static string Kind(MemberKind kind) => kind switch
	{
		MemberKind.Method => "method",
		MemberKind.Constructor => "constructor",
		MemberKind.Field => "field",
		MemberKind.Property => "property",
		MemberKind.Event => "event",
		MemberKind.Type => "type",
		_ => kind.ToString()
	};

	internal sealed record MemberRow(
		string Assembly,
		string Module,
		string Type,
		string Member,
		MemberKind Kind,
		string Access,
		int Count,
		IReadOnlyList<UseSite> Sites);

	internal sealed record UseSite(string Module, string Site, int Count);
}

internal static class Dispositions
{
	internal static IReadOnlyDictionary<string, (string Disposition, string Reason)> Load(string path)
	{
		var result = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
		if (!File.Exists(path)) return result;
		foreach (var line in File.ReadAllLines(path))
		{
			if (line.Length == 0 || line.StartsWith('#')) continue;
			var parts = line.Split('\t');
			if (parts.Length < 2) continue;
			result[parts[0]] = (parts[1], parts.Length > 2 ? parts[2] : "");
		}

		return result;
	}
}

/// <summary>Reads the declared accessibility of every type in the two scanned assemblies.</summary>
internal static class TypeAccess
{
	internal static Dictionary<string, string?> Find(string root)
	{
		var result = new Dictionary<string, string?>(StringComparer.Ordinal);
		foreach (var (project, file) in new[]
		         {
			         ("src/Application/Runtime", "Basil.Application.Runtime.dll"),
			         ("src/Application/Capabilities", "Basil.Application.Capabilities.dll")
		         })
		{
			using var model = new AssemblyModel(Program.FindAssembly(root, project, file));
			foreach (var handle in model.Reader.TypeDefinitions)
				result[model.FullName(handle)] = Access.OfType(model.Reader.GetTypeDefinition(handle).Attributes);
		}

		return result;
	}
}

/// <summary>Finds assembly-visible types whose implemented interface is declared in another module.</summary>
internal static class InterfaceScan
{
	internal static List<string> Find(string root)
	{
		var runtime = Program.FindAssembly(root, "src/Application/Runtime", "Basil.Application.Runtime.dll");
		var capabilities = Program.FindAssembly(root, "src/Application/Capabilities",
			"Basil.Application.Capabilities.dll");
		using var runtimeModel = new AssemblyModel(runtime);
		using var capabilitiesModel = new AssemblyModel(capabilities);
		var models = new[] { runtimeModel, capabilitiesModel };
		var lines = new List<string>();

		foreach (var model in models)
		foreach (var typeHandle in model.Reader.TypeDefinitions)
		{
			var type = model.Reader.GetTypeDefinition(typeHandle);
			var typeFullName = model.FullName(typeHandle);
			var typeAccess = Access.OfType(type.Attributes);
			if (!Access.IsAssemblyVisible(typeAccess)) continue;

			foreach (var implementationHandle in type.GetInterfaceImplementations())
			{
				var implementation = model.Reader.GetInterfaceImplementation(implementationHandle);
				var interfaceName = model.FullName(implementation.Interface);
				var owner = models.FirstOrDefault(candidate => candidate.TryGetType(interfaceName, out _));
				if (owner is null) continue;
				var interfaceModule = Scanner.ModuleOf(owner.Name, interfaceName);
				var typeModule = Scanner.ModuleOf(model.Name, typeFullName);
				if (interfaceModule == typeModule) continue;
				lines.Add(
					$"| `{Scanner.Display(typeFullName)}` | {typeModule} | " +
					$"`{Scanner.Display(interfaceName)}` | {interfaceModule} |");
			}
		}

		return lines;
	}
}
