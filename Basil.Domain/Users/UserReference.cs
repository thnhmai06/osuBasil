namespace Basil.Domain.Users;

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

	public bool Names(UserRecord account)
	{
		return Id is { } id
			? account.Id == id
			: Name is { } name && UserData.NormalizeName(name) == UserData.NormalizeName(account.Value.Name);
	}
}