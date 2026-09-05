using Basil.Application.Services.Authentication;

namespace Basil.LoadTests.Configuration;

/// <summary>
///     Fields shared by every scenario whose load shape is "N concurrent virtual users for a fixed
///     duration". Each concrete scenario binds its own section under <c>Scenarios:{id}</c> to its own
///     settings type — there is no shared polymorphic settings tree, so adding a scenario never means
///     touching this file.
/// </summary>
public class ScenarioSettings
{
	/// <summary>Whether this scenario runs at all for the current profile.</summary>
	public bool Enabled { get; init; }

	/// <summary>
	///     The concurrency levels to run, each as its own named NBomber scenario (<c>{id}_100</c>,
	///     <c>{id}_250</c>, ...). Never hardcoded in code — this array is the only thing that needs to
	///     change to add a benchmark size.
	/// </summary>
	public int[] ConcurrentUsers { get; init; } = [];

	/// <summary>How long each concurrency level runs, after warm-up.</summary>
	public int DurationSeconds { get; init; } = 60;

	/// <summary>How long NBomber's warm-up phase runs before measurements start counting.</summary>
	public int WarmUpSeconds { get; init; } = 10;

	/// <summary>
	///     NBomber's per-scenario failure ceiling before it stops the whole test. NBomber's own default
	///     is 5000; scenarios that must survive past that (stress, soak) override it explicitly.
	/// </summary>
	public int MaxFailCount { get; init; } = 5000;

	/// <summary>Gets <see cref="DurationSeconds" /> as a <see cref="TimeSpan" />.</summary>
	public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);

	/// <summary>Gets <see cref="WarmUpSeconds" /> as a <see cref="TimeSpan" />.</summary>
	public TimeSpan WarmUp => TimeSpan.FromSeconds(WarmUpSeconds);
}

/// <summary>Settings for the startup/idle-resource benchmark (not an NBomber scenario).</summary>
public sealed class StartupSettings
{
	/// <summary>Whether the startup benchmark runs at all.</summary>
	public bool Enabled { get; init; }

	/// <summary>How many start/stop cycles to measure.</summary>
	public int Iterations { get; init; } = 5;
}

/// <summary>Settings for <see cref="Scenarios.LoginScenario" />.</summary>
public sealed class LoginSettings : ScenarioSettings
{
	/// <summary>
	///     When <see langword="true" />, every seeded account is logged in once before measurement starts,
	///     so the measured phase hits the bcrypt-verify cache instead of paying full bcrypt cost. When
	///     <see langword="false" />, the run measures the cold (first-login) cost instead. Both are
	///     legitimate measurements of different things; the report states which one ran.
	/// </summary>
	public bool WarmBcryptCache { get; init; } = true;

	/// <summary>
	///     How long to wait after the bcrypt warm-up pass before the first scenario starts. Warm-up
	///     deliberately leaves every account with a live session, and the server rejects a relogin
	///     within seconds of the session's last poll. This settle lets those sessions age past the
	///     guard so each account's first measured login evicts its stale session cleanly instead of
	///     failing with <c>user-already-logged-in</c>.
	/// </summary>
	public double PostWarmupSettleSeconds { get; init; } = LoginService.ReloginGuardWindowSeconds + 1;

	/// <summary>Gets <see cref="PostWarmupSettleSeconds" /> as a <see cref="TimeSpan" />.</summary>
	public TimeSpan PostWarmupSettle => TimeSpan.FromSeconds(PostWarmupSettleSeconds);
}

/// <summary>Settings for <see cref="Scenarios.IdleScenario" />.</summary>
public sealed class IdleSettings : ScenarioSettings;

/// <summary>Settings for <see cref="Scenarios.ChatScenario" />.</summary>
public sealed class ChatSettings : ScenarioSettings
{
	/// <summary>The channel to send/receive messages on.</summary>
	public string Channel { get; init; } = "#osu";

	/// <summary>The percentage of virtual users in each run who send messages; the rest only receive.</summary>
	public int SendersPercent { get; init; } = 20;

	/// <summary>How many messages each sender emits per minute.</summary>
	public int MessagesPerMinutePerSender { get; init; } = 12;

	/// <summary>Target message payload size in bytes (filler appended to the tracking marker).</summary>
	public int MessageBytes { get; init; } = 64;

	/// <summary>
	///     How often a receiver polls while waiting for messages. Deliberately its own setting rather than
	///     reusing the shared <c>Client:PollIntervalSeconds</c>: that value is tuned for realistic idle
	///     client behavior (seconds), which would dominate the reported delivery-latency percentiles with
	///     an artificial client-side wait unrelated to server fan-out speed. Kept short so the measured
	///     latency approximates actual server delivery time.
	/// </summary>
	public int ReceivePollIntervalMs { get; init; } = 200;

	/// <summary>Gets <see cref="ReceivePollIntervalMs" /> as a <see cref="TimeSpan" />.</summary>
	public TimeSpan ReceivePollInterval => TimeSpan.FromMilliseconds(ReceivePollIntervalMs);
}

/// <summary>Settings for <see cref="Scenarios.MultiplayerScenario" />. Scale axis is rooms, not users.</summary>
public sealed class MultiplayerSettings
{
	/// <summary>Whether this scenario runs at all for the current profile.</summary>
	public bool Enabled { get; init; }

	/// <summary>
	///     Room counts to run, each as its own named NBomber scenario. The server allocates match ids from
	///     a fixed 64-slot pool, so no value here may exceed 64.
	/// </summary>
	public int[] Rooms { get; init; } = [];

	/// <summary>Players per room, at most 16 (the server's per-match slot count).</summary>
	public int PlayersPerRoom { get; init; } = 8;

	/// <summary>Rounds (create → play → complete) each room runs before parting.</summary>
	public int RoundsPerRoom { get; init; } = 3;

	/// <summary>Score-frame updates sent per second per player while a round is in progress.</summary>
	public int ScoreUpdatesPerSecond { get; init; } = 2;

	/// <summary>
	///     Path (relative to the executable) to an <c>.osz</c> to ingest and assign as the room's map.
	///     When <see langword="null" />, rooms run with no map assigned (<c>MapId = 0</c>), which still
	///     exercises the full state machine and round-row write.
	/// </summary>
	public string? BeatmapsetFixture { get; init; }

	/// <summary>Gets <see cref="RoundsPerRoom" />'s implied match duration budget, used for scenario duration.</summary>
	public int DurationSeconds { get; init; } = 180;

	/// <summary>Gets <see cref="DurationSeconds" /> as a <see cref="TimeSpan" />.</summary>
	public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);
}

/// <summary>Settings for <see cref="Scenarios.ApiScenario" />.</summary>
public sealed class ApiSettings : ScenarioSettings
{
	/// <summary>Which single-endpoint scenarios to register, by short id (health, user, match_list, match_report, beatmapset).</summary>
	public string[] Endpoints { get; init; } = [];

	/// <summary>Relative weights for the combined <c>api_mixed</c> scenario, keyed by the same endpoint ids.</summary>
	public Dictionary<string, int> MixedWeights { get; init; } = [];
}

/// <summary>Settings for <see cref="Scenarios.SseScenario" />.</summary>
public sealed class SseSettings : ScenarioSettings;

/// <summary>Settings for the Phase 3 stress scenario, which never stops on failure by design.</summary>
public sealed class StressSettings
{
	/// <summary>Whether the stress scenario runs at all for the current profile.</summary>
	public bool Enabled { get; init; }

	/// <summary>
	///     The chained ramp steps. Each value ramps from the previous step's level up to this one, then
	///     holds, before ramping to the next.
	/// </summary>
	public int[] ConcurrentUsers { get; init; } = [];

	/// <summary>How long each ramp between steps takes.</summary>
	public int RampSeconds { get; init; } = 30;

	/// <summary>How long each step is held at its target concurrency.</summary>
	public int HoldSeconds { get; init; } = 90;

	/// <summary>
	///     The failure ceiling. Phase 3 explicitly requires the run to never stop immediately after a
	///     failure, so this is set far above NBomber's 5000 default.
	/// </summary>
	public int MaxFailCount { get; init; } = int.MaxValue;

	/// <summary>Success rate, in percent, below which the server is considered unusable.</summary>
	public int UnusableSuccessRatePercent { get; init; } = 5;

	/// <summary>How long the success rate must stay below the threshold before the run stops itself.</summary>
	public int UnusableForSeconds { get; init; } = 60;

	/// <summary>CPU percent considered saturated.</summary>
	public int SaturationCpuPercent { get; init; } = 95;

	/// <summary>How long CPU must stay at or above <see cref="SaturationCpuPercent" /> to count as saturation.</summary>
	public int SaturationSeconds { get; init; } = 30;

	/// <summary>Gets <see cref="RampSeconds" /> as a <see cref="TimeSpan" />.</summary>
	public TimeSpan Ramp => TimeSpan.FromSeconds(RampSeconds);

	/// <summary>Gets <see cref="HoldSeconds" /> as a <see cref="TimeSpan" />.</summary>
	public TimeSpan Hold => TimeSpan.FromSeconds(HoldSeconds);
}

/// <summary>Settings for the Phase 4 soak scenario: a long-running weighted mix of the Phase 2 workloads.</summary>
public sealed class SoakSettings
{
	/// <summary>Whether the soak scenario runs at all for the current profile.</summary>
	public bool Enabled { get; init; }

	/// <summary>Concurrent user levels to hold for the full soak duration (typically one value, 500-1000).</summary>
	public int[] ConcurrentUsers { get; init; } = [];

	/// <summary>Total soak duration (12h = 43200, 24h = 86400).</summary>
	public int DurationSeconds { get; init; } = 43200;

	/// <summary>How much of the start is excluded from leak-slope analysis as ramp-up noise.</summary>
	public int WarmUpSeconds { get; init; } = 300;

	/// <summary>How often NBomber streams interim stats, so a multi-hour run doesn't report only at the end.</summary>
	public int ReportingIntervalSeconds { get; init; } = 300;

	/// <summary>Relative weights for each workload mixed into the soak (chat/multiplayer/api/idle).</summary>
	public Dictionary<string, int> Weights { get; init; } = [];

	/// <summary>Per-series leak-slope thresholds; a slope above the threshold with a high R² is reported as a leak.</summary>
	public Dictionary<string, double> LeakSlopeThresholds { get; init; } = [];

	/// <summary>Gets <see cref="DurationSeconds" /> as a <see cref="TimeSpan" />.</summary>
	public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);

	/// <summary>Gets <see cref="WarmUpSeconds" /> as a <see cref="TimeSpan" />.</summary>
	public TimeSpan WarmUp => TimeSpan.FromSeconds(WarmUpSeconds);

	/// <summary>Gets <see cref="ReportingIntervalSeconds" /> as a <see cref="TimeSpan" />.</summary>
	public TimeSpan ReportingInterval => TimeSpan.FromSeconds(ReportingIntervalSeconds);
}