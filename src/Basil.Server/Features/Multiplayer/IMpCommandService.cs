using Basil.Server.Features.Bot;
using Basil.Server.Shared.Sessions;

namespace Basil.Server.Features.Multiplayer;

/// <summary>
///     The <c>!mp</c> command surface, as seen by the chat transport that routes to it.
/// </summary>
/// <remarks>
///     This is the single contract Bot's <c>CommandDispatcher</c> uses to reach Multiplayer: it
///     carries no <see cref="MatchSession" />, <see cref="IMatchRegistry" />, or any of
///     <see cref="MpCommandService" />'s own collaborators (match handlers, result enums,
///     <see cref="MatchControlService" />) across the slice boundary -- a match scope crosses as a
///     plain persistent id, and everything scope-resolution- and reply-routing-related that used to
///     live in <c>CommandDispatcher</c> lives in <see cref="MpCommandService" /> instead.
/// </remarks>
public interface IMpCommandService
{
	/// <summary>
	///     Dispatches a <c>!mp</c> command sent through the chat transport, applying the
	///     channel-eligibility rules and resolving the command's match scope.
	/// </summary>
	/// <remarks>
	///     <c>help</c> (or an empty subcommand) bypasses scope resolution entirely, so it always
	///     answers, even for a sender with no <see cref="UserSession.MpScopeMatchId" /> and no
	///     physical match. <c>#lobby</c> only ever reaches <c>make</c>/<c>makeprivate</c> -- every
	///     other subcommand (including <c>in</c>) is refused there with a DM'd error rather than
	///     running. <c>make</c>/<c>makeprivate</c> create a match, <c>join</c> targets any match by
	///     persistent room id, and <c>in</c> targets one the sender may not be in at all -- all four
	///     run with no channel-derived match scope (reachable via PM to the bot), unlike every other
	///     subcommand, which resolves via the sender's stored scope, the channel scope, or the
	///     match the sender physically sits in. A subcommand run from anywhere but the resolved
	///     match's own channel (or a DM, where <paramref name="channelName" /> is
	///     <see langword="null" />) never replies publicly into that unrelated channel -- it gets the
	///     same DM-with-<c>[#id]</c>-prefix-plus-room-mirror treatment a DM to the bot already gets.
	/// </remarks>
	/// <param name="sender">The userSession issuing the command.</param>
	/// <param name="args">
	///     The command's argument tokens; <c>args[0]</c> is the subcommand name, the rest are its
	///     own arguments.
	/// </param>
	/// <param name="channelScopeMatchDbId">
	///     The persistent id of the match derived from the sender's current chat channel, but only
	///     when the message was sent in that match's own chat channel; <see langword="null" />
	///     otherwise, including for private messages, which are never a match channel.
	/// </param>
	/// <param name="channelName">
	///     The resolved internal channel name the message was sent in, or <see langword="null" /> for
	///     a private message to the bot.
	/// </param>
	/// <param name="sink">The destination for the command's reply.</param>
	/// <param name="cancellationToken">The cancellation token to observe.</param>
	/// <returns>A value that indicates whether the command was recognized and ran successfully.</returns>
	Task<bool> DispatchAsync(UserSession sender, string[] args, int? channelScopeMatchDbId,
		string? channelName, ICommandReplySink sink, CancellationToken cancellationToken = default);

	/// <summary>
	///     Runs a <c>;</c>- or <c>&amp;&amp;</c>-chained line of <c>!mp</c> subcommands sequentially
	///     against the resolved scope.
	/// </summary>
	/// <remarks>
	///     Chaining is only allowed for a sender who is currently a referee of that scope, and only
	///     for <c>!mp</c> subcommands that operate on the existing room. <c>make</c>, <c>makeprivate</c>,
	///     <c>join</c>, <c>in</c>, and <c>help</c> are not chainable: they either create a match or
	///     change the scope elsewhere. Any other segment, such as a bare <c>!roll</c>, <c>!where</c>, or
	///     <c>!faq</c>, is not a <c>!mp</c> command at all, and its presence in a chain rejects the
	///     whole line rather than running part of it silently. A chain with no scope, or from a sender
	///     who is not a referee of it, is likewise rejected with an error, DM'd when the line was
	///     issued outside the match's own channel. <c>#lobby</c> never reaches here with anything
	///     runnable, since every chainable subcommand is already outside that channel's allowlist.
	/// </remarks>
	/// <param name="sender">The userSession issuing the chain.</param>
	/// <param name="segments">
	///     The already-split chain segments: each one's raw text (prefix included) and whether it
	///     only runs if the previous segment succeeded (an <c>&amp;&amp;</c>-preceded segment).
	/// </param>
	/// <param name="channelScopeMatchDbId">
	///     The persistent id of the match derived from the sender's current chat channel, but only
	///     when the message was sent in that match's own chat channel; <see langword="null" />
	///     otherwise.
	/// </param>
	/// <param name="channelName">
	///     The resolved internal channel name the message was sent in, or <see langword="null" /> for
	///     a private message to the bot.
	/// </param>
	/// <param name="prefix">The configured command prefix (e.g. <c>!</c>).</param>
	/// <param name="sink">The destination for the chain's replies.</param>
	/// <param name="cancellationToken">The cancellation token to observe.</param>
	/// <returns>A value that indicates whether any segment in the chain ran successfully.</returns>
	Task<bool> DispatchChainAsync(UserSession sender,
		IReadOnlyList<(string Text, bool RequiresPreviousSuccess)> segments, int? channelScopeMatchDbId,
		string? channelName, string prefix, ICommandReplySink sink, CancellationToken cancellationToken = default);
}
