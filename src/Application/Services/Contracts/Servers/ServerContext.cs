using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Storage.Contracts.Content;
using Basil.Application.Storage.Contracts.Users;

namespace Basil.Application.Services.Contracts.Servers;

/// <summary>The ports through which the server performs its behaviour.</summary>
/// <param name="Session">The runtime dependencies for online users and general channels.</param>
/// <param name="Screenshots">The stored screenshots.</param>
/// <param name="Lobby">The open multiplayer rooms.</param>
/// <param name="Users">The stored users.</param>
internal sealed record ServerContext(
	SessionContext Session,
	IScreenshotStorage Screenshots,
	Lobby Lobby,
	IUserRepository Users);
