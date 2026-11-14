using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Content.Persistence;

internal sealed class ContentDbContext(DbContextOptions<ContentDbContext> options) : DbContext(options),
	IDatabaseContext<ContentDbContext>
{
	/// <summary>The stored menu banners.</summary>
	public DbSet<MenuBannerRow> MenuBanners => Set<MenuBannerRow>();

	static string IDatabaseContext<ContentDbContext>.HistoryTable => "__ef_migrations_content";

	static ContentDbContext IDatabaseContext<ContentDbContext>.Create(DbContextOptions<ContentDbContext> options)
	{
		return new ContentDbContext(options);
	}

	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		ContextModel.ApplyModuleConfigurations<ContentDbContext>(modelBuilder);
	}
}