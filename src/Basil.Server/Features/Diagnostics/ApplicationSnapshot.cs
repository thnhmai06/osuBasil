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
public sealed record ApplicationSample(
	int ActiveGameSessions,
	long ActiveSseSubscribers,
	long SsePublishesDropped);

/// <summary>Samples Basil's own live state from the registries and counters that already own it.</summary>
public sealed class ApplicationSampler(
	ISessionRegistry<GameSession> gameSessions,
	RuntimeMeterListener meterListener)
{
	/// <summary>Takes a fresh sample of Basil's own live state.</summary>
	public ApplicationSample Sample() => new(
		gameSessions.All.Count,
		meterListener.ActiveSseSubscribers,
		meterListener.SsePublishesDropped);
}
