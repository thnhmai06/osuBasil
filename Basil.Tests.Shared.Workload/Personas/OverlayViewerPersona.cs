using Basil.Tests.Shared.Clients.Rest;
using Basil.Tests.Shared.Workload.Outcomes;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>Simulates a stream overlay viewer reading SSE events from <c>/v1/matches/{id}</c> and <c>/v1/rooms/{id}/live</c>.</summary>
public sealed class OverlayViewerPersona(WorkloadContext workload)
{
	/// <summary>Watches one live room or match report stream for a bounded observation window.</summary>
	public async Task RunCycleAsync(IScenarioContext context)
	{
		using var linked = workload.LinkToken(context);
		var ct = linked.Token;
		var random = SeededRandom.For(workload.Seed, context.ScenarioInfo.ScenarioName,
			context.ScenarioInfo.InstanceNumber, context.InvocationNumber);

		var room = workload.World.PickRoom(random);
		if (room is null)
		{
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
			return;
		}

		var watchMatchReport = context.ScenarioInfo.InstanceNumber % 2 == 0;
		var rest = new RestClient(workload.Endpoint, workload.Http);
		AccountLease? lease = null;

		try
		{
			if (!watchMatchReport)
			{
				if (!workload.Accounts.TryAcquire(AccountRole.Player, out lease))
				{
					await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
					return;
				}

				var login = await StepExecution.RunAsync(Channels.Login, context, workload.Outcomes, async () =>
				{
					var reply = await rest.SignInAsync(lease.Account.Name, lease.Account.Password, ct);
					return OutcomeClassifier.FromRest(reply);
				}, ct);
				if (login.Outcome != Outcome.Ok) return;
			}

			var path = watchMatchReport ? $"/v1/matches/{room.RoomId}" : $"/v1/rooms/{room.RoomId}/live";
			using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
			window.CancelAfter(workload.RoundDuration);

			await StepExecution.RunAsync(Channels.Sse, context, workload.Outcomes, async () =>
			{
				using var stream = await rest.OpenStreamAsync(path, authorize: !watchMatchReport, window.Token);
				var open = OutcomeClassifier.FromHttpStatus(stream.Status,
					refusalExpected: stream.Status == System.Net.HttpStatusCode.NotFound);
				if (open.Outcome != Outcome.Ok) return open;

				long bytes = 0;
				var events = 0;
				await foreach (var ev in stream.ReadAsync(window.Token))
				{
					bytes += ev.Data.Length;
					events++;
				}

				return events > 0 || window.IsCancellationRequested
					? StepOutcome.Ok("sse", bytes)
					: StepOutcome.Error("sse_dropped");
			}, ct);
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

			lease?.Dispose();
		}
	}
}
