using System.Globalization;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Chat.Objects;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Scores.Objects;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;

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
			UserOutcome user => user switch
			{
				UserOutcome.NotOutranking or UserOutcome.NotSelf => ErrorKind.Forbidden,
				UserOutcome.SystemAccount => ErrorKind.SystemAccount,
				UserOutcome.WrongPassword => ErrorKind.WrongPassword,
				UserOutcome.Deleted or UserOutcome.NotFound => ErrorKind.NotFound,
				UserOutcome.NameTaken => ErrorKind.Conflict,
				UserOutcome.Locked => ErrorKind.Locked,
				_ => ErrorKind.Invalid
			},
			RoomOutcome room => room switch
			{
				RoomOutcome.RoleDenied or RoomOutcome.IsManager or RoomOutcome.Blocked => ErrorKind.Forbidden,
				RoomOutcome.WrongPassword => ErrorKind.WrongPassword,
				RoomOutcome.NotFound => ErrorKind.NotFound,
				RoomOutcome.RoomClosed => ErrorKind.Gone,
				RoomOutcome.Locked => ErrorKind.Locked,
				RoomOutcome.InvalidMods or RoomOutcome.InvalidSettings or RoomOutcome.SpeedModNotAllowed
					or RoomOutcome.NotFreemod or RoomOutcome.NoTeams => ErrorKind.Invalid,
				_ => ErrorKind.Conflict
			},
			ChannelOutcome channel => channel switch
			{
				ChannelOutcome.AccessDenied or ChannelOutcome.Blocked or ChannelOutcome.Silenced => ErrorKind.Forbidden,
				ChannelOutcome.NotFound => ErrorKind.NotFound,
				ChannelOutcome.Closed => ErrorKind.Gone,
				ChannelOutcome.Empty or ChannelOutcome.InvalidText => ErrorKind.Invalid,
				_ => ErrorKind.Conflict
			},
			ServerOutcome server => server switch
			{
				ServerOutcome.Locked => ErrorKind.Locked,
				ServerOutcome.NotFound => ErrorKind.NotFound,
				ServerOutcome.NameTaken => ErrorKind.Conflict,
				_ => ErrorKind.Invalid
			},
			BeatmapsetOutcome beatmapset => beatmapset == BeatmapsetOutcome.NotFound
				? ErrorKind.NotFound
				: ErrorKind.Locked,
			RulesetOutcome or ScorePerformanceOutcome => ErrorKind.Conflict,
			SessionOutcome connection => connection == SessionOutcome.Rejected
				? ErrorKind.Invalid
				: ErrorKind.Conflict,
			_ => throw new ArgumentException($"No error table for {typeof(TOutcome).Name}.", nameof(outcome))
		};
		return new ErrorInfo(code, kind);
	}
}