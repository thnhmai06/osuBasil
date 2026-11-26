using System.Runtime.CompilerServices;
using System.Text.Json;
using Basil.Tests.Shared.Clients;
using Basil.Tests.Shared.Clients.Rest;
using Basil.Tests.Shared.Environment.Monitor;

namespace Basil.Tests.Shared.Environment.Checks;

/// <summary>The verdict of one server-side check.</summary>
/// <param name="Name">What was checked.</param>
/// <param name="Passed">Whether the server state matched the expectation.</param>
/// <param name="Detail">The numbers behind the verdict.</param>
public sealed record CheckResult(string Name, bool Passed, string Detail);

/// <summary>Server-side checks of a smoke run (plan T8), made through the public REST API and the diagnostics feed only: the matches played rounds, every accepted score is stored exactly once, and nothing is left behind.</summary>
public sealed class SmokeChecks(Endpoint endpoint, HttpClient http, string user, string password, string accountPrefix = "load_")
{
	private const int PageSize = 100;

	// The client and the server clocks may differ a little; the database starts every run without scores or matches.
	private static readonly TimeSpan ClockMargin = TimeSpan.FromMinutes(2);

	/// <summary>Runs the three checks.</summary>
	/// <param name="runStart">When the run started, on the client's clock.</param>
	/// <param name="scoresAccepted">How many score submissions the server answered with success.</param>
	/// <param name="minMatchesWithRounds">How many matches must have at least one finished round.</param>
	/// <param name="monitor">The diagnostics monitor that holds the users online before the run and now; <see langword="null"/> skips that part.</param>
	/// <param name="settle">How long the server may take to close the rooms and sessions after the load stopped.</param>
	/// <param name="ct">Stops the checks.</param>
	public async Task<IReadOnlyList<CheckResult>> RunAsync(DateTimeOffset runStart, long scoresAccepted,
		int minMatchesWithRounds, DiagnosticsMonitor? monitor, TimeSpan settle, CancellationToken ct)
	{
		var rest = new RestClient(endpoint, http);
		var login = await rest.SignInAsync(user, password, ct);
		if (!login.Success)
			return [new CheckResult("sign-in", false, $"The checking account could not sign in ({(int)login.Status}).")];

		try
		{
			var since = runStart - ClockMargin;
			return
			[
				await MatchesAsync(rest, since, minMatchesWithRounds, ct),
				await ScoresAsync(rest, since, scoresAccepted, ct),
				await LeftoversAsync(rest, monitor, settle, ct)
			];
		}
		finally
		{
			try
			{
				using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
				await rest.SignOutAsync(timeout.Token);
			}
			catch (Exception)
			{
				// Best effort.
			}
		}
	}

	private async Task<CheckResult> MatchesAsync(RestClient rest, DateTimeOffset since, int minimum, CancellationToken ct)
	{
		var withRounds = 0;
		var withoutRounds = 0;
		await foreach (var match in PagedAsync(rest, "/v1/matches", ct))
		{
			if (!IsLoadCreator(match) || Time(match, "startedAt") < since) continue;
			if (match.TryGetProperty("endedAt", out var ended) && ended.ValueKind == JsonValueKind.Null)
			{
				withoutRounds++; // still open; the leftovers check reports it as well
				continue;
			}

			if (await FinishedRoundsAsync(rest, match.GetProperty("id").GetInt32(), ct) > 0) withRounds++;
			else withoutRounds++;
		}

		return new CheckResult("matches have finished rounds", withRounds >= minimum,
			$"{withRounds} matches with finished rounds (need {minimum}), {withoutRounds} without.");
	}

	private static async Task<int> FinishedRoundsAsync(RestClient rest, int matchId, CancellationToken ct)
	{
		// An ended match answers one snapshot event with its whole report, then the stream ends.
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(TimeSpan.FromSeconds(10));
		using var stream = await rest.OpenStreamAsync($"/v1/matches/{matchId}", authorize: false, timeout.Token);
		await foreach (var ev in stream.ReadAsync(timeout.Token))
		{
			using var document = JsonDocument.Parse(ev.Data);
			var root = document.RootElement;
			if (root.TryGetProperty("data", out var data)) root = data;
			if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array) return 0;

			return entries.EnumerateArray().Count(e =>
				e.TryGetProperty("type", out var type) && type.GetString() == "round" &&
				e.TryGetProperty("aborted", out var aborted) && aborted.ValueKind == JsonValueKind.False &&
				e.TryGetProperty("endedAt", out var endedAt) && endedAt.ValueKind == JsonValueKind.String);
		}

		return 0;
	}

	private static async Task<CheckResult> ScoresAsync(RestClient rest, DateTimeOffset since, long accepted,
		CancellationToken ct)
	{
		var seen = new HashSet<string>();
		var stored = 0L;
		var duplicates = 0L;
		await foreach (var score in PagedAsync(rest, "/v1/scores", ct))
		{
			if (Time(score, "timestamp") < since) break; // newest first
			stored++;
			var key = string.Join('|', Text(score, "userId"), Text(score, "beatmapHash"), Text(score, "totalScore"),
				Text(score, "maxCombo"), Text(score, "timestamp"));
			if (!seen.Add(key)) duplicates++;
		}

		return new CheckResult("each score is stored exactly once", stored == accepted && duplicates == 0,
			$"{accepted} submissions accepted, {stored} scores stored, {duplicates} duplicates.");
	}

	private async Task<CheckResult> LeftoversAsync(RestClient rest, DiagnosticsMonitor? monitor, TimeSpan settle,
		CancellationToken ct)
	{
		var deadline = DateTimeOffset.UtcNow + settle;
		int openRooms;
		ServerMetricsSummary? server;
		bool clean;
		while (true)
		{
			openRooms = 0;
			await foreach (var room in PagedAsync(rest, "/v1/rooms", ct))
				if (IsLoadCreator(room))
					openRooms++;

			server = monitor?.Summary();
			clean = openRooms == 0 && (server is null ||
			                           (server.FinalUsersOnline <= server.BaselineUsersOnline && server.FinalOpenRooms == 0));
			if (clean || DateTimeOffset.UtcNow >= deadline) break;

			await Task.Delay(TimeSpan.FromSeconds(2), ct);
		}

		var users = server is null
			? "users online not measured"
			: $"{server.FinalUsersOnline} users online (before the run: {server.BaselineUsersOnline}), {server.FinalOpenRooms} rooms by diagnostics";
		return new CheckResult("no session or room is left over", clean,
			$"{openRooms} open rooms of the load accounts, {users}.");
	}

	private static async IAsyncEnumerable<JsonElement> PagedAsync(RestClient rest, string path,
		[EnumeratorCancellation] CancellationToken ct)
	{
		for (var offset = 0;; offset += PageSize)
		{
			var reply = await rest.GetAsync($"{path}?offset={offset}&limit={PageSize}", ct);
			if (!reply.Success || reply.Data.ValueKind != JsonValueKind.Array) yield break;

			foreach (var item in reply.Data.EnumerateArray().Select(e => e.Clone())) yield return item;

			if (reply.Meta.ValueKind != JsonValueKind.Object || !reply.Meta.TryGetProperty("hasNext", out var next) ||
			    next.ValueKind != JsonValueKind.True)
				yield break;
		}
	}

	private bool IsLoadCreator(JsonElement item)
	{
		return item.TryGetProperty("creator", out var creator) && creator.TryGetProperty("name", out var name) &&
		       name.GetString() is { } text && text.StartsWith(accountPrefix, StringComparison.Ordinal);
	}

	private static DateTimeOffset Time(JsonElement item, string property)
	{
		return item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
		       value.TryGetDateTimeOffset(out var time)
			? time
			: DateTimeOffset.MinValue;
	}

	private static string Text(JsonElement item, string property)
	{
		return item.TryGetProperty(property, out var value) ? value.ToString() : string.Empty;
	}
}
