namespace Basil.Server.Shared.Eventing;

/// <summary>
///     A broadcast hub for live streams: every subscriber to a <see cref="StreamKey" /> receives the
///     same immutable event. Owns subscription, routing, broadcast, backpressure and delivery
///     ordering, and nothing else — it stores opaque payload bytes and a version, never a
///     repository, a feature DTO or a snapshot builder.
/// </summary>
public interface ILiveEventHub
{
	/// <summary>
	///     Gets a value indicating whether a stream currently has anything subscribed to it, letting
	///     a caller skip building and serializing a payload nobody will receive.
	/// </summary>
	/// <param name="key">The stream to check.</param>
	bool HasSubscribers(StreamKey key);

	/// <summary>Broadcasts <paramref name="payload" /> to every current subscriber of <paramref name="key" />.</summary>
	/// <param name="key">The stream being updated.</param>
	/// <param name="version">This publish's version, strictly greater than the stream's previous version.</param>
	/// <param name="payload">The opaque bytes to broadcast.</param>
	void Publish(StreamKey key, long version, ReadOnlyMemory<byte> payload);

	/// <summary>Subscribes to a stream, so it starts receiving every publish from this point on.</summary>
	/// <param name="key">The stream to subscribe to.</param>
	/// <returns>A subscription handle carrying the stream's events from now on.</returns>
	LiveSubscription Open(StreamKey key);

	/// <summary>
	///     Drops every stream whose <see cref="StreamKey.Category" /> and <see cref="StreamKey.Id" />
	///     match, once nothing can subscribe to them anymore, so a torn-down entity's streams never
	///     linger in memory past its lifetime.
	/// </summary>
	/// <param name="category">The stream family to forget, e.g. <c>"match"</c>.</param>
	/// <param name="id">The identifier of the specific entity whose streams to forget.</param>
	void Forget(string category, int id);
}