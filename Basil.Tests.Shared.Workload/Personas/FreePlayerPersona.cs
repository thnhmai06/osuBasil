using Basil.Domain.Spectating;
using Basil.Tests.Shared.Clients.OsuWeb;
using Basil.Tests.Shared.Clients.Stable;
using Basil.Tests.Shared.Workload.Outcomes;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>Simulates a solo player: signs in, reads leaderboards, plays beatmaps, submits scores with replays, reads replays and chats.</summary>
public sealed class FreePlayerPersona(WorkloadContext workload)
{
	/// <summary>Runs one solo-play cycle for one virtual user.</summary>
	public async Task RunCycleAsync(IScenarioContext context)
	{
		using var linked = workload.LinkToken(context);
		var ct = linked.Token;
		var random = SeededRandom.For(workload.Seed, context.ScenarioInfo.ScenarioName,
			context.ScenarioInfo.InstanceNumber, context.InvocationNumber);

		if (!workload.Accounts.TryAcquire(AccountRole.Player, out var lease))
		{
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
			return;
		}

		using (lease)
		{
			var identity = lease.Identity();
			var stable = new StableClient(workload.Endpoint, workload.Http, identity);
			var web = new OsuWebClient(workload.Endpoint, workload.Http, identity);

			var login = await workload.LoginStableAsync(stable, context, ct);
			if (login is not { Accepted: true }) return;

			try
			{
				stable.Enqueue(ClientPackets.ChannelJoin("#osu"));
				await workload.PollStableAsync(stable, context, ct);

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
					await StepExecution.RunAsync(Channels.GetScores, context, workload.Outcomes, async () =>
					{
						var reply = await web.GetReplayAsync(scoreId, ct);
						return OutcomeClassifier.FromHttpStatus(reply.Status, refusalExpected: reply.Status == System.Net.HttpStatusCode.NotFound,
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
			finally
			{
				await workload.LogoutStableAsync(stable, context);
			}
		}
	}
}
