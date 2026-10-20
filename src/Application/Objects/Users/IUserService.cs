using Basil.Application.Objects.Events;
using Basil.Application.Objects.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Objects.Users;

/// <summary>Gives access to the accounts' behaviour and announces what happens to them.</summary>
public interface IUserService : IEventPublisher<UserEvent>
{
	/// <summary>Gets the account object through which its behaviour is reached.</summary>
	/// <param name="user">The account to get.</param>
	/// <returns>The account object for <paramref name="user" />.</returns>
	User Of(UserRecord user);
}
