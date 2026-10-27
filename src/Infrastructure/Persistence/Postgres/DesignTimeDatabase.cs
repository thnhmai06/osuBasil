using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Postgres;

/// <summary>Builds the contexts that the EF design-time tools (<c>dotnet ef</c>) create when they generate migrations.</summary>
internal static class DesignTimeDatabase
{
	/// <summary>Makes a context for a module that uses the same conventions and history table as the running server.</summary>
	/// <typeparam name="TContext">The module's context.</typeparam>
	/// <returns>A context bound to a placeholder connection; design-time tools replace the connection when they apply.</returns>
	public static TContext CreateContext<TContext>() where TContext : DbContext, IDatabaseContext<TContext>
	{
		var options = new DbContextOptionsBuilder<TContext>()
			.UseNpgsql("Host=localhost;Database=basil",
				postgres => postgres.MigrationsHistoryTable(TContext.HistoryTable))
			.UseSnakeCaseNamingConvention()
			.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
			.Options;
		return TContext.Create(options);
	}
}