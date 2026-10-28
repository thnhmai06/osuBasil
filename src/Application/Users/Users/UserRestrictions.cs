using Basil.Domain.Users;

namespace Basil.Application.Users.Users;

/// <summary>Owns the active restrictions of one account.</summary>
public sealed class UserRestrictions : IReadOnlyList<UserRestriction>
{
	private readonly User _user;
	private IReadOnlyList<UserRestriction> _items = Array.Empty<UserRestriction>();

	/// <summary>Creates the restriction state for an account.</summary>
	/// <param name="user">The account whose restrictions are held.</param>
	internal UserRestrictions(User user)
	{
		_user = user;
	}

	/// <inheritdoc />
	public int Count
	{
		get
		{
			using var scope = _user.Enter();
			return _items.Count;
		}
	}

	/// <inheritdoc />
	public UserRestriction this[int index]
	{
		get
		{
			using var scope = _user.Enter();
			return _items[index];
		}
	}

	/// <inheritdoc />
	public IEnumerator<UserRestriction> GetEnumerator()
	{
		using var scope = _user.Enter();
		return _items.GetEnumerator();
	}

	/// <inheritdoc />
	System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

	/// <summary>Adds a restriction that has started to the active restrictions.</summary>
	/// <param name="restriction">The restriction to add.</param>
	internal void Add(UserRestriction restriction)
	{
		using var scope = _user.Enter();
		_items = [.. _items, restriction];
	}

	/// <summary>Removes a restriction from the active restrictions.</summary>
	/// <param name="restriction">The restriction to remove.</param>
	internal void Remove(UserRestriction restriction)
	{
		using var scope = _user.Enter();
		_items = _items.Where(item => !item.Equals(restriction)).ToArray();
	}

	/// <summary>Replaces the active restrictions with the restrictions that have not ended.</summary>
	/// <param name="restrictions">The restrictions that remain active.</param>
	internal void Replace(IEnumerable<UserRestriction> restrictions)
	{
		using var scope = _user.Enter();
		_items = [.. restrictions];
	}
}
