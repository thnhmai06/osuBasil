using Basil.Application.Models.Sessions;

namespace Basil.Application.Models.Auth;

/// <summary>The outcome of a <c>Gateway.ConnectAsync</c> login attempt.</summary>
public sealed record LoginResult
{
	private LoginResult()
	{
	}

	/// <summary>Gets the session created for a successful login, or <see langword="null" /> on failure.</summary>
	public GameSession? Session { get; private init; }

	/// <summary>Gets the reason a login failed, or <see langword="null" /> on success.</summary>
	public LoginFailure? Failure { get; private init; }

	/// <summary>Gets a value that indicates whether the login succeeded.</summary>
	public bool Succeeded => Session is not null;

	/// <summary>Creates a successful result carrying the newly created session.</summary>
	/// <param name="session">The session created for the login.</param>
	public static LoginResult Success(GameSession session)
	{
		return new LoginResult { Session = session };
	}

	/// <summary>Creates a failed result carrying the reason.</summary>
	/// <param name="failure">The reason the login failed.</param>
	public static LoginResult Fail(LoginFailure failure)
	{
		return new LoginResult { Failure = failure };
	}
}

/// <summary>The reasons a login attempt can fail.</summary>
public enum LoginFailure : byte
{
	/// <summary>The username does not match a registered account.</summary>
	UnknownUser,

	/// <summary>The password did not match the account's stored credential.</summary>
	WrongPassword,

	/// <summary>The account has been deleted.</summary>
	AccountDeleted,

	/// <summary>The account already has an open game session.</summary>
	AlreadyOnline
}