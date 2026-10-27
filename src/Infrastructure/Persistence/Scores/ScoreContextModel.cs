using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Persistence.Scores;

internal static class ScoreContextModel
{
	/// <summary>Maps the scores table without including it in this context's migrations.</summary>
	internal static void MapScoreReference(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<ScoreRow>(row =>
		{
			row.ToTable("scores", table => table.ExcludeFromMigrations());
			row.HasKey(score => score.Id);
		});
	}
}