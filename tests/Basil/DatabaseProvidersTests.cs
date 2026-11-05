using Basil.Infrastructure.Common.Persistence;
using Basil.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Basil.Tests;

/// <summary>Checks that each database endpoint names its own engine, and that the choice is validated at start.</summary>
public sealed class DatabaseProvidersTests
{
	private static DatabaseOptions Options(string? read, string? write)
	{
		var values = new Dictionary<string, string?>
		{
			["ConnectionStrings:Write"] = "Host=primary;Database=basil;Server=primary;User ID=u;Password=p",
			["Basil:Database:Read:Provider"] = read,
			["Basil:Database:Write:Provider"] = write
		};
		return DatabaseSetup.Read(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
	}

	[Fact]
	public void AReadEngineDifferentFromTheWriteEngineIsValid()
	{
		var providers = Basil.Infrastructure.Persistence.DependencyInjection.Providers(Options("MySql", "PostgreSQL"));

		Assert.IsType<MySqlDatabaseProvider>(providers.Read);
		Assert.IsType<PostgresDatabaseProvider>(providers.Write);
	}

	[Fact]
	public void TheReadEngineFallsBackToTheWriteEngine()
	{
		var providers = Basil.Infrastructure.Persistence.DependencyInjection.Providers(Options(null, "MySql"));

		Assert.IsType<MySqlDatabaseProvider>(providers.Read);
		Assert.IsType<MySqlDatabaseProvider>(providers.Write);
	}

	[Fact]
	public void TheWriteEngineDefaultsToPostgreSql()
	{
		var providers = Basil.Infrastructure.Persistence.DependencyInjection.Providers(Options(null, null));

		Assert.IsType<PostgresDatabaseProvider>(providers.Write);
	}

	[Fact]
	public void TheNamesAreNotCaseSensitive()
	{
		var providers = Basil.Infrastructure.Persistence.DependencyInjection.Providers(Options("mysql", "postgresql"));

		Assert.IsType<MySqlDatabaseProvider>(providers.Read);
	}

	[Fact]
	public void AnUnknownEngineStopsTheServerNamingTheAllowedValues()
	{
		var exception = Assert.Throws<InvalidOperationException>(() =>
			Basil.Infrastructure.Persistence.DependencyInjection.Providers(Options("Oracle", "PostgreSQL")));

		Assert.Contains("Basil:Database:Read:Provider", exception.Message);
		foreach (var name in Basil.Infrastructure.Persistence.DependencyInjection.ProviderNames)
			Assert.Contains(name, exception.Message);
	}

	[Fact]
	public async Task EachEndpointBuildsItsContextsWithItsOwnEngine()
	{
		var options = Options("MySql", "PostgreSQL");
		options.ReadConnectionString = "Server=replica;Database=basil;User ID=u;Password=p";
		await using var database = new Database(Basil.Infrastructure.Persistence.DependencyInjection.Providers(options), Microsoft.Extensions.Options.Options.Create(options));

		var chat = System.Reflection.Assembly.Load("Basil.Infrastructure.Chat").GetTypes()
			.Single(type => type.Name == "ChatDbContext");
		await using var read = (Microsoft.EntityFrameworkCore.DbContext)typeof(Database)
			.GetMethod(nameof(Database.CreateReadContext))!.MakeGenericMethod(chat).Invoke(database, null)!;
		await using var write = (Microsoft.EntityFrameworkCore.DbContext)typeof(Database)
			.GetMethod(nameof(Database.CreateConfirmedContext))!.MakeGenericMethod(chat).Invoke(database, null)!;

		Assert.Contains("MySQL", read.Database.ProviderName, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("Npgsql", write.Database.ProviderName);
	}
}
