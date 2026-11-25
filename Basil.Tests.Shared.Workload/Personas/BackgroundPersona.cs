using System.Net;
using Basil.Tests.Shared.Clients.OsuWeb;
using Basil.Tests.Shared.Clients.Rest;
using Basil.Tests.Shared.Clients.Stable;
using Basil.Tests.Shared.Workload.Outcomes;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>Covers every secondary feature at a low real rate: presence, friends, PMs, channels, search, /d/, difficulty-rating, assets, avatar/screenshot uploads, BasilBot and REST reads.</summary>
public sealed class BackgroundPersona(WorkloadContext workload)
{
	private static readonly string[] CoverVariants = ["cover", "card", "list", "slimcover"];

	/// <summary>Runs one rotating background activity cycle.</summary>
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
			var assets = new AssetClient(workload.Endpoint, workload.Http);
			var rest = new RestClient(workload.Endpoint, workload.Http);

			var login = await workload.LoginStableAsync(stable, context, ct);
			if (login is not { Accepted: true }) return;

			try
			{
				var map = workload.PickBeatmap(random);
				var set = workload.PickBeatmapset(random);

				// 1. Presence, stats, friends, PMs, channels, BasilBot via osu! stable.
				var onlineIds = workload.World.PickOnlineUserIds(random, 8);
				if (onlineIds.Count > 0)
				{
					stable.Enqueue(
						ClientPackets.UserPresenceRequest(onlineIds),
						ClientPackets.UserStatsRequest(onlineIds));
				}
				else
				{
					stable.Enqueue(ClientPackets.UserPresenceRequestAll());
				}

				stable.Enqueue(ClientPackets.ChannelJoin("#osu"));
				await workload.PollStableAsync(stable, context, ct);

				var peer = workload.World.PickOnlineUser(random, stable.UserId);
				if (peer is not null)
				{
					stable.Enqueue(
						ClientPackets.FriendAdd(peer.UserId),
						ClientPackets.SendPrivateMessage(peer.Name, "glhf"),
						ClientPackets.FriendRemove(peer.UserId));
					await workload.PollStableAsync(stable, context, ct);
				}

				var botCmd = (random.Next(4)) switch
				{
					0 => "!roll",
					1 => "!help",
					2 => $"!where {peer?.Name ?? identity.Name}",
					_ => workload.WorldData.Faqs.Count > 0
						? $"!faq {workload.WorldData.Faqs[random.Next(workload.WorldData.Faqs.Count)]}"
						: "!faq list"
				};
				stable.Enqueue(ClientPackets.SendPublicMessage("#osu", botCmd));
				await workload.PollStableAsync(stable, context, ct);

				// 2. osu-web reads: friends, seasonals, search, search-set, difficulty-rating.
				await StepExecution.RunAsync(Channels.GetScores, context, workload.Outcomes, async () =>
				{
					var r = await web.GetFriendsAsync(ct);
					return OutcomeClassifier.FromHttpStatus(r.Status);
				}, ct);

				await StepExecution.RunAsync(Channels.GetScores, context, workload.Outcomes, async () =>
				{
					var r = await web.GetSeasonalsAsync(ct);
					return OutcomeClassifier.FromHttpStatus(r.Status);
				}, ct);

				await StepExecution.RunAsync(Channels.GetScores, context, workload.Outcomes, async () =>
				{
					var r = await web.SearchAsync(set.Artist.Split(' ')[0], status: 4, mode: -1, page: 0, ct);
					return OutcomeClassifier.FromHttpStatus(r.Status);
				}, ct);

				await StepExecution.RunAsync(Channels.GetScores, context, workload.Outcomes, async () =>
				{
					var r = await web.SearchSetAsync(set.Id, ct);
					return OutcomeClassifier.FromHttpStatus(r.Status);
				}, ct);

				await StepExecution.RunAsync(Channels.GetScores, context, workload.Outcomes, async () =>
				{
					var r = await web.GetDifficultyRatingAsync(map.Id, mods: 0, ct);
					return OutcomeClassifier.FromHttpStatus(r.Status);
				}, ct);

				// 3. Asset reads (avatar, cover, thumbnail, preview, seasonal, screenshot).
				await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
				{
					var r = await assets.GetAvatarAsync(peer?.UserId ?? stable.UserId, ct);
					return OutcomeClassifier.FromHttpStatus(r.Status, bytes: r.Body);
				}, ct);

				await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
				{
					var variant = CoverVariants[random.Next(CoverVariants.Length)];
					var r = await assets.GetCoverAsync(set.Id, variant, ct);
					return OutcomeClassifier.FromHttpStatus(r.Status, refusalExpected: r.Status == HttpStatusCode.NotFound, bytes: r.Body);
				}, ct);

				await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
				{
					var r = await assets.GetThumbnailAsync(set.Id, large: random.Next(2) == 0, ct);
					return OutcomeClassifier.FromHttpStatus(r.Status, refusalExpected: r.Status == HttpStatusCode.NotFound, bytes: r.Body);
				}, ct);

				await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
				{
					var r = await assets.GetPreviewAsync(set.Id, ct);
					return OutcomeClassifier.FromHttpStatus(r.Status, refusalExpected: r.Status == HttpStatusCode.NotFound, bytes: r.Body);
				}, ct);

				if (workload.WorldData.Seasonals.Count > 0)
				{
					var seasonal = workload.WorldData.Seasonals[random.Next(workload.WorldData.Seasonals.Count)];
					await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
					{
						var r = await assets.GetMenuImageAsync($"seasonals/{seasonal}", ct);
						return OutcomeClassifier.FromHttpStatus(r.Status, refusalExpected: r.Status == HttpStatusCode.NotFound, bytes: r.Body);
					}, ct);
				}

				// 4. Low-rate /d/ no-video download (reported separately under "download").
				if (context.InvocationNumber % 4 == 0)
				{
					await StepExecution.RunAsync(Channels.Download, context, workload.Outcomes, async () =>
					{
						var r = await web.DownloadSetAsync(set.Id, withVideo: false, ct);
						return OutcomeClassifier.FromHttpStatus(r.Status, bytes: r.Body);
					}, ct);
				}

				// 5. Low-rate screenshot upload and read (reported separately under "upload").
				if (context.InvocationNumber % 5 == 0)
				{
					var (_, fileName) = await StepExecution.RunAsync(Channels.Upload, context, workload.Outcomes, async () =>
					{
						var r = await web.UploadScreenshotAsync(SampleFiles.Png, ct);
						return (OutcomeClassifier.FromHttpStatus(r.Status, bytes: SampleFiles.Png.Length), r.Body.Trim());
					}, ct);

					if (!string.IsNullOrWhiteSpace(fileName))
						workload.World.RecordScreenshot(fileName);
				}

				if (workload.World.PickScreenshot(random) is { } existingScreenshot)
				{
					await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
					{
						var r = await assets.GetScreenshotAsync(existingScreenshot, ct);
						return OutcomeClassifier.FromHttpStatus(r.Status, refusalExpected: r.Status == HttpStatusCode.NotFound, bytes: r.Body);
					}, ct);
				}

				// 6. REST reads and occasional avatar upload.
				var restLogin = await StepExecution.RunAsync(Channels.Login, context, workload.Outcomes, async () =>
				{
					var r = await rest.SignInAsync(lease.Account.Name, lease.Account.Password, ct);
					return OutcomeClassifier.FromRest(r);
				}, ct);

				if (restLogin.Outcome == Outcome.Ok)
				{
					string[] readPaths =
					[
						"/health",
						"/v1/rooms?limit=20",
						$"/v1/users/{stable.UserId}",
						"/v1/beatmapsets?limit=20",
						$"/v1/beatmapsets/{set.Id}",
						"/v1/scores?limit=20",
						"/v1/channels",
						"/v1/faqs",
						"/v1/menu/banners",
						"/v1/menu/seasonals",
						"/v1/menu/icon"
					];

					// Sample 3 REST read routes per cycle to keep per-user rate realistic.
					for (var i = 0; i < 3 && !ct.IsCancellationRequested; i++)
					{
						var path = readPaths[random.Next(readPaths.Length)];
						await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
						{
							var r = await rest.GetAsync(path, ct);
							return OutcomeClassifier.FromRest(r);
						}, ct);
					}

					if (context.InvocationNumber % 6 == 0)
					{
						await StepExecution.RunAsync(Channels.Upload, context, workload.Outcomes, async () =>
						{
							var r = await rest.PutAvatarAsync(stable.UserId, SampleFiles.Png, ct);
							return OutcomeClassifier.FromRest(r);
						}, ct);
					}

					await rest.SignOutAsync(ct);
				}

				stable.Enqueue(ClientPackets.ChannelPart("#osu"));
				await workload.PollStableAsync(stable, context, ct);
			}
			finally
			{
				if (rest.Token is not null)
				{
					try
					{
						using var outTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
						await rest.SignOutAsync(outTimeout.Token);
					}
					catch (Exception)
					{
						// Cleanup.
					}
				}

				await workload.LogoutStableAsync(stable, context);
			}
		}
	}
}
