using Basil.Tests.Shared.Clients.Irc;
using Basil.Tests.Shared.Workload.Outcomes;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>Simulates an IRC user exercising channels, queries (NAMES, WHO, WHOIS, LIST, TOPIC, AWAY) and BasilBot commands.</summary>
public sealed class IrcChatterPersona(WorkloadContext workload)
{
	/// <summary>Runs one IRC session cycle.</summary>
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
			await using var irc = new IrcClient(workload.Endpoint, workload.IrcPort, lease.Account.Name, lease.Account.Password);
			var (regOutcome, reg) = await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
			{
				var result = await irc.ConnectAsync(workload.WaitTimeout, ct);
				return (OutcomeClassifier.FromIrcRegistration(result), result);
			}, ct);

			if (regOutcome.Outcome != Outcome.Ok || reg is not { Welcomed: true }) return;

			await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
			{
				await irc.JoinAsync("#osu", ct);
				return StepOutcome.Ok("join");
			}, ct);

			// Exercise IRC discovery and state commands.
			await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
				OutcomeClassifier.FromIrcReply(await irc.NamesAsync("#osu", workload.WaitTimeout, ct)), ct);

			await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
				OutcomeClassifier.FromIrcReply(await irc.TopicAsync("#osu", workload.WaitTimeout, ct)), ct);

			await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
				OutcomeClassifier.FromIrcReply(await irc.ListAsync(workload.WaitTimeout, ct)), ct);

			var other = workload.World.PickOnlineUser(random);
			var targetNick = (other?.Name ?? lease.Account.Name).Replace(' ', '_');
			await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
				OutcomeClassifier.FromIrcReply(await irc.WhoAsync("#osu", workload.WaitTimeout, ct)), ct);

			await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
				OutcomeClassifier.FromIrcReply(await irc.WhoisAsync(targetNick, workload.WaitTimeout, ct)), ct);

			await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
				OutcomeClassifier.FromIrcReply(await irc.AwayAsync("spectating match", workload.WaitTimeout, ct)), ct);

			await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
				OutcomeClassifier.FromIrcReply(await irc.AwayAsync(null, workload.WaitTimeout, ct)), ct);

			// Chat and BasilBot commands.
			var botCommand = (context.InvocationNumber % 4) switch
			{
				0 => "!roll 100",
				1 => "!help",
				2 => $"!where {other?.Name ?? lease.Account.Name}",
				_ => "!faq list"
			};
			await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
			{
				await irc.PrivmsgAsync("#osu", botCommand, ct);
				return StepOutcome.Ok("bot_cmd");
			}, ct);

			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(2), ct);

			await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
			{
				await irc.PartAsync("#osu", ct);
				return StepOutcome.Ok("part");
			}, ct);
		}
	}
}
