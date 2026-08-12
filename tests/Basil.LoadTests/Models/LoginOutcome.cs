namespace Basil.LoadTests.Models;

/// <summary>
///     The result of a login attempt. Success is decided from the <c>cho-token</c> header and the
///     decoded <c>LoginReply</c> packet — an HTTP 200 alone is not success, since a failed login still
///     returns 200 with a failure packet and no token.
/// </summary>
/// <param name="Success"><see langword="true" /> when a token and a positive user id were both present.</param>
/// <param name="Token">The issued session token, when <paramref name="Success" /> is <see langword="true" />.</param>
/// <param name="UserId">The account's user id, when <paramref name="Success" /> is <see langword="true" />.</param>
/// <param name="FailureReason">
///     The <c>cho-token</c> header's failure string (e.g. <c>incorrect-credentials</c>,
///     <c>user-already-logged-in</c>) when <paramref name="Success" /> is <see langword="false" />.
/// </param>
public sealed record LoginOutcome(bool Success, string? Token, int? UserId, string? FailureReason)
{
	/// <summary>Builds a successful outcome.</summary>
	public static LoginOutcome Ok(string token, int userId)
	{
		return new LoginOutcome(true, token, userId, null);
	}

	/// <summary>Builds a failed outcome.</summary>
	public static LoginOutcome Fail(string reason)
	{
		return new LoginOutcome(false, null, null, reason);
	}
}