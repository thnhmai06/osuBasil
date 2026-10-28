using System.Text;
using Basil.Application.Auth.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Infrastructure.Common;
using Basil.Infrastructure.Common.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XenoAtom.CommandLine;

namespace Basil.Host;

/// <summary>
///     The commands that run against the storage without starting the server, such as creating the first administrator.
///     They start only the storage (the database is migrated, the stored data loaded), never recover the running state, and
///     wait for every write to be stored before they end.
/// </summary>
public static class OfflineRunner
{
	/// <summary>Tells whether the command line names an offline command.</summary>
	/// <param name="args">The command-line arguments.</param>
	/// <returns><see langword="true" /> when the first argument is the name of an offline command.</returns>
	public static bool Handles(string[] args)
	{
		return args.Length > 0 && args[0].Equals("users", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Runs an offline command.</summary>
	/// <param name="args">The command-line arguments, for example <c>users create --username a --password b</c>.</param>
	/// <param name="configure">Changes the builder before it is built; the tests use it to point at their storage.</param>
	/// <returns>The exit code: 0 when the command did its work.</returns>
	public static async Task<int> RunAsync(string[] args, Action<HostApplicationBuilder>? configure = null)
	{
		string? username = null, password = null;
		var admin = false;
		var app = new CommandApp("basil")
		{
			new Command("users", "Accounts")
			{
				new Command("create", "Create an account")
				{
					{ "u|username=", "The {USERNAME} of the account", v => username = v },
					{ "p|password=", "The plain {PASSWORD} of the account", v => password = v },
					{ "admin", "Give the account every permission", _ => admin = true },
					new HelpOption(),
					async (context, _) =>
					{
						if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
						{
							await context.Error.WriteLineAsync("--username and --password are required.");
							return 2;
						}

						return await CreateUserAsync(username, password, admin, context.Out, context.Error, args, configure);
					}
				}
			}
		};
		return await app.RunAsync(args);
	}

	private static async Task<int> CreateUserAsync(string username, string password, bool admin, TextWriter output,
		TextWriter error, string[] args, Action<HostApplicationBuilder>? configure)
	{
		var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
		builder.Services.AddOptions<StorageOptions>().Bind(builder.Configuration.GetSection("Basil:Storage"));
		builder.Services.AddOptions<DatabaseOptions>().Bind(builder.Configuration.GetSection("Basil:Database"));
		builder.Services.AddBasilStorage();
		configure?.Invoke(builder);
		builder.Logging.SetMinimumLevel(LogLevel.Warning);

		using var host = builder.Build();
		await host.StartAsync();
		try
		{
			var users = host.Services.GetRequiredService<IUserRepository>();
			if (await users.GetByNameAsync(username) is not null)
			{
				await error.WriteLineAsync($"The username {username} is taken.");
				return 1;
			}

			UserRecord user;
			try
			{
				user = await users.CreateAsync(new UserData
				{
					Name = username, Permissions = admin ? UserPermissionsGroups.All : UserPermissionsGroups.Default
				});
			}
			catch (ArgumentException exception)
			{
				await error.WriteLineAsync(exception.Message);
				return 1;
			}

			await host.Services.GetRequiredService<ICredentialRepository>()
				.CreateOrUpdateAsync(new Credentials(user, new Md5(Encoding.UTF8.GetBytes(password))));
			await output.WriteLineAsync($"Created {username} with id {user.Id}.");
			return 0;
		}
		finally
		{
			await host.StopAsync();
		}
	}
}
