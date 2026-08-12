namespace Basil.LoadTests.Scenarios;

/// <summary>
///     Every <see cref="IBasilScenario" /> the runner knows about, keyed by the same id its
///     <c>Scenarios:{id}</c> configuration section uses. Adding a scenario is one line here plus one
///     new class next to the others in this folder.
/// </summary>
public static class ScenarioCatalog
{
	/// <summary>All registered scenarios, in the order <c>Program.cs</c> runs them.</summary>
	public static IReadOnlyList<IBasilScenario> All { get; } =
	[
		new LoginScenario(),
		new IdleScenario(),
		new ChatScenario(),
		new MultiplayerScenario(),
		new ApiScenario(),
		new SseScenario(),
		new StressScenario(),
		new SoakScenario()
	];
}