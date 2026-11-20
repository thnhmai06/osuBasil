using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Sessions.Persistence;

/// <summary>
///     Migrates the sessions database, first carrying the history of a database created before the module was renamed
///     (the Authentication module) over to the current history table so that applied migrations are not replayed.
/// </summary>
internal sealed class SessionsMigration : IModuleMigration
{
	private const string RenameLegacyHistory = """
	                                           DO $$
	                                           BEGIN
	                                           	IF to_regclass('"__ef_migrations_auth"') IS NOT NULL
	                                           	   AND to_regclass('"__ef_migrations_sessions"') IS NULL THEN
	                                           		ALTER TABLE "__ef_migrations_auth" RENAME TO "__ef_migrations_sessions";
	                                           	END IF;
	                                           END $$;
	                                           """;

	public async Task MigrateAsync(Database database, CancellationToken ct)
	{
		await using var context = database.CreateConfirmedContext<SessionsDbContext>();
		await context.Database.ExecuteSqlRawAsync(RenameLegacyHistory, ct);
		await context.Database.MigrateAsync(ct);
	}
}
