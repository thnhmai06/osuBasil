using Basil.Application.Content.Persistence;
using Basil.Domain.Users;
using Basil.Testing.Clients;
using Basil.Testing.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Rest.Tests;

/// <summary>One server and one PostgreSQL container for the REST host tests, which use different usernames.</summary>
public sealed class RestHostFixture : IAsyncLifetime
{
	private readonly PersistenceFixture _database = new();

	public BasilHost Host { get; private set; } = null!;

	/// <summary>Gets the database server, for tests that start a server of their own.</summary>
	public PersistenceFixture Database => _database;

	/// <summary>Staff: manages users and restrictions.</summary>
	public static readonly UserPermissions StaffPermissions = UserPermissionsGroups.Default |
	                                                          UserPermissions.UserManage | UserPermissions.UserManageRestrict;

	public async ValueTask InitializeAsync()
	{
		await _database.InitializeAsync();
		Host = await BasilHost.StartAsync(_database);
		await RaiseLimitsAsync(Host);
	}

	/// <summary>Lifts the request limits, which all test clients share through the loopback address.</summary>
	public static async Task RaiseLimitsAsync(BasilHost host)
	{
		var settings = host.Services.GetRequiredService<ISettingsRepository>();
		var current = await settings.GetAsync();
		await settings.CreateOrUpdateAsync(current with
		{
			RateLimits = current.RateLimits with
			{
				Rest = new ServerSettings.RestLimitSettings
				{
					Mutations = new ServerSettings.Limit(100_000, TimeSpan.FromMinutes(1)),
					Anonymous = new ServerSettings.AnonymousLimitSettings
					{
						PerIp = new ServerSettings.Limit(100_000, TimeSpan.FromMinutes(1))
					}
				}
			}
		});
	}

	/// <summary>Creates an account and returns a client signed in as it.</summary>
	public async Task<(RestClient Client, int Id)> SignInAsync(string name, UserPermissions? permissions = null)
	{
		var user = await Host.CreateUserAsync(name, "password", permissions);
		var client = new RestClient(Host.Url, Host.Domain);
		await client.LoginAsync(name);
		return (client, user.Id);
	}

	public async ValueTask DisposeAsync()
	{
		await Host.DisposeAsync();
		await _database.DisposeAsync();
	}
}

[CollectionDefinition("REST host")]
public sealed class RestHostCollection : ICollectionFixture<RestHostFixture>;
