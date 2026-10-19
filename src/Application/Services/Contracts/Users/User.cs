using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Users;

/// <summary>Owns the state changes of one account.</summary>
public sealed class User
{
	/// <summary>Gets the account this object represents.</summary>
	public UserRecord Record { get; }

	/// <summary>The runtime context for account behaviour.</summary>
	internal UserContext Context { get; }

	/// <summary>Creates the account object for a stored user.</summary>
	/// <param name="record">The account to represent.</param>
	/// <param name="context">The runtime context for account behaviour.</param>
	internal User(UserRecord record, UserContext context)
	{
		Record = record;
		Context = context;
	}

	/// <summary>Scopes this account's behaviour to a connection and its authority.</summary>
	/// <param name="by">The connection acting on the account.</param>
	/// <returns>A short-lived scope for this account and connection.</returns>
	public ScopedUser ScopeTo(Connection by)
	{
		return new ScopedUser(this, by);
	}

	/// <summary>Changes the account data and keeps its online session in sync.</summary>
	/// <param name="change">The change to apply to the account.</param>
	internal void Apply(Action<UserData> change)
	{
		change(Record.Value);
		var session = Context.Registry.Sessions.GetValueOrDefault(Record);
		if (session is not null && !ReferenceEquals(session.User, Record))
			change(session.User.Value);
	}
}
