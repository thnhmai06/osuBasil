using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Basil.Infrastructure.Persistence.Postgres;

/// <summary>Lets the Entity Framework command-line tools build the context to create migrations; the server never uses it.</summary>
internal sealed class BasilDbContextFactory : IDesignTimeDbContextFactory<BasilDbContext>
{
	public BasilDbContext CreateDbContext(string[] args)
	{
		var options = new DbContextOptionsBuilder<BasilDbContext>()
			.UseNpgsql("Host=localhost;Database=basil")
			.UseSnakeCaseNamingConvention()
			.Options;
		return new BasilDbContext(options);
	}
}
