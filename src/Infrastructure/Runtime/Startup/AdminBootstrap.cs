using System.Text;
using Basil.Application.Storage.Contracts.Auth;
using Basil.Application.Storage.Contracts.Common;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Startup;

/// <summary>
///     Gives a fresh server its first administrator: an account named <c>admin</c> with the password <c>admin</c> and
///     every permission, created only when no live account exists yet.
/// </summary>
/// <remarks>
///     This is a bootstrap, not a repair: once any live account exists, it never creates the account again. The
///     created account must be given real credentials at once.
/// </remarks>
internal sealed class AdminBootstrap(
	IUserRepository users,
	ICredentialRepository credentials,
	ILogger<AdminBootstrap> logger)
{
	/// <summary>Creates the first administrator account when the server has no live account other than its own.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>
	///     When a live account other than <see cref="BasilBot" /> exists, nothing is created. The warning about the
	///     created credentials is logged only when the account is created.
	/// </remarks>
	public async Task RunAsync(CancellationToken cancellationToken)
	{
		var live = await users.ListAsync(new UserQuery(Deleted: false), new PageRequest(0, 2), cancellationToken);
		if (live.Items.Any(user => user.Id != BasilBot.Id))
		{
			logger.LogDebug("A live account already exists; not bootstrapping an administrator");
			return;
		}

		UserRecord admin;
		try
		{
			admin = await users.CreateAsync(
				new UserData { Name = "admin", Permissions = PermissionsGroups.All }, cancellationToken);
		}
		catch (AlreadyExistsException exception)
		{
			logger.LogWarning(exception, "An account named 'admin' already exists; not bootstrapping an administrator");
			return;
		}

		await credentials.CreateOrUpdateAsync(
			new Credentials(admin, new Md5(Encoding.UTF8.GetBytes("admin"))), cancellationToken);
		logger.LogWarning(
			"No account existed, so the administrator account 'admin' with the password 'admin' and every permission was created. Change this password and these credentials now.");
	}
}
