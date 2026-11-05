using Basil.Infrastructure.Common.Persistence;
using Microsoft.Extensions.Options;

namespace Basil.Testing.Persistence;

/// <summary>Makes the server's database over the PostgreSQL provider, for tests that need no composition.</summary>
public static class TestDatabase
{
	/// <summary>Makes a database that reaches the given connection string for reads and writes.</summary>
	/// <param name="connectionString">How to reach the database.</param>
	/// <returns>The database; no connection is opened until a context uses it.</returns>
	public static Database Create(string connectionString)
	{
		return new Database(
			Infrastructure.Persistence.DependencyInjection.Providers(new DatabaseOptions
				{ WriteConnectionString = connectionString, WriteProvider = TestServer.ProviderName }),
			Options.Create(new DatabaseOptions { WriteConnectionString = connectionString }));
	}
}