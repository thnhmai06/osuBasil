using System.Text.RegularExpressions;

namespace Basil.ArchitectureTests;

/// <summary>
///     Pins the owner rule that a <c>Scoped*</c> capability only checks privileges, calls the owning object and
///     wraps its answer: it declares no nested type and its methods stay short.
/// </summary>
public sealed partial class ScopedThinnessTests
{
	/// <summary>The most lines (not blank, not comment) a method body of a Scoped type may hold.</summary>
	private const int MaxBodyLines = 12;

	/// <summary>
	///     The Scoped files whose methods still hold the logic of their object; each is listed until it is thinned, and
	///     <see cref="TheListOfFilesNotThinnedYet_HoldsOnlyFilesThatAreStillTooLong" /> removes it from the list.
	/// </summary>
	private static readonly HashSet<string> NotThinnedYet = new(StringComparer.Ordinal);

	[Fact]
	public void ScopedTypes_NoNestedTypes()
	{
		var offenders = ScopedFiles().SelectMany(path => NestedTypes(File.ReadAllText(path))
			.Select(line => $"{Path.GetFileName(path)}: {line.Trim()}")).ToList();

		Assert.True(offenders.Count == 0, "A Scoped type declares no nested type: " + string.Join("; ", offenders));
	}

	[Fact]
	public void ScopedMethods_WithinBodyLimit()
	{
		var offenders = ScopedFiles().Where(path => !NotThinnedYet.Contains(Path.GetFileName(path)))
			.SelectMany(path => LongBodies(File.ReadAllText(path)).Select(body => $"{Path.GetFileName(path)}: {body}"))
			.ToList();

		Assert.True(offenders.Count == 0,
			$"A Scoped method holds at most {MaxBodyLines} lines, the rest belongs to the object: " +
			string.Join("; ", offenders));
	}

	[Fact]
	public void NotThinnedList_OnlyTooLong()
	{
		var tooLong = ScopedFiles().Where(path => LongBodies(File.ReadAllText(path)).Any())
			.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);

		var stale = NotThinnedYet.Where(name => !tooLong.Contains(name)).ToList();

		Assert.True(stale.Count == 0, "Remove the thinned files from the list: " + string.Join(", ", stale));
	}

	[Fact]
	public void ScopedFiles_AreFound()
	{
		// Positive control: a scan that finds no file would pass every rule.
		Assert.Contains(ScopedFiles(), path => path.EndsWith("ScopedServer.cs", StringComparison.Ordinal));
	}

	[Fact]
	public void NestedType_Found()
	{
		const string source =
			"namespace A;\npublic readonly struct ScopedX\n{\n\tprivate sealed class Helper\n\t{\n\t}\n}\n";

		Assert.Single(NestedTypes(source));
	}

	[Fact]
	public void TopLevelType_Ignored()
	{
		Assert.Empty(NestedTypes(
			"namespace A;\npublic readonly struct ScopedX\n{\n\tpublic int M()\n\t{\n\t\treturn 1;\n\t}\n}\n"));
	}

	[Fact]
	public void OverlongMethod_Found()
	{
		var body = string.Concat(Enumerable.Repeat("\t\tx++;\n", MaxBodyLines + 1));
		var source = $"namespace A;\npublic struct ScopedX\n{{\n\tpublic void Long()\n\t{{\n{body}\t}}\n}}\n";

		Assert.Single(LongBodies(source));
	}

	[Fact]
	public void MethodAtLimit_AcceptedSkipsBlank()
	{
		var body = string.Concat(Enumerable.Repeat("\t\tx++;\n", MaxBodyLines)) + "\n\t\t// note\n";
		var source = $"namespace A;\npublic struct ScopedX\n{{\n\tpublic void Ok()\n\t{{\n{body}\t}}\n}}\n";

		Assert.Empty(LongBodies(source));
	}

	private static IEnumerable<string> NestedTypes(string source)
	{
		var depth = 0;
		foreach (var line in source.Split('\n'))
		{
			if (depth == 1 && NestedTypeDeclaration().IsMatch(line)) yield return line;
			depth += line.Count(c => c == '{') - line.Count(c => c == '}');
		}
	}

	private static IEnumerable<string> LongBodies(string source)
	{
		var depth = 0;
		var signature = "";
		var count = 0;
		var named = false;
		foreach (var line in source.Split('\n'))
		{
			var trimmed = line.Trim();
			var before = depth;
			depth += line.Count(c => c == '{') - line.Count(c => c == '}');
			if (before == 1 && trimmed.Length > 0 && !trimmed.StartsWith("///", StringComparison.Ordinal) &&
			    !trimmed.StartsWith('[') && trimmed != "{" && trimmed != "}" && count == 0 && !named)
			{
				signature = trimmed;
				named = true;
			}

			if (before == 1 && trimmed.EndsWith(';')) named = false;
			if (before >= 2 && trimmed.Length > 0 && !trimmed.StartsWith("//", StringComparison.Ordinal)) count++;
			if (before == 2 && depth == 1)
			{
				// count includes the closing brace of the body.
				if (count - 1 > MaxBodyLines) yield return $"{signature} ({count - 1} lines)";
				count = 0;
				named = false;
			}
		}
	}

	private static IEnumerable<string> ScopedFiles()
	{
		var applications = Path.Combine(FindRepositoryRoot(), "src", "Application");
		return Directory.EnumerateFiles(applications, "Scoped*.cs", SearchOption.AllDirectories)
			.Where(path => path.Contains(Path.DirectorySeparatorChar + "Capabilities" + Path.DirectorySeparatorChar,
				StringComparison.Ordinal));
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Basil.slnx")))
			directory = directory.Parent;
		return directory?.FullName ?? throw new DirectoryNotFoundException("Basil.slnx was not found.");
	}

	[GeneratedRegex(
		@"^\s*(public |internal |private |protected )?(sealed |static |readonly |abstract )*(class|struct|enum|record|interface)\s+\w+")]
	private static partial Regex NestedTypeDeclaration();
}