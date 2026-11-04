using System.Xml.Linq;

namespace Basil.ArchitectureTests;

/// <summary>
///     Pins that PostgreSQL stays behind <c>Basil.Infrastructure.Postgres</c>: only the composition root references
///     that project, and no other project under <c>src</c> depends on Npgsql.
/// </summary>
public sealed class PostgresBoundaryTests
{
	private const string Postgres = "Basil.Infrastructure.Postgres";

	[Fact]
	public void OnlyTheCompositionRootReferencesThePostgresProject()
	{
		var referrers = Projects().Where(project => References(project).Contains(Postgres))
			.Select(Path.GetFileNameWithoutExtension);

		Assert.Equal(["Basil"], referrers);
	}

	[Fact]
	public void NoProjectOutsideThePostgresProjectUsesNpgsqlPackages()
	{
		var offenders = Projects().Where(project => Path.GetFileNameWithoutExtension(project) != Postgres)
			.Where(project => Packages(project).Any(IsNpgsql))
			.Select(Path.GetFileNameWithoutExtension);

		Assert.Empty(offenders);
	}

	[Fact]
	public void TheMeasureSeesNpgsqlInThePostgresProject_PositiveControl()
	{
		var postgres = Projects().Single(project => Path.GetFileNameWithoutExtension(project) == Postgres);

		Assert.Contains(Packages(postgres), IsNpgsql);
	}

	private static bool IsNpgsql(string package)
	{
		return package.StartsWith("Npgsql", StringComparison.Ordinal);
	}

	private static string[] Projects()
	{
		var source = Path.Combine(RepositoryRoot(), "src");
		return Directory.GetFiles(source, "*.csproj", SearchOption.AllDirectories)
			.Where(project => !project.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
			.ToArray();
	}

	private static IEnumerable<string> References(string project)
	{
		return XDocument.Load(project).Descendants().Where(element => element.Name.LocalName == "ProjectReference")
			.Select(element => Path.GetFileNameWithoutExtension(
				element.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)));
	}

	private static IEnumerable<string> Packages(string project)
	{
		return XDocument.Load(project).Descendants().Where(element => element.Name.LocalName == "PackageReference")
			.Select(element => element.Attribute("Include")!.Value);
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Basil.slnx")))
			directory = directory.Parent;
		return directory?.FullName ?? throw new DirectoryNotFoundException("Basil.slnx was not found.");
	}
}
