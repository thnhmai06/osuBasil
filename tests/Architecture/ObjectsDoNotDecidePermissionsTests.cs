using System.Text.RegularExpressions;

namespace Basil.ArchitectureTests;

/// <summary>
///     Pins the owner rule that a runtime object does the logic and returns its own outcome, while the <c>Scoped*</c>
///     capability in front of it decides what the caller's privileges allow: an object of the listed modules neither
///     reads effective privileges nor answers with a denial.
/// </summary>
public sealed partial class ObjectsDoNotDecidePermissionsTests
{
	/// <summary>The modules whose objects follow the rule so far; a module joins when its objects are done.</summary>
	private static readonly string[] Modules = ["Multiplayer", "Chat"];

	[Fact]
	public void ObjectsOfTheListedModules_DoNotReadPermissionsOrAnswerWithADenial()
	{
		var offenders = ObjectFiles().SelectMany(path => Offences(File.ReadAllText(path)).Select(line =>
			$"{Path.GetFileName(path)}: {line.Trim()}")).ToList();

		Assert.True(offenders.Count == 0, "Objects that decide privileges:\n" + string.Join("\n", offenders));
	}

	[Fact]
	public void ObjectFiles_AreFound()
	{
		// Positive control: a scan that finds no file would pass every rule.
		Assert.Contains(ObjectFiles(), path => path.EndsWith("Room.Members.cs", StringComparison.Ordinal));
	}

	[Fact]
	public void Offences_FindsAnObjectThatReadsPermissionsOrReturnsAResult()
	{
		const string source = """
		                      internal Result<RoomOutcome> Join(StableConnection actor)
		                      {
		                          var rights = actor.EffectivePrivileges(now);
		                          return Result<RoomOutcome>.Denied(UserPrivileges.Player);
		                      }
		                      """;

		Assert.Equal(3, Offences(source).Count());
	}

	[Fact]
	public void Offences_AcceptsAnObjectThatReceivesPermissionsAsAValue()
	{
		const string source = "public bool Involves(Connection connection, UserPrivileges privileges) => privileges.Allows(UserPrivileges.Player);";

		Assert.Empty(Offences(source));
	}

	private static IEnumerable<string> Offences(string source)
	{
		return source.Split('\n').Where(line => Offence().IsMatch(line));
	}

	private static IEnumerable<string> ObjectFiles()
	{
		var applications = Path.Combine(FindRepositoryRoot(), "src", "Application");
		return Modules.SelectMany(module => Directory.EnumerateFiles(Path.Combine(applications, module, "Objects"),
			"*.cs", SearchOption.AllDirectories));
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Basil.slnx")))
			directory = directory.Parent;

		return directory?.FullName ?? throw new DirectoryNotFoundException("Basil.slnx was not found.");
	}

	[GeneratedRegex(@"EffectivePrivileges\(|Result<\w+>|\.Denied\(")]
	private static partial Regex Offence();
}
