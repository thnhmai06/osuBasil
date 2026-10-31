using Basil.Application.Beatmaps.Objects;
using Basil.Application.Chat.Objects;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;

namespace Basil.Host.Shared;

/// <summary>How a refused operation is classified, independent of the transport that reports it.</summary>
public enum ErrorKind : byte
{
	/// <summary>The caller lacks permissions.</summary>
	PermissionDenied = 1,

	/// <summary>The caller may not do this although the request is valid (hierarchy, ownership).</summary>
	Forbidden,

	/// <summary>The password given is wrong.</summary>
	WrongPassword,

	/// <summary>The thing does not exist or the caller may not see it.</summary>
	NotFound,

	/// <summary>The state of the system does not allow the operation.</summary>
	Conflict,

	/// <summary>An area is locked.</summary>
	Locked,

	/// <summary>The target is BasilBot's fixed record.</summary>
	SystemAccount,

	/// <summary>The room is closed.</summary>
	Gone,

	/// <summary>The input is not valid.</summary>
	Invalid
}

/// <summary>A refusal as a stable machine-readable code and its classification.</summary>
/// <param name="Code">The camelCase code clients can rely on.</param>
/// <param name="Kind">The classification.</param>
public readonly record struct ErrorInfo(string Code, ErrorKind Kind);

/// <summary>The one table that turns the outcome of an operation into a code and a kind, shared by every transport.</summary>
public static class OutcomeErrors
{
	/// <summary>The code of a refusal for lack of permissions.</summary>
	public const string PermissionDeniedCode = "permissionDenied";

	/// <summary>Describes an outcome that is not <c>Ok</c>.</summary>
	/// <param name="outcome">The outcome of any Application object.</param>
	/// <returns>The code and kind of the refusal.</returns>
	/// <exception cref="ArgumentException">The outcome's enum is not covered by the table.</exception>
	public static ErrorInfo Describe<TOutcome>(TOutcome outcome) where TOutcome : struct, Enum
	{
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
				RoomOutcome.RoomClosed => ErrorKind.Gone,
				RoomOutcome.Locked => ErrorKind.Locked,
				RoomOutcome.InvalidMods or RoomOutcome.InvalidSettings or RoomOutcome.SpeedModNotAllowed
					or RoomOutcome.NotFreemod or RoomOutcome.NoTeams => ErrorKind.Invalid,
				_ => ErrorKind.Conflict
			},
			ChannelOutcome channel => channel switch
			{
				ChannelOutcome.AccessDenied or ChannelOutcome.Blocked => ErrorKind.Forbidden,
				ChannelOutcome.NotFound => ErrorKind.NotFound,
				ChannelOutcome.Empty or ChannelOutcome.InvalidText => ErrorKind.Invalid,
				_ => ErrorKind.Conflict
			},
			ServerOutcome server => server switch
			{
				ServerOutcome.Locked => ErrorKind.Locked,
				ServerOutcome.NameTaken => ErrorKind.Conflict,
				_ => ErrorKind.Invalid
			},
			BeatmapsetOutcome => ErrorKind.Locked,
			ConnectionOutcome connection => connection == ConnectionOutcome.Rejected
				? ErrorKind.Invalid
				: ErrorKind.Conflict,
			_ => throw new ArgumentException($"No error table for {typeof(TOutcome).Name}.", nameof(outcome))
		};
		return new ErrorInfo(code, kind);
	}
}