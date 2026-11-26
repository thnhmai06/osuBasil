using System.Collections.Concurrent;
using System.Net;
using Basil.Tests.Shared.Clients.OsuWeb;
using Basil.Tests.Shared.Clients.Rest;
using Basil.Tests.Shared.Clients.Stable;
using Basil.Tests.Shared.Workload.Outcomes;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>Covers every secondary feature at a low real rate: presence, friends, PMs, channels, search, /d/, difficulty-rating, assets, avatar/screenshot uploads, BasilBot and REST reads, on a pool of signed-in sessions that cycles borrow and return.</summary>
public sealed class BackgroundPersona(WorkloadContext workload)
{
	private static readonly string[] CoverVariants = ["cover", "card", "list", "slimcover"];

	private readonly ConcurrentDictionary<long, Session> _sessions = new();
	private readonly CancellationTokenSource _stopKeepAlive = new();
	private IScenarioContext? _latest;
	private Task? _keepAlive;
	private long _nextId;
	private volatile bool _closing;

	/// <summary>Runs one rotating background activity cycle on a free pooled session, signing a new one in when none is free.</summary>
	public async Task RunCycleAsync(IScenarioContext context)
	{
		if (_closing) return;

		_latest = context;
		_keepAlive ??= Task.Run(() => KeepAliveAsync(_stopKeepAlive.Token));

		using var linked = workload.LinkToken(context);
		var ct = linked.Token;
		var random = SeededRandom.For(workload.Seed, context.ScenarioInfo.ScenarioName,
			context.ScenarioInfo.InstanceNumber, context.InvocationNumber);

		var session = await BorrowAsync(context, ct);
		if (session is null)
		{
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
			return;
		}

		try
		{
			await ActivityAsync(session, context, random, ct);
		}
		finally
		{
			if (_closing || workload.IsDropped(session.Stable))
			{
				_sessions.TryRemove(session.Id, out _);
				await CloseAsync(session);
			}
			else
			{
				session.Return();
			}
		}
	}

	/// <summary>Signs out every pooled session and returns every account lease; best effort, never throws.</summary>
	public async Task ShutdownAsync()
	{
		_closing = true;
		await _stopKeepAlive.CancelAsync();
		if (_keepAlive is not null)
		{
			try
			{
				await _keepAlive.WaitAsync(TimeSpan.FromSeconds(5));
			}
			catch (Exception)
			{
				// Best effort.
			}
		}

		var all = _sessions.Values.ToArray();
		_sessions.Clear();
		await Task.WhenAll(all.Select(CloseAsync));
	}

	private async Task<Session?> BorrowAsync(IScenarioContext context, CancellationToken ct)
	{
		foreach (var pooled in _sessions.Values)
		{
			if (!pooled.TryBorrow()) continue;
			if (!workload.IsDropped(pooled.Stable)) return pooled;

			_sessions.TryRemove(pooled.Id, out _);
			await CloseAsync(pooled);
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

		var session = new Session(Interlocked.Increment(ref _nextId), lease, stable,
			new OsuWebClient(workload.Endpoint, workload.Http, identity),
			new AssetClient(workload.Endpoint, workload.Http),
			new RestClient(workload.Endpoint, workload.Http));
		session.TryBorrow();
		_sessions[session.Id] = session;

		stable.Enqueue(ClientPackets.ChannelJoin("#osu"));
		await workload.PollStableAsync(stable, context, ct);
		return session;
	}

	// An idle osu! client still polls; without it the server would end the pooled sessions as silent.
	private async Task KeepAliveAsync(CancellationToken stop)
	{
		while (!stop.IsCancellationRequested)
		{
			if (_latest is { } context)
			{
				await Task.WhenAll(_sessions.Values.Select(async session =>
				{
					if (!session.TryBorrow()) return;
					try
					{
						await workload.PollStableAsync(session.Stable, context, stop);
					}
					finally
					{
						session.Return();
					}
				}));
			}

			await WorkloadContext.SafeDelayAsync(workload.PollInterval, stop);
		}
	}

	private async Task CloseAsync(Session session)
	{
		await workload.SignOutQuietAsync(session.Stable);
		await WorkloadContext.SignOutQuietAsync(session.Rest);
		session.Lease.Dispose();
	}

	private async Task ActivityAsync(Session session, IScenarioContext context, Random random, CancellationToken ct)
	{
		var stable = session.Stable;
		var web = session.Web;
		var assets = session.Assets;
		var rest = session.Rest;

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
			2 => $"!where {peer?.Name ?? stable.Identity.Name}",
			_ => workload.WorldData.Faqs.Count > 0
				? $"!faq {workload.WorldData.Faqs[random.Next(workload.WorldData.Faqs.Count)]}"
				: "!faq list"
		};
		stable.Enqueue(ClientPackets.SendPublicMessage("#osu", botCmd));
		await workload.PollStableAsync(stable, context, ct);

		// 2. osu-web reads: friends, seasonals, search, search-set, difficulty-rating.
		await StepExecution.RunAsync(Channels.OsuWeb, context, workload.Outcomes, async () =>
		{
			var r = await web.GetFriendsAsync(ct);
			return OutcomeClassifier.FromHttpStatus(r.Status);
		}, ct);

		await StepExecution.RunAsync(Channels.OsuWeb, context, workload.Outcomes, async () =>
		{
			var r = await web.GetSeasonalsAsync(ct);
			return OutcomeClassifier.FromHttpStatus(r.Status);
		}, ct);

		await StepExecution.RunAsync(Channels.OsuWeb, context, workload.Outcomes, async () =>
		{
			var r = await web.SearchAsync(set.Artist.Split(' ')[0], status: 4, mode: -1, page: 0, ct);
			return OutcomeClassifier.FromHttpStatus(r.Status);
		}, ct);

		await StepExecution.RunAsync(Channels.OsuWeb, context, workload.Outcomes, async () =>
		{
			var r = await web.SearchSetAsync(set.Id, ct);
			return OutcomeClassifier.FromHttpStatus(r.Status);
		}, ct);

		await StepExecution.RunAsync(Channels.OsuWeb, context, workload.Outcomes, async () =>
		{
			var r = await web.GetDifficultyRatingAsync(map.Id, mods: 0, ct);
			return OutcomeClassifier.FromHttpStatus(r.Status);
		}, ct);

		// 3. Asset reads (avatar, cover, thumbnail, preview, seasonal, screenshot).
		await StepExecution.RunAsync(Channels.Asset, context, workload.Outcomes, async () =>
		{
			var r = await assets.GetAvatarAsync(peer?.UserId ?? stable.UserId, ct);
			return OutcomeClassifier.FromHttpStatus(r.Status, bytes: r.Body);
		}, ct);

		await StepExecution.RunAsync(Channels.Asset, context, workload.Outcomes, async () =>
		{
			var variant = CoverVariants[random.Next(CoverVariants.Length)];
			var r = await assets.GetCoverAsync(set.Id, variant, ct);
			return OutcomeClassifier.FromHttpStatus(r.Status, refusalExpected: r.Status == HttpStatusCode.NotFound, bytes: r.Body);
		}, ct);

		await StepExecution.RunAsync(Channels.Asset, context, workload.Outcomes, async () =>
		{
			var r = await assets.GetThumbnailAsync(set.Id, large: random.Next(2) == 0, ct);
			return OutcomeClassifier.FromHttpStatus(r.Status, refusalExpected: r.Status == HttpStatusCode.NotFound, bytes: r.Body);
		}, ct);

		await StepExecution.RunAsync(Channels.Asset, context, workload.Outcomes, async () =>
		{
			var r = await assets.GetPreviewAsync(set.Id, ct);
			return OutcomeClassifier.FromHttpStatus(r.Status, refusalExpected: r.Status == HttpStatusCode.NotFound, bytes: r.Body);
		}, ct);

		if (workload.WorldData.Seasonals.Count > 0)
		{
			var seasonal = workload.WorldData.Seasonals[random.Next(workload.WorldData.Seasonals.Count)];
			await StepExecution.RunAsync(Channels.Asset, context, workload.Outcomes, async () =>
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
			await StepExecution.RunAsync(Channels.Asset, context, workload.Outcomes, async () =>
			{
				var r = await assets.GetScreenshotAsync(existingScreenshot, ct);
				return OutcomeClassifier.FromHttpStatus(r.Status, refusalExpected: r.Status == HttpStatusCode.NotFound, bytes: r.Body);
			}, ct);
		}

		// 6. REST reads and occasional avatar upload; the REST session is signed in once and kept with the pooled session.
		if (rest.Token is null)
		{
			var restLogin = await StepExecution.RunAsync(Channels.Login, context, workload.Outcomes, async () =>
			{
				var r = await rest.SignInAsync(session.Lease.Account.Name, session.Lease.Account.Password, ct);
				return OutcomeClassifier.FromRest(r);
			}, ct);
			if (restLogin.Outcome != Outcome.Ok) return;
		}

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
				var r = await session.Rest.GetAsync(path, ct);
				if (r.Status == HttpStatusCode.Unauthorized) session.Rest = new RestClient(workload.Endpoint, workload.Http);
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
	}

	private sealed class Session(long id, AccountLease lease, StableClient stable, OsuWebClient web, AssetClient assets, RestClient rest)
	{
		private int _busy;

		public long Id { get; } = id;

		public AccountLease Lease { get; } = lease;

		public StableClient Stable { get; } = stable;

		public OsuWebClient Web { get; } = web;

		public AssetClient Assets { get; } = assets;

		public RestClient Rest { get; set; } = rest;

		public bool TryBorrow()
		{
			return Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
		}

		public void Return()
		{
			Volatile.Write(ref _busy, 0);
		}
	}
}
