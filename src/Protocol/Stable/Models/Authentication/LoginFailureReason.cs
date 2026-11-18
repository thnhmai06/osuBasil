namespace Basil.Protocol.Game.Stable.Models.Authentication;

public enum LoginFailureReason : sbyte
{
	AuthenticationFailed = -1,

	OldClient = -2,

	Banned = -3,

	BannedAlternate = -4,

	ErrorOccurred = -5,

	NeedsSupporter = -6,

	PasswordReset = -7,

	RequiresVerification = -8
}