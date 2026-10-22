using Basil.Application.Capabilities.Beatmaps;
using Basil.Application.Runtime;
using Basil.Application.Persistence.Users;
using Basil.Bot;
using Basil.Infrastructure.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Basil.ArchitectureTests;

/// <summary>
///     The application as the host composes it: every registration of the application, the runtime infrastructure and
///     the bot can be built and resolved, with a stub behind every port that leaves the process. A construction cycle
///     shows up as a hang, so the check runs against a deadline.
/// </summary>
public sealed class CompositionTests
{
	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

	[Fact]
	public async Task EveryRegistrationOfTheApplicationCanBeBuiltAndResolved()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		AddPortStubs(services);
		services.AddApplication();
		services.AddInfrastructureRuntime();
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

	private static void AddPortStubs(IServiceCollection services)
	{
		var portAssemblies = new[] { typeof(IUserRepository).Assembly, typeof(IBeatmapsetReader).Assembly };
		foreach (var port in portAssemblies.SelectMany(assembly => assembly.GetTypes())
			         .Where(type => type.IsInterface && type.IsPublic && !type.ContainsGenericParameters))
			services.AddSingleton(port, _ => Substitute.For([port], []));
	}
}
