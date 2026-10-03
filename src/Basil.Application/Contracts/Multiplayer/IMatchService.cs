namespace Basil.Application.Contracts.Multiplayer;

/// <summary>Keeps the stored match history consistent.</summary>
public interface IMatchService
{
	/// <summary>Ends every stored match and round that is still running, as when the server stopped while they were.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>Run by the host when it starts, before any room opens. Each such round is ended as aborted and each such match gets a closing event.</remarks>
	/// <returns>How many matches were ended.</returns>
	Task<int> CloseUnfinishedAsync(CancellationToken cancellationToken = default);
}