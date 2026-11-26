using System.Collections.Concurrent;
using Basil.Tests.Shared.Clients.Irc;
using Basil.Tests.Shared.Workload.Outcomes;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>Simulates an IRC user: each scenario copy connects once and keeps the connection, then every invocation exercises channel queries (NAMES, WHO, WHOIS, LIST, TOPIC, AWAY) and one BasilBot command.</summary>
public sealed class IrcChatterPersona(WorkloadContext workload)
{
	private const string Channel = "#osu";

	private readonly ConcurrentDictionary<int, Held> _held = new();
	private volatile bool _closing;

	/// <summary>Runs one round of IRC activity on the copy's connection, connecting first when the copy has none or the server ended it.</summary>
	public async Task RunCycleAsync(IScenarioContext context)
	{
		if (_closing) return;

		using var linked = workload.LinkToken(context);
		var ct = linked.Token;
		var instance = context.ScenarioInfo.InstanceNumber;
		var random = SeededRandom.For(workload.Seed, context.ScenarioInfo.ScenarioName, instance, context.InvocationNumber);

		var held = await EnsureConnectionAsync(instance, context, ct);
		if (held is null)
		{
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
			return;
		}

		var irc = held.Irc;
		var wait = workload.WaitTimeout;
		var other = workload.World.PickOnlineUser(random);
		var targetNick = (other?.Name ?? held.Lease.Account.Name).Replace(' ', '_');

		// Exercise IRC discovery and state commands.
		await Query(held, context, ct, async () => OutcomeClassifier.FromIrcReply(await irc.NamesAsync(Channel, wait, ct)));
		await Query(held, context, ct, async () => OutcomeClassifier.FromIrcReply(await irc.TopicAsync(Channel, wait, ct)));
		await Query(held, context, ct, async () => OutcomeClassifier.FromIrcReply(await irc.ListAsync(wait, ct)));
		await Query(held, context, ct, async () => OutcomeClassifier.FromIrcReply(await irc.WhoAsync(Channel, wait, ct)));
		await Query(held, context, ct, async () => OutcomeClassifier.FromIrcReply(await irc.WhoisAsync(targetNick, wait, ct)));
		await Query(held, context, ct, async () => OutcomeClassifier.FromIrcReply(await irc.AwayAsync("spectating match", wait, ct)));
		await Query(held, context, ct, async () => OutcomeClassifier.FromIrcReply(await irc.AwayAsync(null, wait, ct)));

		// Chat and BasilBot commands.
		var botCommand = (context.InvocationNumber % 4) switch
		{
			0 => "!roll 100",
			1 => "!help",
			2 => $"!where {other?.Name ?? held.Lease.Account.Name}",
			_ => "!faq list"
		};
		await Query(held, context, ct, () => IrcSteps.CommandAsync(irc, Channel, botCommand, "bot_cmd", wait, ct));

		await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(2), ct);
	}

	/// <summary>Disconnects every IRC connection this persona still holds and returns every account lease; best effort, never throws.</summary>
	public async Task ShutdownAsync()
	{
		_closing = true;
		var all = _held.Values.ToArray();
		_held.Clear();
		await Task.WhenAll(all.Select(CloseAsync));
	}

	private async Task Query(Held held, IScenarioContext context, CancellationToken ct, Func<Task<StepOutcome>> action)
	{
		var outcome = await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, action, ct);
		if (outcome.Outcome == Outcome.Error && !ct.IsCancellationRequested) held.Broken = true;
	}

	private async Task<Held?> EnsureConnectionAsync(int instance, IScenarioContext context, CancellationToken ct)
	{
		if (_held.TryGetValue(instance, out var existing))
		{
			if (!existing.Broken) return existing;

			_held.TryRemove(instance, out _);
			await CloseAsync(existing);
		}

		if (!workload.Accounts.TryAcquire(AccountRole.Player, out var lease)) return null;

		var irc = new IrcClient(workload.Endpoint, workload.IrcPort, lease.Account.Name, lease.Account.Password);
		var held = new Held(lease, irc);
		var (registered, reg) = await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
		{
			var result = await irc.ConnectAsync(workload.WaitTimeout, ct);
			return (OutcomeClassifier.FromIrcRegistration(result), result);
		}, ct);
		if (registered.Outcome != Outcome.Ok || reg is not { Welcomed: true })
		{
			if (registered.Outcome == Outcome.Error && reg is { Line: not null }) lease.Quarantine();
			await CloseAsync(held);
			return null;
		}

		await Query(held, context, ct, () => IrcSteps.JoinAsync(irc, Channel, workload.WaitTimeout, ct));
		if (held.Broken)
		{
			await CloseAsync(held);
			return null;
		}

		_held[instance] = held;
		return held;
	}

	private static async Task CloseAsync(Held held)
	{
		await held.Irc.DisposeAsync();
		held.Lease.Dispose();
	}

	private sealed class Held(AccountLease lease, IrcClient irc)
	{
		public AccountLease Lease { get; } = lease;

		public IrcClient Irc { get; } = irc;

		public bool Broken { get; set; }
	}
}
