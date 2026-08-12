using Basil.LoadTests.Client;
using Basil.LoadTests.Configuration;
using Basil.LoadTests.Hosting;
using Basil.LoadTests.Models;
using Microsoft.Extensions.Configuration;

namespace Basil.LoadTests.Scenarios;

/// <summary>
///     Everything an <see cref="IBasilScenario" /> needs to build its <c>NBomber</c> scenarios: the
///     resolved profile, the seeded account pool, a client factory pointed at the running server, and
///     the raw <see cref="IConfiguration" /> for binding each scenario's own settings section — there is
///     no shared polymorphic settings tree, each scenario binds <c>Scenarios:{id}</c> to its own type.
/// </summary>
public sealed class BasilScenarioContext
{
	public required LoadProfile Profile { get; init; }
	public required IConfiguration Configuration { get; init; }
	public required IReadOnlyList<LoadAccount> Accounts { get; init; }
	public required BasilHttpClientFactory ClientFactory { get; init; }
	public required IServerHost Host { get; init; }
	public required Action<string> LogInfo { get; init; }
	public required Action<string> LogWarning { get; init; }
	public required string ReportFolder { get; init; }

	/// <summary>Binds <c>Scenarios:{scenarioId}</c> to <typeparamref name="T" />, defaulting when the section is absent.</summary>
	public T GetScenarioSettings<T>(string scenarioId) where T : new()
	{
		return Configuration.GetSection($"Scenarios:{scenarioId}").Get<T>() ?? new T();
	}
}