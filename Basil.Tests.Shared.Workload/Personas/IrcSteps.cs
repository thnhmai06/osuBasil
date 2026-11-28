using Basil.Protocol.Irc;
using Basil.Protocol.Irc.Models;
using Basil.Tests.Shared.Clients.Irc;
using Basil.Tests.Shared.Workload.Outcomes;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>
///     IRC actions that read the server's answer before they report: a command is ok when BasilBot answers it, a join
///     or part when the server echoes it.
/// </summary>
internal static class IrcSteps
{
	private const string BotNick = "BasilBot";

	/// <summary>
	///     Sends <paramref name="text" /> to <paramref name="target" /> and waits for BasilBot's reply; the reply's
	///     arrival is the answer, its wording is not read.
	/// </summary>
	/// <returns>Ok on a reply, an error on a timeout or a refusing numeric.</returns>
	public static async Task<StepOutcome> CommandAsync(IrcClient irc, string target, string text, string tag,
		TimeSpan timeout, CancellationToken ct)
	{
		using var reply = irc.Expect(line => IsBotReply(line, target, irc.Nick) || IsRefusal(line));
		await irc.PrivmsgAsync(target, text, ct);
		var line = await reply.WaitAsync(timeout, ct);
		if (line is null) return StepOutcome.Error($"{tag}_timeout");
		if (OutcomeClassifier.FromIrcNotice(line) is { } turnedAway) return turnedAway;
		return IsRefusal(line) ? StepOutcome.Error($"{tag}_{line.Command}") : StepOutcome.Ok(tag);
	}

	/// <summary>Joins a channel and waits for the server's join echo.</summary>
	public static Task<StepOutcome> JoinAsync(IrcClient irc, string channel, TimeSpan timeout, CancellationToken ct)
	{
		return MembershipAsync(irc, new IrcMessage(IrcVerb.Join, null, channel), IrcVerb.Join, channel, "join", timeout,
			ct);
	}

	/// <summary>Leaves a channel and waits for the server's part echo.</summary>
	public static Task<StepOutcome> PartAsync(IrcClient irc, string channel, TimeSpan timeout, CancellationToken ct)
	{
		return MembershipAsync(irc, new IrcMessage(IrcVerb.Part, null, channel), IrcVerb.Part, channel, "part", timeout,
			ct);
	}

	private static async Task<StepOutcome> MembershipAsync(IrcClient irc, IrcMessage request, string echo,
		string channel, string tag, TimeSpan timeout, CancellationToken ct)
	{
		var line = await irc.RequestAsync(request,
			reply => (reply.Command == echo && reply.Parameters.Count > 0 &&
			          string.Equals(reply.Parameters[0], channel, StringComparison.OrdinalIgnoreCase)) ||
			         IsRefusal(reply),
			timeout, ct);
		if (line is null) return StepOutcome.Error($"{tag}_timeout");
		if (OutcomeClassifier.FromIrcNotice(line) is { } turnedAway) return turnedAway;
		return IsRefusal(line) ? StepOutcome.Error($"{tag}_{line.Command}") : StepOutcome.Ok(tag);
	}

	private static bool IsBotReply(IrcMessage line, string target, string ownNick)
	{
		return line is { Command: IrcVerb.Privmsg, Prefix: { } prefix } && prefix.StartsWith(BotNick,
			                                                                StringComparison.OrdinalIgnoreCase)
		                                                                && line.Parameters.Count > 0
		                                                                && (string.Equals(line.Parameters[0], target,
			                                                                    StringComparison.OrdinalIgnoreCase)
		                                                                    || string.Equals(line.Parameters[0],
			                                                                    ownNick,
			                                                                    StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsRefusal(IrcMessage line)
	{
		return line.Command is ['4' or '5', _, _] && ushort.TryParse(line.Command, out _);
	}
}