using Basil.Domain.Channels;

namespace Basil.Application.Abstractions.Channels;

/// <summary>
///     Provides read access to the Channels table.
/// </summary>
public interface IChannelRepository
{
	/// <summary>
	///     Fetches every channel row in the database, regardless of the
	///     <see cref="Channel.AutoJoin" /> flag.
	/// </summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Every stored channel.</returns>
	/// <remarks>
	///     Used to seed the runtime channel registry at startup. Every channel is loaded
	///     unconditionally; <see cref="Channel.AutoJoin" /> only gates what gets sent to a client at
	///     login, never whether the channel exists in the registry.
	/// </remarks>
	Task<IReadOnlyList<Channel>> FetchAllAsync(CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches the channel with the given name.
	/// </summary>
	/// <param name="name">The channel name to look up.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The matching channel, or <see langword="null" /> when no such channel exists.</returns>
	Task<Channel?> FetchOneByNameAsync(string name, CancellationToken cancellationToken = default);
}