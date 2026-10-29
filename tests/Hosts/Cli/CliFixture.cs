using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Host.Cli.Commands;
using Basil.Testing.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Tests;

/// <summary>One server and one PostgreSQL container for the command tests, which use names of their own.</summary>
public sealed class CliFixture : IAsyncLifetime
{
	private readonly PersistenceFixture _database = new();

	public BasilHost Host { get; private set; } = null!;

	public PersistenceFixture Database => _database;

	public CommandRunner Runner => Host.Services.GetRequiredService<CommandRunner>();

	public async ValueTask InitializeAsync()
	{
		await _database.InitializeAsync();
		Host = await BasilHost.StartAsync(_database);
	}

	/// <summary>Runs a typed command line as the operator would.</summary>
	public async Task<(int Code, string Text)> RunAsync(string line, CommandSession? session = null)
	{
		var output = new CollectedOutput();
		var code = await Runner.RunAsync(CommandCatalog.Split(line), output, true, session, TestContext.Current.CancellationToken);
		return (code, output.Text);
	}

	/// <summary>Makes a name no other test uses.</summary>
	public static string Name(string prefix)
	{
		return prefix + Guid.NewGuid().ToString("N")[..8];
	}

	public async Task<UserRecord> UserAsync(string name)
	{
		return (await Host.Services.GetRequiredService<IUserRepository>().GetByNameAsync(name))!;
	}

	public async ValueTask DisposeAsync()
	{
		await Host.DisposeAsync();
		await _database.DisposeAsync();
	}
}

[CollectionDefinition("CLI host")]
public sealed class CliCollection : ICollectionFixture<CliFixture>;
