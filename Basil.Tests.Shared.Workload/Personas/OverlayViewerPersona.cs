using System.Collections.Concurrent;
using Basil.Tests.Shared.Clients.Rest;
using Basil.Tests.Shared.Workload.Outcomes;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>
///     Simulates a stream overlay viewer reading SSE events from <c>/v1/matches/{id}</c> and
///     <c>/v1/rooms/{id}/live</c>: each scenario copy signs in once where the stream needs it and keeps the session, then
///     every invocation watches one stream for one observation window.
/// </summary>
public sealed class OverlayViewerPersona(WorkloadContext workload)
{
	private readonly ConcurrentDictionary<int, Viewer> _viewers = new();
	private volatile bool _closing;

	/// <summary>Watches one live room or match report stream for a bounded observation window.</summary>
	public async Task RunCycleAsync(IScenarioContext context)
	{
		if (_closing) return;

		using var linked = workload.LinkToken(context);
		var ct = linked.Token;
		var instance = context.ScenarioInfo.InstanceNumber;
		var random = SeededRandom.For(workload.Seed, context.ScenarioInfo.ScenarioName, instance,
			context.InvocationNumber);

		var room = workload.World.PickRoom(random);
		if (room is null)
		{
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
			return;
		}

		var watchMatchReport = instance % 2 == 0;
		if (!_viewers.TryGetValue(instance, out var viewer))
		{
			viewer = new Viewer(new RestClient(workload.Endpoint, workload.Http));
			_viewers[instance] = viewer;
		}

		if (!watchMatchReport && viewer.Rest.Token is null && !await SignInAsync(viewer, context, ct))
		{
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
			return;
		}

		var path = watchMatchReport ? $"/v1/matches/{room.RoomId}" : $"/v1/rooms/{room.RoomId}/live";
		using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
		window.CancelAfter(workload.RoundDuration);

		var delivered = await SseWatch.WatchAsync(workload, viewer.Rest, path, !watchMatchReport,
			() => context, window.Token);

		// One stream per viewing window: a refused or finished stream is not reopened at once.
		await WorkloadContext.SafeDelayAsync(workload.RoundDuration, window.Token);

		// A refused token means the viewer has to sign in again next time.
		if (!delivered && !watchMatchReport && !window.IsCancellationRequested) await ReleaseAsync(viewer);
	}

	/// <summary>Signs out every session this persona still holds and returns every account lease; best effort, never throws.</summary>
	public async Task ShutdownAsync()
	{
		_closing = true;
		var all = _viewers.Values.ToArray();
		_viewers.Clear();
		await Task.WhenAll(all.Select(ReleaseAsync));
	}

	private async Task<bool> SignInAsync(Viewer viewer, IScenarioContext context, CancellationToken ct)
	{
		if (!workload.Accounts.TryAcquire(AccountRole.Player, out var lease)) return false;

		var login = await StepExecution.RunAsync(Channels.Login, context, workload.Outcomes, async () =>
		{
			var reply = await viewer.Rest.SignInAsync(lease.Account.Name, lease.Account.Password, ct);
			return OutcomeClassifier.FromRest(reply);
		}, ct);
		if (login.Outcome != Outcome.Ok || viewer.Rest.Token is null)
		{
			lease.Dispose();
			return false;
		}

		viewer.Lease = lease;
		return true;
	}

	private static async Task ReleaseAsync(Viewer viewer)
	{
		await WorkloadContext.SignOutQuietAsync(viewer.Rest);
		viewer.Lease?.Dispose();
		viewer.Lease = null;
	}

	private sealed class Viewer(RestClient rest)
	{
		public RestClient Rest { get; } = rest;

		public AccountLease? Lease { get; set; }
	}
}