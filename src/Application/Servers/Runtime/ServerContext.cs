using Basil.Application.Content.Capabilities;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Application.Chat.Runtime;
using Basil.Application.Users.Connections;
using Basil.Application.Multiplayer.Runtime;

namespace Basil.Application.Servers.Runtime;

/// <summary>The ports through which the server performs its behaviour.</summary>
/// <param name="Connections">The runtime dependencies for online users.</param>
/// <param name="Chat">The runtime dependencies for general channels.</param>
/// <param name="RoomChannels">The channels of open multiplayer rooms.</param>
/// <param name="PmChannels">The private-message channels of online users.</param>
/// <param name="Inspector">Identifies uploaded picture formats and dimensions.</param>
/// <param name="Screenshots">The stored screenshots.</param>
/// <param name="Lobby">The open multiplayer rooms.</param>
/// <param name="Users">The stored users.</param>
internal sealed record ServerContext(
	ConnectionContext Connections,
	ChannelContext Chat,
	RoomChannels RoomChannels,
	PmChannels PmChannels,
	IScreenshotInspector Inspector,
	IScreenshotStorage Screenshots,
	Lobby Lobby,
	IUserRepository Users);