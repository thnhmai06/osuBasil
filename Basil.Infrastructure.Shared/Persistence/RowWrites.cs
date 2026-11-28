using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Shared.Persistence;

public static class RowWrites
{
	extension(DbContext context)
	{
		public async Task UpsertAsync<TRow>(TRow row, Expression<Func<TRow, bool>> key) where TRow : class
		{
			var stored = await context.Set<TRow>().AsTracking().FirstOrDefaultAsync(key);
			if (stored is null)
				context.Add(row);
			else
				context.Entry(stored).CurrentValues.SetValues(row);
			await context.SaveChangesAsync();
		}

		public async Task ReplaceAsync<TRow>(TRow row, Expression<Func<TRow, bool>> key) where TRow : class
		{
			var stored = await context.Set<TRow>().AsTracking().FirstOrDefaultAsync(key);
			if (stored is null)
				return;

			context.Entry(stored).CurrentValues.SetValues(row);
			await context.SaveChangesAsync();
		}
	}
}