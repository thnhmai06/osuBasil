using System.Text.RegularExpressions;

namespace Basil.ArchitectureTests;

/// <summary>
///     A capability (<c>Scoped*</c> view) only decides whether the actor may run a command, calls one method of the
///     object that owns the behaviour and wraps the answer; the logic lives in the object.
/// </summary>
public partial class ScopedThinnessTests
{
	private const int MaxBodyLines = 12;

	[Fact]
	public void NoCapabilityDeclaresANestedTypeOrAFatMethod()
	{
		var root = Path.Combine(FindRepositoryRoot(), "src", "Application");
		var offenders = Directory.EnumerateFiles(root, "Scoped*.cs", SearchOption.AllDirectories)
			.Where(file => Path.GetFileName(Path.GetDirectoryName(file)) == "Capabilities")
			.SelectMany(file => Offences(Path.GetFileName(file), File.ReadAllLines(file)))
			.ToList();

		Assert.True(offenders.Count == 0, "Capabilities that are not thin: " + string.Join(", ", offenders));
	}

	[Fact]
	public void TheDetectorSeesANestedTypeAndAFatMethod_PositiveControl()
	{
		var fat = string.Join('\n', Enumerable.Range(0, MaxBodyLines + 1).Select(i => $"\t\tvar x{i} = {i};"));
		var source = "public struct ScopedFake\n{\n\tprivate sealed class Helper\n\t{\n\t}\n\n\tpublic void Run()\n\t{\n" +
		             fat + "\n\t}\n}";

		var offences = Offences("ScopedFake.cs", source.Split('\n')).ToList();

		Assert.Contains("ScopedFake.cs nested type", offences);
		Assert.Contains(offences, offence => offence.StartsWith("ScopedFake.cs method of "));
	}

	private static IEnumerable<string> Offences(string name, string[] lines)
	{
		var bodyLines = 0;
		var inBody = false;
		foreach (var line in lines)
		{
			if (NestedType().IsMatch(line)) yield return $"{name} nested type";

			if (line.TrimEnd() == "\t{")
			{
				inBody = true;
				bodyLines = 0;
			}
			else if (line.TrimEnd() == "\t}")
			{
				if (inBody && bodyLines > MaxBodyLines) yield return $"{name} method of {bodyLines} lines";
				inBody = false;
			}
			else if (inBody && !string.IsNullOrWhiteSpace(line))
			{
				bodyLines++;
			}
		}
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Basil.slnx")))
			directory = directory.Parent;

		return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
	}

	[GeneratedRegex(@"^\t(public |private |internal |protected )?(sealed |static |readonly |abstract )*(class|struct|record|enum|interface) ")]
	private static partial Regex NestedType();
}
