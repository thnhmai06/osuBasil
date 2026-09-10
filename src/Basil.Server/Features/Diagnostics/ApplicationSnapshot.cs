using Basil.Server.Shared.Sessions;

namespace Basil.Server.Features.Diagnostics;

/// <summary>Basil's own live state: what the process is doing, not how the runtime underneath it is doing.</summary>
/// <param name="ActiveGameSessions">The number of osu! client sessions currently logged in.</param>
/// <param name="ActiveSseSubscribers">
///     The number of Server-Sent Events connections currently open, across every stream Basil
///     publishes.
/// </param>
/// <param name="SsePublishesDropped">
///     The cumulative number of stream publishes dropped since the process started for arriving out
///     of order relative to a newer one already applied to the same stream -- a benign, by-design
///     race outcome under concurrent load, not an error count.
/// </param>
/// <param name="ActiveMatches">The number of multiplayer matches currently registered.</param>
/// <param name="ActiveMatchTimers">The number of multiplayer matches with a countdown currently running.</param>
/// <param name="ActiveChannels">The number of chat channels currently registered.</param>
/// <param name="ActiveIrcSessions">The number of IRC sessions currently online.</param>
public sealed record ApplicationSample(
	int ActiveGameSessions,
	long ActiveSseSubscribers,
	long SsePublishesDropped,
	long ActiveMatches,
	long ActiveMatchTimers,
	long ActiveChannels,
	long ActiveIrcSessions);

/// <summary>Samples Basil's own live state from the registries and counters that already own it.</summary>
/// <remarks>
///     <see cref="ActiveMatches" />, <see cref="ActiveMatchTimers" />, <see cref="ActiveChannels" />
///     and <see cref="ActiveIrcSessions" /> come from Multiplayer, Chat and Irc's own observable
///     gauges, read back through <see cref="RuntimeMeterListener" /> rather than this slice holding a
///     reference to any of those slices' registries -- the owning slice is the only one that knows
///     what "active" means for its own state, and the meter is the seam that lets Diagnostics report
///     it without an edge to any of them.
/// </remarks>
public sealed class ApplicationSampler(
	ISessionRegistry<GameSession> gameSessions,
	RuntimeMeterListener meterListener)
{
	/// <summary>Takes a fresh sample of Basil's own live state.</summary>
	public ApplicationSample Sample()
	{
		meterListener.RefreshObservableGauges();
		return new(
			gameSessions.All.Count,
			meterListener.ActiveSseSubscribers,
			meterListener.SsePublishesDropped,
			meterListener.ActiveMatches,
			meterListener.ActiveMatchTimers,
			meterListener.ActiveChannels,
			meterListener.ActiveIrcSessions);
	}
}
