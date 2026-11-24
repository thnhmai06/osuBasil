using Basil.Application.Shared.Replies;
using Basil.Application.Bot.Replies;
using Basil.Application.Multiplayer.Objects;
using Basil.Domain.Multiplayer.Room;

namespace Basil.Application.Bot.Commands;

internal static class MpReplies
{
	public static string Describe(IReplies<BotReply> replies, RoomActionResult result)
	{
		return result == RoomActionResult.Denied
			? replies.Text(BotReply.MpMissingPrivileges)
			: replies.Text(Of(result), new { outcome = result.ToString() });
	}

	public static string Describe(IReplies<BotReply> replies, LobbyResult result)
	{
		return result == LobbyResult.Denied
			? replies.Text(BotReply.MpPlayersOnly)
			: replies.Text(Of(result), new { outcome = result.ToString() });
	}

	public static BotReply Of(LobbyResult outcome)
	{
		return outcome switch
		{
			LobbyResult.AlreadyInRoom => BotReply.MpUserAlreadyInRoom,
			LobbyResult.TooManyRooms => BotReply.MpRoomLimitReached,
			LobbyResult.NoRoomId => BotReply.MpNoMatchesAvailable,
			LobbyResult.Locked => BotReply.MpRoomsLocked,
			_ => BotReply.MpGenericFailed
		};
	}

	public static string Describe(IReplies<BotReply> replies, RoomJoinResult result)
	{
		return result == RoomJoinResult.Denied
			? replies.Text(BotReply.MpPlayersOnly)
			: replies.Text(Of(result), new { outcome = result.ToString() });
	}

	public static BotReply Of(RoomJoinResult outcome)
	{
		return outcome switch
		{
			RoomJoinResult.RoomClosed => BotReply.MpMatchClosed,
			RoomJoinResult.NotInRoom => BotReply.MpTargetNotInMatch,
			RoomJoinResult.Banned => BotReply.MpBannedFromMatch,
			RoomJoinResult.Full => BotReply.MpMatchIsFull,
			RoomJoinResult.AlreadySeated => BotReply.MpAlreadyInThisMatch,
			RoomJoinResult.InAnotherRoom => BotReply.MpTargetInAnotherMatch,
			RoomJoinResult.IsObserver => BotReply.MpTargetIsObserver,
			RoomJoinResult.IsPlayer => BotReply.MpTargetIsPlayer,
			RoomJoinResult.NotObserver => BotReply.MpTargetNotObserver,
			RoomJoinResult.WrongPassword => BotReply.MpIncorrectPassword,
			_ => BotReply.MpGenericFailed
		};
	}

	public static string Describe(IReplies<BotReply> replies, RoomInviteResult result)
	{
		return result == RoomInviteResult.Denied
			? replies.Text(BotReply.MpMissingPrivileges)
			: replies.Text(Of(result), new { outcome = result.ToString() });
	}

	public static BotReply Of(RoomInviteResult outcome)
	{
		return outcome switch
		{
			RoomInviteResult.RoomClosed => BotReply.MpMatchClosed,
			RoomInviteResult.TargetOffline => BotReply.MpTargetNotOnline,
			RoomInviteResult.AlreadyInRoom => BotReply.MpUserAlreadyInRoom,
			RoomInviteResult.Blocked => BotReply.MpCannotInvite,
			_ => BotReply.MpGenericFailed
		};
	}

	public static string Describe(IReplies<BotReply> replies, RoomTargetResult result)
	{
		return result == RoomTargetResult.Denied
			? replies.Text(BotReply.MpMissingPrivileges)
			: replies.Text(Of(result), new { outcome = result.ToString() });
	}

	public static BotReply Of(RoomTargetResult outcome)
	{
		return outcome switch
		{
			RoomTargetResult.NotFound => BotReply.MpUserNotFound,
			RoomTargetResult.RoomClosed => BotReply.MpMatchClosed,
			RoomTargetResult.InProgress => BotReply.MpMatchAlreadyInProgress,
			RoomTargetResult.NotInRoom => BotReply.MpTargetNotInMatch,
			RoomTargetResult.NotBanned => BotReply.MpTargetNotBanned,
			RoomTargetResult.IsManager => BotReply.MpTargetIsManager,
			RoomTargetResult.IsCreator => BotReply.MpTargetIsCreator,
			RoomTargetResult.AlreadyReferee => BotReply.MpTargetAlreadyReferee,
			RoomTargetResult.NotReferee => BotReply.MpTargetNotReferee,
			RoomTargetResult.SlotNotOpen => BotReply.MpDestinationSlotNotOpen,
			RoomTargetResult.RoomLocked => BotReply.MpMatchIsLocked,
			RoomTargetResult.NoTeams => BotReply.MpMatchHasNoTeams,
			RoomTargetResult.Banned => BotReply.MpTargetIsBanned,
			RoomTargetResult.TargetOffline => BotReply.MpTargetNotOnline,
			_ => BotReply.MpGenericFailed
		};
	}

	public static BotReply Of(RoomActionResult outcome)
	{
		return outcome switch
		{
			RoomActionResult.Denied => BotReply.MpMissingPrivileges,
			RoomActionResult.Banned => BotReply.MpBannedFromMatch,
			RoomActionResult.Full => BotReply.MpMatchIsFull,
			RoomActionResult.NotInRoom => BotReply.MpTargetNotInMatch,
			RoomActionResult.IsManager => BotReply.MpTargetIsManager,
			RoomActionResult.NotBanned => BotReply.MpTargetNotBanned,
			RoomActionResult.AlreadyReferee => BotReply.MpTargetAlreadyReferee,
			RoomActionResult.NotReferee => BotReply.MpTargetNotReferee,
			RoomActionResult.IsCreator => BotReply.MpTargetIsCreator,
			RoomActionResult.SlotNotOpen => BotReply.MpDestinationSlotNotOpen,
			RoomActionResult.RoomLocked => BotReply.MpMatchIsLocked,
			RoomActionResult.InProgress => BotReply.MpMatchAlreadyInProgress,
			RoomActionResult.OwnSlot => BotReply.MpOwnSlotLocked,
			RoomActionResult.NoTeams => BotReply.MpMatchHasNoTeams,
			RoomActionResult.NotFreemod => BotReply.MpFreemodNotEnabled,
			RoomActionResult.SpeedModNotAllowed => BotReply.MpSpeedModsMatchWide,
			RoomActionResult.TagNotAvailable => BotReply.MpTagNotAvailable,
			RoomActionResult.PlayerReady => BotReply.MpPlayerReady,
			RoomActionResult.NoBeatmap => BotReply.MpNoBeatmapSelected,
			RoomActionResult.NotPlaying => BotReply.MpTargetNotPlaying,
			RoomActionResult.NotInProgress => BotReply.MpMatchNotInProgress,
			RoomActionResult.RoundMismatch => BotReply.MpScoreNotCurrentRound,
			RoomActionResult.RoomClosed => BotReply.MpMatchClosed,
			_ => BotReply.MpGenericFailed
		};
	}
}