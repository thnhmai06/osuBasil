using Basil.Application.Beatmaps.Objects;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Persistence;
using Basil.Application.Chat.Registry;
using Basil.Application.Content.Persistence;
using Basil.Application.Content.Ports;
using Basil.Application.Content.Storage;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Settings;
using Basil.Application.Shared.Storage;
using Basil.Application.Sessions.Objects;
using Basil.Application.Users.Persistence;

namespace Basil.Application.Servers.Objects;

internal sealed record ServerContext(
	SessionContext Connections,
	ChannelContext Chat,
	RoomChannels RoomChannels,
	PmChannels PmChannels,
	IScreenshotInspector Inspector,
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