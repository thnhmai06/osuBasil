using Basil.Application.Contracts.Events;
using Basil.Application.Contracts.Sessions;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Client;
using Basil.Domain.Users;
using System.Threading.Channels;

namespace Basil.Application.Contracts.Users;

/// <summary>Changes users in ways that have consequences.</summary>
public interface IUserService : IEventPublisher<UserEvent>
{
	/// <summary>Silences a user until a given time.</summary>
	/// <param name="user">The user to silence.</param>
	/// <param name="endsAt">When the silence ends.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> if the user was changed; <see langword="false" /> for BasilBot, which cannot be changed this way.</returns>
	Task<bool> SilenceAsync(User user, DateTimeOffset endsAt, CancellationToken cancellationToken = default);

	/// <summary>Replaces the privileges of a user.</summary>
	/// <param name="user">The user whose privileges are changed.</param>
	/// <param name="privilege">The new privileges.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> if the user was changed; <see langword="false" /> for BasilBot, which cannot be changed this way.</returns>
	Task<bool> SetPrivilegeAsync(User user, ClientPrivileges privilege, CancellationToken cancellationToken = default);

	/// <summary>Deletes a user's account, keeping its record.</summary>
	/// <param name="user">The user to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> if the user was changed; <see langword="false" /> for BasilBot, which cannot be changed this way.</returns>
	/// <remarks>The user loses every privilege and their open connections are closed.</remarks>
	Task<bool> DeleteAsync(User user, CancellationToken cancellationToken = default);
}