using Basil.Application.Capabilities.Content;
using Basil.Application.Persistence.Content;
using Basil.Application.Persistence.Users;
using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Sessions;

namespace Basil.Application.Runtime.Servers;

/// <summary>The ports through which the server performs its behaviour.</summary>
/// <param name="Session">The runtime dependencies for online users and general channels.</param>
/// <param name="Inspector">Identifies uploaded picture formats and dimensions.</param>
/// <param name="Screenshots">The stored screenshots.</param>
/// <param name="Lobby">The open multiplayer rooms.</param>
/// <param name="Users">The stored users.</param>
internal sealed record ServerContext(
	SessionContext Session,
	IScreenshotInspector Inspector,
	IScreenshotStorage Screenshots,
	Lobby Lobby,
	IUserRepository Users);