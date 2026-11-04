using System.Xml.Linq;

namespace Basil.ArchitectureTests;

/// <summary>
///     Pins that PostgreSQL stays behind <c>Basil.Infrastructure.Persistence</c>: only the composition root references
///     that project, and no other project under <c>src</c> depends on Npgsql.
/// </summary>
public sealed class PersistenceBoundaryTests
{
	private const string Persistence = "Basil.Infrastructure.Persistence";

	[Fact]
	public void OnlyTheCompositionRootReferencesThePersistenceProject()
	{
		var referrers = Projects().Where(project => References(project).Contains(Persistence))
			.Select(Path.GetFileNameWithoutExtension);

		Assert.Equal(["Basil"], referrers);
	}

	[Fact]
	public void NoProjectOutsideThePersistenceProjectUsesNpgsqlPackages()
	{
		var offenders = Projects().Where(project => Path.GetFileNameWithoutExtension(project) != Persistence)
			.Where(project => Packages(project).Any(IsNpgsql))
			.Select(Path.GetFileNameWithoutExtension);

		Assert.Empty(offenders);
	}

	[Fact]
	public void TheMeasureSeesNpgsqlInThePersistenceProject_PositiveControl()
	{
		var persistence = Projects().Single(project => Path.GetFileNameWithoutExtension(project) == Persistence);

		Assert.Contains(Packages(persistence), IsNpgsql);
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
