using Basil.Application.Storage.Sessions;

namespace Basil.Application.Services.Sessions;

/// <summary>The rules about how many connections of each kind one user may hold.</summary>
internal static class ConnectionRules
{
	/// <summary>Gets a value that indicates whether one user may hold several connections of a kind at once.</summary>
	/// <param name="type">The kind of connection.</param>
	/// <returns><see langword="true" /> only for <see cref="ConnectionType.Tourney" />.</returns>
	internal static bool AllowsMany(this ConnectionType type) => type is ConnectionType.Tourney;
}