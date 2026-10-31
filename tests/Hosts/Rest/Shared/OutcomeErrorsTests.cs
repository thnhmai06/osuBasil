using Basil.Application.Beatmaps.Objects;
using Basil.Application.Chat.Objects;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Host.Rest.Http;
using Basil.Host.Shared;
using static Basil.Host.Shared.ErrorKind;

namespace Basil.Host.Rest.Tests.Shared;

/// <summary>The outcome to error table: ADR-009 gives the classification of every refusal; each row is a case.</summary>
public sealed class OutcomeErrorsTests
{
	private static readonly Dictionary<UserOutcome, ErrorKind> Users = new()
	{
		[UserOutcome.NotOutranking] = Forbidden, [UserOutcome.SystemAccount] = SystemAccount,
		[UserOutcome.InvalidRestriction] = Invalid, [UserOutcome.Self] = Invalid, [UserOutcome.Deleted] = NotFound,
		[UserOutcome.WrongPassword] = WrongPassword, [UserOutcome.InvalidName] = Invalid,
		[UserOutcome.NameTaken] = Conflict, [UserOutcome.NotFound] = NotFound, [UserOutcome.Locked] = Locked,
		[UserOutcome.NotSelf] = Forbidden, [UserOutcome.InvalidLabel] = Invalid, [UserOutcome.InvalidCountry] = Invalid,
		[UserOutcome.InvalidImage] = Invalid, [UserOutcome.DimensionsTooLarge] = Invalid, [UserOutcome.TooLarge] = Invalid
	};

	private static readonly Dictionary<RoomOutcome, ErrorKind> Rooms = new()
	{
		[RoomOutcome.RoleDenied] = Forbidden, [RoomOutcome.AlreadySeated] = Conflict, [RoomOutcome.Banned] = Conflict,
		[RoomOutcome.InAnotherRoom] = Conflict, [RoomOutcome.WrongPassword] = WrongPassword, [RoomOutcome.Full] = Conflict,
		[RoomOutcome.IsObserver] = Conflict, [RoomOutcome.NotInRoom] = Conflict, [RoomOutcome.IsManager] = Forbidden,
		[RoomOutcome.NotBanned] = Conflict, [RoomOutcome.TargetOffline] = Conflict, [RoomOutcome.AlreadyInRoom] = Conflict,
		[RoomOutcome.TooManyReferees] = Conflict, [RoomOutcome.AlreadyReferee] = Conflict,
		[RoomOutcome.NotReferee] = Conflict, [RoomOutcome.IsCreator] = Conflict, [RoomOutcome.IsPlayer] = Conflict,
		[RoomOutcome.NotObserver] = Conflict, [RoomOutcome.TooManyRooms] = Conflict, [RoomOutcome.NoRoomId] = Conflict,
		[RoomOutcome.SlotNotOpen] = Conflict, [RoomOutcome.RoomLocked] = Conflict, [RoomOutcome.Locked] = Locked,
		[RoomOutcome.InProgress] = Conflict, [RoomOutcome.OwnSlot] = Conflict, [RoomOutcome.NoTeams] = Invalid,
		[RoomOutcome.NotFreemod] = Invalid, [RoomOutcome.SpeedModNotAllowed] = Invalid, [RoomOutcome.InvalidMods] = Invalid,
		[RoomOutcome.InvalidSettings] = Invalid, [RoomOutcome.NoBeatmap] = Conflict, [RoomOutcome.NotPlaying] = Conflict,
		[RoomOutcome.NotInProgress] = Conflict, [RoomOutcome.NoCountdown] = Conflict, [RoomOutcome.RoundMismatch] = Conflict,
		[RoomOutcome.RoomClosed] = Gone, [RoomOutcome.Blocked] = Forbidden
	};

	private static readonly Dictionary<ChannelOutcome, ErrorKind> Channels = new()
	{
		[ChannelOutcome.AlreadyMember] = Conflict, [ChannelOutcome.NotMember] = Conflict, [ChannelOutcome.Closed] = Conflict,
		[ChannelOutcome.Empty] = Invalid, [ChannelOutcome.InvalidText] = Invalid, [ChannelOutcome.Blocked] = Forbidden,
		[ChannelOutcome.TargetSilenced] = Conflict, [ChannelOutcome.AccessDenied] = Forbidden, [ChannelOutcome.NotFound] = NotFound
	};

	private static readonly Dictionary<ServerOutcome, ErrorKind> Servers = new()
	{
		[ServerOutcome.NameTaken] = Conflict, [ServerOutcome.TooLarge] = Invalid, [ServerOutcome.InvalidImage] = Invalid,
		[ServerOutcome.DimensionsTooLarge] = Invalid, [ServerOutcome.Locked] = Locked
	};

	private static readonly Dictionary<ConnectionOutcome, ErrorKind> Connections = new()
	{
		[ConnectionOutcome.TargetOffline] = Conflict, [ConnectionOutcome.Self] = Conflict,
		[ConnectionOutcome.Blocked] = Conflict, [ConnectionOutcome.Closed] = Conflict,
		[ConnectionOutcome.OtherClient] = Conflict, [ConnectionOutcome.Rejected] = Invalid
	};

	public static TheoryData<string, string> Rows()
	{
		var rows = new TheoryData<string, string>();
		foreach (var (k, v) in Users) rows.Add("User:" + k, v.ToString());
		foreach (var (k, v) in Rooms) rows.Add("Room:" + k, v.ToString());
		foreach (var (k, v) in Channels) rows.Add("Channel:" + k, v.ToString());
		foreach (var (k, v) in Servers) rows.Add("Server:" + k, v.ToString());
		foreach (var (k, v) in Connections) rows.Add("Connection:" + k, v.ToString());
		rows.Add("Beatmapset:Locked", Locked.ToString());
		return rows;
	}

	[Theory]
	[MemberData(nameof(Rows))]
	public void Describe_EveryRefusal_HasTheKindThatTheSpecificationGives(string row, string expected)
	{
		var (area, name) = (row.Split(':')[0], row.Split(':')[1]);
		var info = area switch
		{
			"User" => OutcomeErrors.Describe(Enum.Parse<UserOutcome>(name)),
			"Room" => OutcomeErrors.Describe(Enum.Parse<RoomOutcome>(name)),
			"Channel" => OutcomeErrors.Describe(Enum.Parse<ChannelOutcome>(name)),
			"Server" => OutcomeErrors.Describe(Enum.Parse<ServerOutcome>(name)),
			"Connection" => OutcomeErrors.Describe(Enum.Parse<ConnectionOutcome>(name)),
			_ => OutcomeErrors.Describe(Enum.Parse<BeatmapsetOutcome>(name))
		};

		Assert.Equal(expected, info.Kind.ToString());
		Assert.Equal(char.ToLowerInvariant(name[0]) + name[1..], info.Code);
	}

	[Fact]
	public void Tables_CoverEveryRefusalOfEveryEnum()
	{
		Assert.Equal(Enum.GetValues<UserOutcome>().Length - 1, Users.Count);
		Assert.Equal(Enum.GetValues<RoomOutcome>().Length - 1, Rooms.Count);
		Assert.Equal(Enum.GetValues<ChannelOutcome>().Length - 1, Channels.Count);
		Assert.Equal(Enum.GetValues<ServerOutcome>().Length - 1, Servers.Count);
		Assert.Equal(Enum.GetValues<ConnectionOutcome>().Length - 1, Connections.Count);
	}

	[Theory]
	[InlineData(PermissionDenied, 403)]
	[InlineData(Forbidden, 403)]
	[InlineData(WrongPassword, 403)]
	[InlineData(NotFound, 404)]
	[InlineData(Conflict, 409)]
	[InlineData(Locked, 409)]
	[InlineData(SystemAccount, 409)]
	[InlineData(Gone, 410)]
	[InlineData(Invalid, 422)]
	public void StatusOf_EveryKind_IsTheStatusOfAdr009(ErrorKind kind, int status)
	{
		Assert.Equal(status, OutcomeResults.StatusOf(kind));
	}
}
