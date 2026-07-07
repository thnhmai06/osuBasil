namespace Bancho.Protocol;

/// <summary>Ported from app/packets.py's LoginFailureReason — sent as the login_reply packet's user id value on failure.</summary>
public enum LoginFailureReason
{
    AuthenticationFailed = -1,
    OldClient = -2,
    Banned = -3,
    ErrorOccurred = -5,
    NeedsSupporter = -6,
    PasswordReset = -7,
    RequiresVerification = -8
}