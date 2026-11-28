using Basil.Application.Beatmaps.Objects;
using Basil.Application.Chat.Objects;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Rulesets.Objects;
using Basil.Application.Scores.Objects;
using Basil.Application.Servers.Objects;
using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;

namespace Basil.Host.Shared.Errors;

/// <summary>The sentence a client reads for each refusal; one text per code, and a refusal for lack of rights names the role that can act.</summary>
public static class OutcomeTexts
{
	private const string RoomRoles =
		"Only the room's host, a referee or the creator can do this; a user with the Tournament privilege can act in every room.";

	private const string UserRoles =
		"This needs the Moderator or Owner privilege, or it must be your own account.";

	private const string PlayerOnly = "Only a user with the Player privilege can create or join rooms.";

	private const string ServerRoles =
		"This needs a staff privilege: Moderator for announcements and channels, Owner for the menu and help entries, Developer for settings, Tournament for beatmapsets.";

	private static readonly Dictionary<string, string> Texts = new()
	{
		["roomClosed"] = "The room is closed.",
		["inProgress"] = "A round is in progress.",
		["notInProgress"] = "No round is in progress.",
		["notInRoom"] = "That user is not in the room.",
		["banned"] = "That user is banned from the room.",
		["notBanned"] = "That user is not banned from the room.",
		["full"] = "The room is full.",
		["isManager"] = "That user is a referee or the creator of the room.",
		["isCreator"] = "That user created the room.",
		["alreadyReferee"] = "That user is already a referee.",
		["notReferee"] = "That user is not a referee.",
		["slotNotOpen"] = "That slot is not open.",
		["roomLocked"] = "The room is locked.",
		["ownSlot"] = "A slot cannot be moved onto itself.",
		["noTeams"] = "The room is not in a team mode.",
		["notFreemod"] = "The room does not allow free mods.",
		["speedModNotAllowed"] = "A speed mod cannot be set on the room.",
		["noBeatmap"] = "The room has no beatmap.",
		["notPlaying"] = "The user is not playing.",
		["roundMismatch"] = "That is not the round in progress.",
		["alreadyMember"] = "The user is already a member.",
		["isPlayer"] = "That user is a player in the room.",
		["tagNotAvailable"] = "The room cannot use tag mode.",
		["playerReady"] = "A player is ready.",
		["notOutranking"] = "That account has a privilege rank equal to or above yours.",
		["systemAccount"] = "That is a system account and cannot be changed.",
		["self"] = "This cannot be done to your own account.",
		["deleted"] = "That account was deleted.",
		["wrongPassword"] = "The password is wrong.",
		["nameTaken"] = "That name is already in use.",
		["locked"] = "That area is locked; only the Owner can change it.",
		["notSelf"] = "Only the account itself can do this.",
		["notMember"] = "The user is not a member of the channel.",
		["closed"] = "The channel or session is closed.",
		["blocked"] = "That user has blocked you.",
		["silenced"] = "You are silenced.",
		["targetSilenced"] = "That user is silenced.",
		["accessDenied"] = "You may not read that channel.",
		["tooManyRooms"] = "You have reached the room limit.",
		["alreadyInRoom"] = "The user is already in a room.",
		["noRoomId"] = "No room id is available.",
		["targetOffline"] = "That user is not online.",
		["alreadySeated"] = "You are already in the room.",
		["inAnotherRoom"] = "That user is in another room.",
		["isObserver"] = "That user is only watching the room.",
		["notObserver"] = "That user is not watching the room.",
		["busy"] = "The same job is already running.",
		["otherClient"] = "The account is signed in on another client.",
		["rejected"] = "The request was rejected.",
		["invalidAddress"] = "The address is not valid.",
		["invalidBannerWindow"] = "The banner's end must be after its start.",
		["invalidFileName"] = "The file name is not valid.",
		["invalidFaqName"] = "The help entry name is not valid.",
		["invalidImage"] = "The picture cannot be read.",
		["tooLarge"] = "The upload is too large.",
		["dimensionsTooLarge"] = "The picture is too large in pixels.",
		["invalidChannelName"] = "The channel name is not valid.",
		["invalidChannelTopic"] = "The channel topic is not valid.",
		["invalidChannelPrivileges"] = "The channel privileges are not valid.",
		["empty"] = "The message is empty.",
		["invalidText"] = "The text is not valid.",
		["invalidName"] = "The name is not valid.",
		["invalidCountry"] = "The country code is not valid.",
		["invalidSilence"] = "The silence is not valid.",
		["invalidMods"] = "The mods are not valid.",
		["invalidSettings"] = "The room settings are not valid.",
		["invalid"] = "The input is not valid."
	};

	public static string Of(Enum outcome, ErrorInfo info)
	{
		if (info.Kind == ErrorKind.PrivilegeDenied)
			return outcome switch
			{
				RoomActionResult or RoomTargetResult or RoomInviteResult => RoomRoles,
				LobbyResult or RoomJoinResult => PlayerOnly,
				UserActionResult => UserRoles,
				ServerActionResult or ChannelJoinResult or BeatmapsetEditResult or RulesetUpdateResult
					or ScoreRecalculationResult => ServerRoles,
				_ => "You do not hold the privilege this needs."
			};

		if (info.Code == "notFound")
			return outcome switch
			{
				UserActionResult or RoomTargetResult => "No such user.",
				BeatmapsetEditResult => "No such beatmapset.",
				ChannelJoinResult => "No such channel.",
				_ => "No such item."
			};

		if (info.Code == "locked" && outcome is BeatmapsetEditResult)
			return "The beatmapset is locked.";
		if (info.Code == "locked" && outcome is LobbyResult)
			return "Room creation is locked; only room managers can create a room.";
		if (info.Code == "wrongPassword" && outcome is RoomJoinResult)
			return "The room password is wrong.";

		return Texts.GetValueOrDefault(info.Code, "The request was refused.");
	}

	public static string Of(string code)
	{
		return Texts.GetValueOrDefault(code, "The request was refused.");
	}
}
