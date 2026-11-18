using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Host.Cli.Commands;
using Basil.Testing.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Tests;

public sealed class CliFixture : IAsyncLifetime
{
	public BasilHost Host { get; private set; } = null!;

	public PersistenceFixture Database { get; } = new();

	public CommandRunner Runner => Host.Services.GetRequiredService<CommandRunner>();

	public async ValueTask InitializeAsync()
	{
		await Database.InitializeAsync();
		Host = await BasilHost.StartAsync(Database);
	}

	public async ValueTask DisposeAsync()
	{
		await Host.DisposeAsync();
		await Database.DisposeAsync();
	}

	public async Task<(int Code, string Text)> RunAsync(string line, CommandSession? session = null)
	{
		var output = new CollectedOutput();
		var code = await Runner.RunAsync(CommandCatalog.Split(line), output, true, session,
			TestContext.Current.CancellationToken);
		return (code, output.Text);
	}

	public static string Name(string prefix)
	{
		return prefix + Guid.NewGuid().ToString("N")[..8];
	}

	public async Task<UserRecord> UserAsync(string name)
	{
		return (await Host.Services.GetRequiredService<IUserRepository>().GetByNameAsync(name))!;
	}
}

[CollectionDefinition("CLI host")]
public sealed class CliCollection : ICollectionFixture<CliFixture>;