using Basil.Tests.Shared.Clients.Irc;
using Basil.Tests.Shared.Clients.Rest;
using Basil.Tests.Shared.Clients.Stable;
using Basil.Tests.Shared.Workload.Outcomes;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>Simulates login and logout churn across osu! stable, IRC and REST sessions.</summary>
public sealed class ChurnPersona(WorkloadContext workload)
{
	/// <summary>Runs one short-lived sign-in/activity/sign-out cycle.</summary>
	public async Task RunCycleAsync(IScenarioContext context)
	{
		using var linked = workload.LinkToken(context);
		var ct = linked.Token;

		if (!workload.Accounts.TryAcquire(AccountRole.Player, out var lease))
		{
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
			return;
		}

		using (lease)
		{
			switch (context.InvocationNumber % 3)
			{
				case 0:
					await ChurnStableAsync(lease, context, ct);
					break;
				case 1:
					await ChurnRestAsync(lease, context, ct);
					break;
				default:
					await ChurnIrcAsync(lease, context, ct);
					break;
			}
		}
	}

	/// <summary>Does nothing: every cycle signs itself out, so no session outlives it.</summary>
	public Task ShutdownAsync()
	{
		return Task.CompletedTask;
	}

	private async Task ChurnStableAsync(AccountLease lease, IScenarioContext context, CancellationToken ct)
	{
		var client = new StableClient(workload.Endpoint, workload.Http, lease.Identity());
		var login = await workload.LoginStableAsync(client, context, ct, lease: lease);
		if (login is not { Accepted: true }) return;

		try
		{
			for (var i = 0; i < 3 && !ct.IsCancellationRequested; i++)
			{
				await workload.PollStableAsync(client, context, ct);
				await WorkloadContext.SafeDelayAsync(workload.PollInterval, ct);
			}
		}
		finally
		{
			await workload.LogoutStableAsync(client, context);
		}
	}

	private async Task ChurnRestAsync(AccountLease lease, IScenarioContext context, CancellationToken ct)
	{
		var rest = new RestClient(workload.Endpoint, workload.Http);
		var login = await StepExecution.RunAsync(Channels.Login, context, workload.Outcomes, async () =>
		{
			var reply = await rest.SignInAsync(lease.Account.Name, lease.Account.Password, ct);
			return OutcomeClassifier.FromRest(reply);
		}, ct);

		if (login.Outcome != Outcome.Ok) return;

		try
		{
			await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
			{
				var reply = await rest.GetAsync("/v1/auth/session", ct);
				return OutcomeClassifier.FromRest(reply);
			}, ct);
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
		}
		finally
		{
			await workload.LogoutRestAsync(rest, context);
		}
	}

	private async Task ChurnIrcAsync(AccountLease lease, IScenarioContext context, CancellationToken ct)
	{
		await using var irc =
			new IrcClient(workload.Endpoint, workload.IrcPort, lease.Account.Name, lease.Account.Password);
		var (outcome, reg) = await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
		{
			var result = await irc.ConnectAsync(workload.WaitTimeout, ct);
			return (OutcomeClassifier.FromIrcRegistration(result), result);
		}, ct);

		if (outcome.Outcome != Outcome.Ok || reg is not { Welcomed: true }) return;

		await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(2), ct);
	}
}