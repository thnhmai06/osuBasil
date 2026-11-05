namespace Basil.Testing.Persistence;

/// <summary>Reads the structure of the test database, which each engine keeps in its own catalog.</summary>
public static class SchemaQueries
{
	/// <summary>Gets a query of the names of the foreign keys of the database, as the column <c>conname</c>, ordered by name.</summary>
	public static string ForeignKeys => TestServer.IsMySql
		? "SELECT constraint_name AS conname FROM information_schema.table_constraints " +
		  "WHERE constraint_type = 'FOREIGN KEY' AND table_schema = DATABASE() ORDER BY constraint_name"
		: "SELECT conname FROM pg_constraint WHERE contype = 'f' ORDER BY conname";

	/// <summary>
	///     Gets a query of the modules whose migration history was created, as the column <c>module</c>, oldest first.
	///     A module may appear more than once.
	/// </summary>
	public static string MigrationOrder => TestServer.IsMySql
		// InnoDB writes a hyphen of a database name as @002d in its table names.
		? "SELECT SUBSTRING_INDEX(name, '__ef_migrations_', -1) AS module FROM information_schema.innodb_tables " +
		  "WHERE name LIKE CONCAT(REPLACE(DATABASE(), '-', '@002d'), '/\\_\\_ef\\_migrations\\_%') ORDER BY table_id"
		: """
		  SELECT module FROM (
		  	SELECT 'users' AS module, xmin::text::bigint AS transaction_id FROM "__ef_migrations_users"
		  	UNION ALL SELECT 'auth', xmin::text::bigint FROM "__ef_migrations_auth"
		  	UNION ALL SELECT 'beatmaps', xmin::text::bigint FROM "__ef_migrations_beatmaps"
		  	UNION ALL SELECT 'content', xmin::text::bigint FROM "__ef_migrations_content"
		  	UNION ALL SELECT 'scores', xmin::text::bigint FROM "__ef_migrations_scores"
		  	UNION ALL SELECT 'multiplayer', xmin::text::bigint FROM "__ef_migrations_multiplayer"
		  	UNION ALL SELECT 'chat', xmin::text::bigint FROM "__ef_migrations_chat"
		  ) AS migrations ORDER BY transaction_id
		  """;
}
