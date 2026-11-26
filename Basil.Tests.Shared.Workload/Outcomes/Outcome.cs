using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Basil.Protocol.Irc.Models;
using Basil.Tests.Shared.Clients.Irc;
using Basil.Tests.Shared.Clients.Rest;
using Basil.Tests.Shared.Clients.Stable;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.Tests.Shared.Workload.Outcomes;

/// <summary>The five outcome classes of a load test action; only <see cref="Error" /> counts against the SLO.</summary>
public enum Outcome
{
	/// <summary>The request succeeded as expected.</summary>
	Ok,

	/// <summary>A business rule refused the request, as expected.</summary>
	Refused,

	/// <summary>The request was rate-limited (HTTP 429).</summary>
	Throttled,

	/// <summary>The server shed the request under load (HTTP 503).</summary>
	Shed,

	/// <summary>An unexpected server error (5xx other than 503), timeout, protocol mismatch, or dropped IRC/SSE stream.</summary>
	Error
}

/// <summary>The channel step names reported by NBomber.</summary>
public static class Channels
{
	public const string Login = "login";

	public const string Poll = "poll";

	public const string Submit = "submit";

	public const string GetScores = "getscores";

	public const string Rest = "rest";

	public const string Irc = "irc";

	public const string Sse = "sse";

	public const string Download = "download";

	public const string Upload = "upload";

	/// <summary>Osu-web reads that are not leaderboards: friends, seasonals, search, search-set and difficulty-rating.</summary>
	public const string OsuWeb = "osuweb";

	/// <summary>Replay reads.</summary>
	public const string Replay = "replay";

	/// <summary>Reads from the asset hosts: avatar, cover, thumbnail, preview, menu image and screenshot.</summary>
	public const string Asset = "asset";

	/// <summary>Every sign-out.</summary>
	public const string Logout = "logout";

	/// <summary>Deliberate refusal checks of the edge-case persona.</summary>
	public const string Edge = "edge";

	/// <summary>
	///     Gets the step that holds the throttled and shed answers of a channel, so their fast latency never enters the
	///     channel's own percentiles.
	/// </summary>
	public static string Limited(string channel)
	{
		return $"{channel}.limited";
	}
}

/// <summary>Result of one measured channel step.</summary>
/// <param name="Outcome">The classified outcome.</param>
/// <param name="Detail">A short status code or reason tag for the report.</param>
/// <param name="Bytes">Optional payload size in bytes.</param>
public readonly record struct StepOutcome(Outcome Outcome, string Detail = "", long Bytes = 0)
{
	public static StepOutcome Ok(string detail = "ok", long bytes = 0)
	{
		return new StepOutcome(Outcome.Ok, detail, bytes);
	}

	public static StepOutcome Refused(string detail = "refused")
	{
		return new StepOutcome(Outcome.Refused, detail);
	}

	public static StepOutcome Throttled(string detail = "429")
	{
		return new StepOutcome(Outcome.Throttled, detail);
	}

	public static StepOutcome Shed(string detail = "503")
	{
		return new StepOutcome(Outcome.Shed, detail);
	}

	public static StepOutcome Error(string detail)
	{
		return new StepOutcome(Outcome.Error, detail);
	}
}

/// <summary>Thread-safe tally of outcomes per channel for the run report.</summary>
public sealed class OutcomeTally
{
	private readonly ConcurrentDictionary<(string Channel, Outcome Outcome), long> _counts = new();

	/// <summary>Records one outcome for a channel.</summary>
	public void Record(string channel, Outcome outcome)
	{
		_counts.AddOrUpdate((channel, outcome), 1, static (_, count) => count + 1);
	}

	/// <summary>Returns a point-in-time breakdown grouped by channel and outcome name.</summary>
	public IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> Snapshot()
	{
		var byChannel = new SortedDictionary<string, IReadOnlyDictionary<string, long>>(StringComparer.Ordinal);
		foreach (var group in _counts.GroupBy(pair => pair.Key.Channel))
		{
			var inner = new SortedDictionary<string, long>(StringComparer.Ordinal);
			foreach (var item in group)
				inner[item.Key.Outcome.ToString().ToLowerInvariant()] = item.Value;
			byChannel[group.Key] = inner;
		}

		return byChannel;
	}

	/// <summary>Gets the total count of a given outcome across every channel.</summary>
	public long Total(Outcome outcome)
	{
		return _counts.Where(pair => pair.Key.Outcome == outcome).Sum(pair => pair.Value);
	}
}

/// <summary>Classifies HTTP, osu! stable, REST and IRC answers into <see cref="StepOutcome" />.</summary>
public static class OutcomeClassifier
{
	/// <summary>
	///     Classifies a raw HTTP status code; 4xx (other than 429) is <see cref="Outcome.Refused" /> when expected, or
	///     <see cref="Outcome.Error" /> otherwise.
	/// </summary>
	public static StepOutcome FromHttpStatus(HttpStatusCode status, bool refusalExpected = false, long bytes = 0)
	{
		var code = (int)status;
		var text = code.ToString();
		return code switch
		{
			>= 200 and < 300 => StepOutcome.Ok(text, bytes),
			429 => StepOutcome.Throttled(text),
			503 => StepOutcome.Shed(text),
			>= 400 and < 500 when refusalExpected => StepOutcome.Refused(text),
			>= 400 and < 500 => StepOutcome.Error($"http_{code}"),
			_ => StepOutcome.Error($"http_{code}")
		};
	}

	/// <summary>Classifies an osu! stable sign-in result.</summary>
	public static StepOutcome FromStableLogin(StableLoginResult result, bool refusalExpected = false)
	{
		var code = (int)result.Status;
		switch (code)
		{
			case 429:
				return StepOutcome.Throttled();
			case 503:
				return StepOutcome.Shed();
			case < 200 or >= 300:
				return StepOutcome.Error($"http_{code}");
		}

		if (result.Accepted) return StepOutcome.Ok("200");
		return refusalExpected
			? StepOutcome.Refused($"login_{result.UserId}")
			: StepOutcome.Error($"login_refused_{result.UserId}");
	}

	/// <summary>
	///     Classifies an osu! stable poll response; a restart answer means the server dropped the session and is an
	///     <see cref="Outcome.Error" />.
	/// </summary>
	public static StepOutcome FromStableExchange(StableExchange exchange)
	{
		var http = FromHttpStatus(exchange.Status);
		return http.Outcome == Outcome.Ok && IsDropped(exchange) ? StepOutcome.Error("session_dropped") : http;
	}

	/// <summary>
	///     Gets whether the server answered a poll with the restart packet, which it sends for a token it does not know
	///     or has closed.
	/// </summary>
	public static bool IsDropped(StableExchange exchange)
	{
		return exchange.Packets.OfType<ServerRestarting>().Any();
	}

	/// <summary>Classifies a REST API reply.</summary>
	public static StepOutcome FromRest(RestReply reply, bool refusalExpected = false)
	{
		var code = (int)reply.Status;
		if (code == 429) return StepOutcome.Throttled();
		if (code == 503) return StepOutcome.Shed();
		if (code is >= 200 and < 300 && reply.Success) return StepOutcome.Ok(code.ToString());
		if (code is >= 400 and < 500 && refusalExpected)
			return StepOutcome.Refused(reply.ErrorCode ?? code.ToString());

		return StepOutcome.Error(reply.ErrorCode is { Length: > 0 } err ? $"{code}:{err}" : $"http_{code}");
	}

	/// <summary>Classifies a score submission text answer.</summary>
	public static StepOutcome FromScoreSubmission(HttpStatusCode status, string body, bool refusalExpected = false)
	{
		var http = FromHttpStatus(status, refusalExpected);
		if (http.Outcome != Outcome.Ok) return http;

		var refused = body.StartsWith("error:", StringComparison.OrdinalIgnoreCase);
		if (!refused) return StepOutcome.Ok("submitted");

		return refusalExpected ? StepOutcome.Refused(body.Trim()) : StepOutcome.Error(body.Trim());
	}

	/// <summary>Classifies an IRC registration attempt.</summary>
	public static StepOutcome FromIrcRegistration(IrcRegistration registration, bool refusalExpected = false)
	{
		if (registration.Welcomed) return StepOutcome.Ok("001");
		if (registration.Line is null) return StepOutcome.Error("irc_timeout");

		return refusalExpected
			? StepOutcome.Refused($"irc_{registration.Line.Command}")
			: StepOutcome.Error($"irc_{registration.Line.Command}");
	}

	/// <summary>Classifies an IRC command reply.</summary>
	public static StepOutcome FromIrcReply(IrcMessage? reply, bool refusalExpected = false)
	{
		if (reply is null) return StepOutcome.Error("irc_timeout");
		var isRefusal = reply.Command is ['4' or '5', _, _];
		if (!isRefusal) return StepOutcome.Ok(reply.Command);

		return refusalExpected
			? StepOutcome.Refused($"irc_{reply.Command}")
			: StepOutcome.Error($"irc_{reply.Command}");
	}
}

/// <summary>Runs one action as an NBomber step on the named channel and records its classified outcome.</summary>
public static class StepExecution
{
	/// <summary>Executes <paramref name="action" /> and reports it to NBomber under <paramref name="channel" />.</summary>
	/// <remarks>
	///     The action is timed here and its real latency is reported afterwards, so a throttled or shed answer can go to
	///     <see cref="Channels.Limited" /> without its fast latency entering the percentiles of the judged channel.
	///     Only <see cref="Outcome.Error" /> is reported as a failed NBomber response, so NBomber's failure percentage matches
	///     the SLO definition.
	///     Nothing runs or is recorded when <paramref name="ct" /> is already cancelled, and a cancellation during the action
	///     is not recorded;
	///     both return an <see cref="Outcome.Error" /> tagged <c>cancelled</c> that is not part of the tally.
	/// </remarks>
	public static async Task<(StepOutcome Outcome, T? Value)> RunAsync<T>(
		string channel,
		IScenarioContext context,
		OutcomeTally tally,
		Func<Task<(StepOutcome Outcome, T? Value)>> action,
		CancellationToken ct)
	{
		if (ct.IsCancellationRequested) return (StepOutcome.Error("cancelled"), default);

		StepOutcome recorded;
		T? captured = default;
		var started = Stopwatch.GetTimestamp();

		try
		{
			(recorded, captured) = await action();
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			return (StepOutcome.Error("cancelled"), default);
		}
		catch (TimeoutException)
		{
			recorded = StepOutcome.Error("timeout");
		}
		catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
			                           or SocketException or AuthenticationException or OperationCanceledException)
		{
			recorded = StepOutcome.Error(ex.GetType().Name);
		}

		var latencyMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
		tally.Record(channel, recorded.Outcome);
		var size = (int)Math.Clamp(recorded.Bytes, 0, int.MaxValue);
		var name = recorded.Outcome.ToString().ToLowerInvariant();
		var tag = string.IsNullOrEmpty(recorded.Detail) ? name : $"{name}:{recorded.Detail}";
		var step = recorded.Outcome is Outcome.Throttled or Outcome.Shed ? Channels.Limited(channel) : channel;

		await Step.Run(step, context, () => Task.FromResult(recorded.Outcome == Outcome.Error
			? Response.Fail(statusCode: tag, message: recorded.Detail, customLatencyMs: latencyMs)
			: Response.Ok(statusCode: tag, size, customLatencyMs: latencyMs)));

		return (recorded, captured);
	}

	/// <summary>Executes a value-less action inside an NBomber step named <paramref name="channel" />.</summary>
	public static async Task<StepOutcome> RunAsync(
		string channel,
		IScenarioContext context,
		OutcomeTally tally,
		Func<Task<StepOutcome>> action,
		CancellationToken ct)
	{
		var (outcome, _) = await RunAsync<object?>(channel, context, tally, async () => (await action(), null), ct);
		return outcome;
	}
}