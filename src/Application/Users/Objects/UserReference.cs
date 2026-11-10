using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

/// <summary>The way a caller names an account: by the id it has or by the name it was given.</summary>
/// <remarks>
///     A reference proves nothing about the account: it may name no account, or one that was deleted. Only the Users
///     module turns it into an account, after the caller has been authorized.
/// </remarks>
public readonly record struct UserReference
{
	private UserReference(int? id, string? name)
	{
		Id = id;
		Name = name;
	}

	/// <summary>Gets the id of the account, or <see langword="null" /> when the reference names it by name.</summary>
	public int? Id { get; }

	/// <summary>Gets the name of the account, or <see langword="null" /> when the reference names it by id.</summary>
	public string? Name { get; }

	/// <summary>Names an account by its id.</summary>
	/// <param name="id">The account id.</param>
	/// <returns>A reference to the account with that id.</returns>
	public static UserReference ById(int id)
	{
		return new UserReference(id, null);
	}

	/// <summary>Names an account by its name.</summary>
	/// <param name="name">The account name; names that differ only in case or in spaces versus underscores are the same name.</param>
	/// <returns>A reference to the account with that name.</returns>
	public static UserReference ByName(string name)
	{
		return new UserReference(null, name);
	}

	/// <summary>Tells whether this reference names an account.</summary>
	/// <param name="account">The account to compare with.</param>
	/// <returns><see langword="true" /> when the account has the referenced id or name.</returns>
	public bool Names(UserRecord account)
	{
		return Id is { } id
			? account.Id == id
			: Name is { } name && UserData.NormalizeName(name) == UserData.NormalizeName(account.Value.Name);
	}
}