namespace Basil.ArchitectureTests;

/// <summary>
///     Pins the rule that the Infrastructure projects hold no SQL text: Entity Framework Core with LINQ and row
///     objects only.
/// </summary>
public sealed class PersistenceSqlTests
{
	private static readonly string[] ForbiddenTokens =
		["Dapper", "FromSql", "ExecuteSql", "SqlQuery", "NpgsqlCommand", "new NpgsqlConnection("];

	[Fact]
	public void InfrastructureProjects_HoldNoSqlText()
	{
		var source = Path.Combine(FindRepositoryRoot(), "src");
		var projects = Directory.EnumerateDirectories(Path.Combine(source, "Infrastructure"));
		var offenders = projects
			.SelectMany(project => Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
			.Where(path => !IsGenerated(path))
			.SelectMany(path =>
				ForbiddenIn(File.ReadAllText(path)).Select(token => $"{Path.GetFileName(path)}: {token}"))
			.ToList();

		Assert.True(offenders.Count == 0, "The infrastructure holds no SQL text: " + string.Join(", ", offenders));
	}

	[Fact]
	public void ForbiddenIn_FindsEachRawSqlApi()
	{
		// Positive control: a rule that always passes would not notice any of these.
		foreach (var token in ForbiddenTokens)
			Assert.Contains(token, ForbiddenIn($"var x = connection.{token}(\"select 1\");"));
	}

	[Fact]
	public void ForbiddenIn_IgnoresCodeWithoutRawSql()
	{
		Assert.Empty(
			ForbiddenIn("await context.Users.Where(row => row.Id == id).ExecuteUpdateAsync(set => set, token);"));
	}

	private static IEnumerable<string> ForbiddenIn(string source)
	{
		return ForbiddenTokens.Where(token => source.Contains(token, StringComparison.Ordinal));
	}

	private static bool IsGenerated(string path)
	{
		var separator = Path.DirectorySeparatorChar;
		return path.Contains($"{separator}obj{separator}") || path.Contains($"{separator}bin{separator}") ||
		       path.Contains($"{separator}Migrations{separator}");
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Basil.slnx")))
			directory = directory.Parent;

		return directory?.FullName ??
		       throw new InvalidOperationException("Basil.slnx was not found above the test binaries.");
	}
}