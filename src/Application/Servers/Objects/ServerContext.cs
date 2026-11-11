using Basil.Application.Beatmaps.Objects;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Persistence;
using Basil.Application.Chat.Registry;
using Basil.Application.Shared.Storage;
using Basil.Application.Content.Persistence;
using Basil.Application.Shared.Settings;
using Basil.Application.Content.Ports;
using Basil.Application.Content.Storage;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;

namespace Basil.Application.Servers.Objects;

/// <summary>The ports through which the server performs its behaviour.</summary>
/// <param name="Connections">The runtime dependencies for online users.</param>
/// <param name="Chat">The runtime dependencies for general channels.</param>
/// <param name="RoomChannels">The channels of open multiplayer rooms.</param>
/// <param name="PmChannels">The private-message channels of online users.</param>
/// <param name="Inspector">Identifies uploaded picture formats and dimensions.</param>
/// <param name="Spool">Takes uploads off the request into files while they are checked.</param>
/// <param name="Screenshots">The stored screenshots.</param>
/// <param name="Lobby">The open multiplayer rooms.</param>
/// <param name="Channels">The stored general channels.</param>
/// <param name="Users">The stored users.</param>
/// <param name="Beatmapsets">The beatmapsets of the server.</param>
/// <param name="Settings">The stored server settings.</param>
/// <param name="Faqs">The stored FAQ pages.</param>
/// <param name="Banners">The stored main-menu banners.</param>
/// <param name="BannerImages">The stored images of the main-menu banners.</param>
/// <param name="Seasonals">The stored seasonal main-menu backgrounds.</param>
/// <param name="Icon">The stored main-menu icon.</param>
internal sealed record ServerContext(
	ConnectionContext Connections,
	ChannelContext Chat,
	RoomChannels RoomChannels,
	PmChannels PmChannels,
	IScreenshotInspector Inspector,
	IUploadSpool Spool,
	IScreenshotStorage Screenshots,
	Lobby Lobby,
	IChannelRepository Channels,
	IUserRepository Users,
	Beatmapsets Beatmapsets,
	ISettingsRepository Settings,
	IFaqStorage Faqs,
	IMenuBannerRepository Banners,
	IMenuBannerStorage BannerImages,
	IMenuSeasonalsStorage Seasonals,
	IMenuIconStorage Icon);