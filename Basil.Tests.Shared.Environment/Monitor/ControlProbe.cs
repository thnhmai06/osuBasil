using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Basil.Tests.Shared.Clients;
using Basil.Tests.Shared.Clients.Irc;
using Basil.Tests.Shared.Clients.OsuWeb;
using Basil.Tests.Shared.Clients.Rest;
using Basil.Tests.Shared.Clients.Stable;

namespace Basil.Tests.Shared.Environment.Monitor;

/// <summary>One timestamped latency sample recorded by the control node (espresso).</summary>
public sealed record ControlSample(
	DateTimeOffset At,
	string Channel,
	double LatencyMs,
	bool Ok,
	string Detail);

/// <summary>
///     Runs a steady low-rate control probe (poll, getscores, REST, IRC) from espresso alongside every measured run
///     to distinguish server saturation from generator saturation.
/// </summary>
public sealed class ControlProbe(
	Endpoint endpoint,
	int ircPort,
	HttpClient http,
	string username,
	string password,
	string beatmapMd5,
	int beatmapMode)
{
	/// <summary>
	///     Runs the probe for <paramref name="duration" /> and writes <c>control-trace.jsonl</c> into
	///     <paramref name="reportDirectory" />.
	/// </summary>
	public async Task RunAsync(TimeSpan duration, TimeSpan interval, string reportDirectory, CancellationToken ct)
	{
		Directory.CreateDirectory(reportDirectory);
		var tracePath = Path.Combine(reportDirectory, "control-trace.jsonl");
		await using var writer = new StreamWriter(tracePath, false, new UTF8Encoding(false));
		writer.AutoFlush = true;

		var identity = StableIdentity.Create(username, password);
		var stable = new StableClient(endpoint, http, identity);
		var web = new OsuWebClient(endpoint, http, identity);
		var rest = new RestClient(endpoint, http);
		await using var irc = new IrcClient(endpoint, ircPort, username, password);

		var login = await stable.LoginAsync(ct);
		var restLogin = await rest.SignInAsync(username, password, ct);
		var ircReg = await irc.ConnectAsync(TimeSpan.FromSeconds(10), ct);
		if (ircReg.Welcomed) await irc.JoinAsync("#osu", ct);

		var deadline = DateTimeOffset.UtcNow + duration;
		try
		{
			while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
			{
				if (login.Accepted)
				{
					await ProbeAsync(writer, "poll", async () =>
					{
						var r = await stable.PollAsync(ct);
						return (r.IsSuccess, ((int)r.Status).ToString());
					});
					await ProbeAsync(writer, "getscores", async () =>
					{
						var r = await web.GetScoresAsync(beatmapMd5, beatmapMode, 0, 1, ct);
						return (r.IsSuccess, ((int)r.Status).ToString());
					});
				}

				if (restLogin.Success)
					await ProbeAsync(writer, "rest", async () =>
					{
						var r = await rest.GetAsync("/v1/rooms?limit=10", ct);
						return (r.Success, ((int)r.Status).ToString());
					});

				if (ircReg.Welcomed)
					await ProbeAsync(writer, "irc", async () =>
					{
						var r = await irc.TopicAsync("#osu", TimeSpan.FromSeconds(10), ct);
						return (r is not null, r?.Command ?? "timeout");
					});

				await Task.Delay(interval, ct);
			}
		}
		catch (OperationCanceledException)
		{
			// Completed or cancelled.
		}
		finally
		{
			try
			{
				using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
				if (stable.Token is not null) await stable.LogoutAsync(timeout.Token);
				if (rest.Token is not null) await rest.SignOutAsync(timeout.Token);
			}
			catch (Exception)
			{
				// Cleanup.
			}
		}
	}

	private static async Task ProbeAsync(StreamWriter writer, string channel,
		Func<Task<(bool Ok, string Detail)>> call)
	{
		var started = Stopwatch.GetTimestamp();
		bool ok;
		string detail;
		try
		{
			(ok, detail) = await call();
		}
		catch (Exception ex)
		{
			ok = false;
			detail = ex.GetType().Name;
		}

		var ms = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 2);
		var sample = new ControlSample(DateTimeOffset.UtcNow, channel, ms, ok, detail);
		await writer.WriteLineAsync(JsonSerializer.Serialize(sample));
	}
}