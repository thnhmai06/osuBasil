using System.Collections.Concurrent;
using System.Net;
using Basil.Domain.Spectating;
using Basil.Tests.Shared.Clients.OsuWeb;
using Basil.Tests.Shared.Clients.Stable;
using Basil.Tests.Shared.Workload.Outcomes;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>Simulates a solo player: each scenario copy signs in once and keeps the session, then every invocation reads leaderboards, plays one beatmap, submits the score with its replay, reads the replay and chats.</summary>
public sealed class FreePlayerPersona(WorkloadContext workload)
{
	private readonly ConcurrentDictionary<int, Held> _held = new();
	private volatile bool _closing;

	/// <summary>Runs one solo play for the copy's signed-in player, signing in first when the copy has no live session.</summary>
	public async Task RunCycleAsync(IScenarioContext context)
	{
		if (_closing) return;

		using var linked = workload.LinkToken(context);
		var ct = linked.Token;
		var instance = context.ScenarioInfo.InstanceNumber;
		var random = SeededRandom.For(workload.Seed, context.ScenarioInfo.ScenarioName, instance, context.InvocationNumber);

		var held = await EnsureSessionAsync(instance, context, ct);
		if (held is null)
		{
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
			return;
		}

		var stable = held.Stable;
		var web = held.Web;
		var identity = stable.Identity;
		var map = workload.PickBeatmap(random);

		// Read leaderboard before play.
		var (_, board) = await StepExecution.RunAsync(Channels.GetScores, context, workload.Outcomes, async () =>
		{
			var reply = await web.GetScoresAsync(map.Md5, map.Mode, 0, 1, ct);
			return (OutcomeClassifier.FromHttpStatus(reply.Status), reply.Body);
		}, ct);

		if (board is { Rows.Count: > 0 })
			workload.World.RecordScoreId(board.Rows[0].ScoreId);

		// Enter play and send periodic polls + spectator frames.
		stable.Enqueue(ClientPackets.ChangeAction(WorkloadContext.PlayingAction(map)));
		workload.World.PutOnline(new OnlineUser(stable.UserId, identity.Name, true, map.Md5));

		var ticks = Math.Max(2, (int)(workload.RoundDuration.TotalSeconds / Math.Max(0.5, workload.PollInterval.TotalSeconds)));
		for (var tick = 0; tick < ticks && !ct.IsCancellationRequested; tick++)
		{
			var action = tick == 0 ? SpectatorAction.NewSong : SpectatorAction.Standard;
			stable.Enqueue(ClientPackets.SpectateFrames(WorkloadContext.BuildSpectatorBundle(tick, 0, random, action)));
			await workload.PollStableAsync(stable, context, ct);
			await WorkloadContext.SafeDelayAsync(workload.PollInterval, ct);
		}

		if (ct.IsCancellationRequested) return;

		stable.Enqueue(
			ClientPackets.SpectateFrames(WorkloadContext.BuildSpectatorBundle(ticks, 0, random, SpectatorAction.Completion)),
			ClientPackets.ChangeAction(WorkloadContext.IdleAction()));
		await workload.PollStableAsync(stable, context, ct);
		workload.World.PutOnline(new OnlineUser(stable.UserId, identity.Name, false, string.Empty));

		// Submit score with replay.
		var score = PlayedScore.Generate(random, map.Md5, passed: true, mode: map.Mode, mods: 0,
			storyboardMd5: map.StoryboardMd5);
		var replay = SampleFiles.Replay(random);
		await StepExecution.RunAsync(Channels.Submit, context, workload.Outcomes, async () =>
		{
			var reply = await web.SubmitScoreAsync(score, replay, ct);
			return OutcomeClassifier.FromScoreSubmission(reply.Status, reply.Body);
		}, ct);

		// Re-read leaderboard and optionally fetch a replay.
		var (_, refreshed) = await StepExecution.RunAsync(Channels.GetScores, context, workload.Outcomes, async () =>
		{
			var reply = await web.GetScoresAsync(map.Md5, map.Mode, 0, 1, ct);
			return (OutcomeClassifier.FromHttpStatus(reply.Status), reply.Body);
		}, ct);

		var scoreId = refreshed is { Rows.Count: > 0 }
			? refreshed.Rows[0].ScoreId
			: workload.World.PickScoreId(random);
		if (scoreId > 0)
		{
			workload.World.RecordScoreId(scoreId);
			await StepExecution.RunAsync(Channels.Replay, context, workload.Outcomes, async () =>
			{
				var reply = await web.GetReplayAsync(scoreId, ct);
				return OutcomeClassifier.FromHttpStatus(reply.Status, refusalExpected: reply.Status == HttpStatusCode.NotFound,
					bytes: reply.Body);
			}, ct);
		}

		// Occasional public chat message.
		if (random.Next(4) == 0)
		{
			stable.Enqueue(ClientPackets.SendPublicMessage("#osu", $"gg {map.Version}"));
			await workload.PollStableAsync(stable, context, ct);
		}
	}

	/// <summary>Signs out every session this persona still holds and returns every account lease; best effort, never throws.</summary>
	public async Task ShutdownAsync()
	{
		_closing = true;
		var all = _held.Values.ToArray();
		_held.Clear();
		await Task.WhenAll(all.Select(CloseAsync));
	}

	private async Task<Held?> EnsureSessionAsync(int instance, IScenarioContext context, CancellationToken ct)
	{
		if (_held.TryGetValue(instance, out var existing))
		{
			if (!workload.IsDropped(existing.Stable)) return existing;

			_held.TryRemove(instance, out _);
			await CloseAsync(existing);
		}

		if (!workload.Accounts.TryAcquire(AccountRole.Player, out var lease)) return null;

		var identity = lease.Identity();
		var stable = new StableClient(workload.Endpoint, workload.Http, identity);
		var login = await workload.LoginStableAsync(stable, context, ct, lease: lease);
		if (login is not { Accepted: true })
		{
			lease.Dispose();
			return null;
		}

		stable.Enqueue(ClientPackets.ChannelJoin("#osu"));
		await workload.PollStableAsync(stable, context, ct);

		var held = new Held(lease, stable, new OsuWebClient(workload.Endpoint, workload.Http, identity));
		_held[instance] = held;
		return held;
	}

	private async Task CloseAsync(Held held)
	{
		await workload.SignOutQuietAsync(held.Stable);
		held.Lease.Dispose();
	}

	private sealed record Held(AccountLease Lease, StableClient Stable, OsuWebClient Web);
}
