using Basil.Infrastructure.Shared.Persistence;
using Microsoft.Extensions.Options;

namespace Basil.Testing.Persistence;

public static class TestDatabase
{
	public static Database Create(string connectionString)
	{
		return new Database(
			Infrastructure.Persistence.DependencyInjection.Providers(new DatabaseOptions
				{ WriteConnectionString = connectionString, WriteProvider = TestServer.ProviderName }),
			Options.Create(new DatabaseOptions { WriteConnectionString = connectionString }));
	}
}