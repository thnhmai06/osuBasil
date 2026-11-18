using Basil.Infrastructure.Persistence.Providers;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Persistence;

public static class DependencyInjection
{
	public static readonly IReadOnlyList<string> ProviderNames = ["PostgreSQL", "MySql"];

	public static IServiceCollection AddPersistenceInfrastructure(this IServiceCollection services)
	{
		services.TryAddSingleton(provider => Providers(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value));
		return services;
	}

	public static DatabaseProviders Providers(DatabaseOptions options)
	{
		var write = Create(options.WriteProvider ?? ProviderNames[0], "Basil:Database:Write:Provider");
		var read = options.ReadProvider is null ? write : Create(options.ReadProvider, "Basil:Database:Read:Provider");
		return new DatabaseProviders(read, write);
	}

	private static IDatabaseProvider Create(string name, string setting)
	{
		return name.ToLowerInvariant() switch
		{
			"postgresql" => new PostgresDatabaseProvider(),
			"mysql" => new MySqlDatabaseProvider(),
			_ => throw new InvalidOperationException(
				$"{setting} is '{name}'; the allowed values are {string.Join(", ", ProviderNames)}.")
		};
	}
}