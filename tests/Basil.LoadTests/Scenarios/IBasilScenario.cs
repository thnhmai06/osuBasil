using NBomber.Contracts;

namespace Basil.LoadTests.Scenarios;

/// <summary>
///     One load-test workload. Adding a new workload (Phase 2's chat/multiplayer/api, Phase 3's stress,
///     Phase 4's soak) means one new class implementing this interface plus one line in
///     <see cref="ScenarioCatalog" /> — nothing in <c>Client/</c>, <c>Hosting/</c>, or
///     <c>Infrastructure/</c> needs to change.
/// </summary>
public interface IBasilScenario
{
	/// <summary>The scenario's short id, matching its <c>Scenarios:{id}</c> configuration section.</summary>
	string Id { get; }

	/// <summary>
	///     Builds one <see cref="ScenarioProps" /> per configured concurrency level (or per another
	///     scale axis, e.g., room count for multiplayer). The caller (<c>Program.cs</c>) runs each
	///     returned scenario as its own sequential <c>NBomberRunner.Run()</c> call — never all at once —
	///     since concurrency levels of the same workload share the same account pool and must not
	///     collide on the server's one-session-per-account rule.
	/// </summary>
	IReadOnlyList<ScenarioProps> Build(BasilScenarioContext context);
}
