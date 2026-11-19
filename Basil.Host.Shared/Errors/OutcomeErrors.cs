using Basil.Domain;
using System.Globalization;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Scores.Objects;
using Basil.Application.Servers.Objects;
using Basil.Domain.Authentication;
using Basil.Domain.Chat;
using Basil.Domain.Content;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer.Room;
using Basil.Domain.Users;

namespace Basil.Host.Shared.Errors;

public enum ErrorKind : byte
{
	PrivilegeDenied = 1,

	Forbidden,

	WrongPassword,

	NotFound,

	Conflict,

	Locked,

	SystemAccount,

	Gone,

	Invalid
}

public readonly record struct ErrorInfo(string Code, ErrorKind Kind);

public static class BusinessErrors
{
	public static ErrorInfo Describe(BasilException error)
	{
		var code = error switch
		{
			InvalidAddressException => "invalidAddress",
			InvalidBannerWindowException => "invalidBannerWindow",
			InvalidFileNameException => "invalidFileName",
			InvalidFaqNameException => "invalidFaqName",
			InvalidImageException => "invalidImage",
			UploadTooLargeException => "tooLarge",
			ImageDimensionsTooLargeException => "dimensionsTooLarge",
			InvalidChannelNameException => "invalidChannelName",
			InvalidChannelTopicException => "invalidChannelTopic",
			InvalidChannelPrivilegesException => "invalidChannelPrivileges",
			EmptyMessageException => "empty",
			InvalidMessageException => "invalidText",
			InvalidUserNameException => "invalidName",
			InvalidCountryException => "invalidCountry",
			InvalidSilenceException => "invalidSilence",
			InvalidModsException => "invalidMods",
			InvalidRoomSettingsException => "invalidSettings",
			_ => "invalid"
		};
		return new ErrorInfo(code, ErrorKind.Invalid);
	}
}

public static class OutcomeErrors
{
	public const string PrivilegeDeniedCode = "privilegeDenied";

	private const int OkValue = 1;

	public static bool IsOk<TOutcome>(TOutcome outcome) where TOutcome : struct, Enum
	{
		return Convert.ToInt32(outcome, CultureInfo.InvariantCulture) == OkValue;
	}

	public static ErrorInfo Describe<TOutcome>(TOutcome outcome) where TOutcome : struct, Enum
	{
		if (EqualityComparer<TOutcome>.Default.Equals(outcome, default))
			return new ErrorInfo(PrivilegeDeniedCode, ErrorKind.PrivilegeDenied);

		var name = outcome.ToString();
		var code = char.ToLowerInvariant(name[0]) + name[1..];
		var kind = outcome switch
		{
			UserActionResult user => user switch
			{
				UserActionResult.NotOutranking or UserActionResult.NotSelf => ErrorKind.Forbidden,
				UserActionResult.SystemAccount => ErrorKind.SystemAccount,
				UserActionResult.WrongPassword => ErrorKind.WrongPassword,
				UserActionResult.Deleted or UserActionResult.NotFound => ErrorKind.NotFound,
				UserActionResult.NameTaken => ErrorKind.Conflict,
				UserActionResult.Locked => ErrorKind.Locked,
				_ => ErrorKind.Invalid
			},
			RoomActionResult room => room switch
			{
				RoomActionResult.RoleDenied or RoomActionResult.IsManager or RoomActionResult.Blocked => ErrorKind.Forbidden,
				RoomActionResult.WrongPassword => ErrorKind.WrongPassword,
				RoomActionResult.NotFound => ErrorKind.NotFound,
				RoomActionResult.RoomClosed => ErrorKind.Gone,
				RoomActionResult.Locked => ErrorKind.Locked,
				RoomActionResult.SpeedModNotAllowed or RoomActionResult.NotFreemod or RoomActionResult.NoTeams =>
					ErrorKind.Invalid,
				_ => ErrorKind.Conflict
			},
			ChannelJoinResult channel => channel switch
			{
				ChannelJoinResult.AccessDenied or ChannelJoinResult.Blocked or ChannelJoinResult.Silenced => ErrorKind.Forbidden,
				ChannelJoinResult.NotFound => ErrorKind.NotFound,
				ChannelJoinResult.Closed => ErrorKind.Gone,
				_ => ErrorKind.Conflict
			},
			ServerActionResult server => server switch
			{
				ServerActionResult.Locked => ErrorKind.Locked,
				ServerActionResult.NotFound => ErrorKind.NotFound,
				ServerActionResult.NameTaken => ErrorKind.Conflict,
				_ => ErrorKind.Invalid
			},
			BeatmapsetEditResult beatmapset => beatmapset == BeatmapsetEditResult.NotFound
				? ErrorKind.NotFound
				: ErrorKind.Locked,
			RulesetUpdateResult or ScoreRecalculationResult => ErrorKind.Conflict,
			SessionAuthResult connection => connection == SessionAuthResult.Rejected
				? ErrorKind.Invalid
				: ErrorKind.Conflict,
			_ => throw new ArgumentException($"No error table for {typeof(TOutcome).Name}.", nameof(outcome))
		};
		return new ErrorInfo(code, kind);
	}
}