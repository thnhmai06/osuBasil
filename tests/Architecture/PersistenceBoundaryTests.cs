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

	[Fact]
	public void NoMigrationNamesAProviderOrAStoreType()
	{
		var offenders = MigrationFiles().Where(Offends).Select(Path.GetFileName);

		Assert.Empty(offenders);
	}

	[Fact]
	public void TheMeasureSeesAProviderAndAStoreTypeInAMigration_PositiveControl()
	{
		var directory = Directory.CreateTempSubdirectory();
		try
		{
			var provider = Path.Combine(directory.FullName, "A.cs");
			var storeType = Path.Combine(directory.FullName, "B.cs");
			File.WriteAllText(provider, "using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;");
			File.WriteAllText(storeType, "table.Column<int>(type: \"integer\", nullable: false)");

			Assert.True(Offends(provider));
			Assert.True(Offends(storeType));
		}
		finally
		{
			directory.Delete(true);
		}
	}

	private static bool Offends(string file)
	{
		var text = File.ReadAllText(file);
		return text.Contains("Npgsql", StringComparison.Ordinal) || text.Contains("MySql", StringComparison.Ordinal) ||
		       text.Contains("type:", StringComparison.Ordinal) ||
		       text.Contains(".HasColumnType(", StringComparison.Ordinal);
	}

	private static string[] MigrationFiles()
	{
		var migrations = Path.Combine(RepositoryRoot(), "src", "Infrastructure", "Persistence", "Migrations");
		var files = Directory.GetFiles(migrations, "*.cs", SearchOption.AllDirectories);
		Assert.NotEmpty(files);
		return files;
	}

	private static bool IsNpgsql(string package)
	{
		return package.StartsWith("Npgsql", StringComparison.Ordinal) ||
		       package.StartsWith("MySql", StringComparison.Ordinal);
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