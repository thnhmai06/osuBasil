using Basil.Application.Storage.Contracts.Sessions;

namespace Basil.Application.Services.Contracts.Users;

/// <summary>The outcome of a login attempt.</summary>
public sealed record LoginResult
{
	private LoginResult()
	{
	}

	/// <summary>Gets the connection opened for a successful login, or <see langword="null" /> on failure.</summary>
	public Connection? Connection { get; private init; }

	/// <summary>Gets the reason a login failed, or <see langword="null" /> on success.</summary>
	public LoginFailure? Failure { get; private init; }

	/// <summary>Gets a value that indicates whether the login succeeded.</summary>
	public bool Succeeded => Connection is not null;

	/// <summary>Creates a successful result carrying the newly created connection.</summary>
	/// <param name="connection">The connection opened for the login.</param>
	public static LoginResult Success(Connection connection)
	{
		return new LoginResult { Connection = connection };
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

	/// <summary>The account already has an active connection of the same kind.</summary>
	AlreadyOnline,

	/// <summary>The account may not connect with an osu!tourney client.</summary>
	NoTourneyPermission
}