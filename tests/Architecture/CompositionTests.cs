using Basil.Bot;
using Basil.Infrastructure.Common;
using Basil.Infrastructure.Common.Postgres;
using Basil.Host;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.ArchitectureTests;

/// <summary>
///     The server as the host composes it: every registration of the modules, their storage and the bot can be built and
///     resolved. A construction cycle shows up as a hang, so the check runs against a deadline.
/// </summary>
public sealed class CompositionTests
{
	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

	[Fact]
	public async Task EveryRegistrationOfTheApplicationCanBeBuiltAndResolved()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		AddCommonOptions(services);
		services.AddBasil();
		services.AddBot();

		await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true,
			ValidateScopes = true
		});

		var unresolved = await Task.Run(() => Resolve(provider, services), TestContext.Current.CancellationToken)
			.WaitAsync(Deadline, TestContext.Current.CancellationToken);

		Assert.Empty(unresolved);
	}

	/// <summary>Every handler class of the application and the bot is registered for each event type it handles.</summary>
	[Fact]
	public async Task EveryEventHandlerIsRegisteredForTheEventsItHandles()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		AddCommonOptions(services);
		services.AddBasil();
		services.AddBot();
		await using var provider = services.BuildServiceProvider();

		var contracts = ModuleAssemblies.Applications.Append(typeof(Bot.DependencyInjection).Assembly)
			.SelectMany(assembly => assembly.GetTypes())
			.Where(type => type is { IsClass: true, IsAbstract: false })
			.SelectMany(type => type.GetInterfaces()
				.Where(contract => contract.IsGenericType &&
				                   contract.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
				.Select(contract => (Handler: type, Contract: contract)))
			.ToList();

		Assert.NotEmpty(contracts);
		var missing = contracts
			.Where(pair => !provider.GetServices(pair.Contract).Any(handler => handler!.GetType() == pair.Handler))
			.Select(pair => $"{pair.Handler.Name} for {pair.Contract.GetGenericArguments()[0].Name}");
		Assert.Empty(missing);
	}

	private static List<string> Resolve(ServiceProvider provider, ServiceCollection services)
	{
		var failures = new List<string>();
		foreach (var descriptor in services.Where(descriptor => !descriptor.ServiceType.ContainsGenericParameters))
			try
			{
				_ = provider.GetServices(descriptor.ServiceType).Count();
			}
			catch (Exception exception)
			{
				failures.Add($"{descriptor.ServiceType.FullName}: {exception.Message}");
			}

		return failures;
	}

	private static void AddCommonOptions(IServiceCollection services)
	{
		services.AddSingleton(Options.Create(new StorageOptions { DataDirectory = Path.GetTempPath() }));
		services.AddSingleton(
			Options.Create(new DatabaseOptions { ConnectionString = "Host=localhost;Database=basil" }));
	}
}