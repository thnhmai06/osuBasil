using Basil.Infrastructure.Shared.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Persistence;

/// <summary>Registers the database engines of the server.</summary>
public static class DependencyInjection
{
	/// <summary>
	///     The names of the database engines <c>Basil:Database:Read:Provider</c> and <c>Basil:Database:Write:Provider</c>
	///     accept.
	/// </summary>
	public static readonly IReadOnlyList<string> ProviderNames = ["PostgreSQL", "MySql"];

	/// <summary>Adds the database engine of each endpoint, chosen by name in the database settings.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddPersistenceInfrastructure(this IServiceCollection services)
	{
		services.TryAddSingleton(provider => Providers(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value));
		return services;
	}

	/// <summary>Chooses the engine of each endpoint; the read endpoint uses the write engine unless it names its own.</summary>
	/// <param name="options">The database settings.</param>
	/// <returns>The engines of the two endpoints.</returns>
	/// <exception cref="InvalidOperationException">An engine name is not one of <see cref="ProviderNames" />.</exception>
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