using Basil.Domain.Users;

namespace Basil.Application.Users;

/// <summary>Names an account by its id or by its user name.</summary>
public readonly record struct UserReference
{
	private UserReference(int? id, string? name)
	{
		Id = id;
		Name = name;
	}

	public int? Id { get; }

	public string? Name { get; }

	public static UserReference ById(int id)
	{
		return new UserReference(id, null);
	}

	public static UserReference ByName(string name)
	{
		return new UserReference(null, name);
	}

	/// <summary>
	///     Gives the form in which user names are compared, or <see langword="null" /> when the text is not a valid user
	///     name.
	/// </summary>
	public static string? Normalize(string name)
	{
		try
		{
			return new UserData { Name = name }.NormalizedName;
		}
		catch (InvalidUserNameException)
		{
			return null;
		}
	}

	public bool Names(UserRecord account)
	{
		return Id is { } id
			? account.Id == id
			: Name is { } name && Normalize(name) == account.Value.NormalizedName;
	}
}