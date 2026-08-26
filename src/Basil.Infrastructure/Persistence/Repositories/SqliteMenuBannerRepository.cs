using Basil.Application.Abstractions.Content;
using Basil.Domain.Content;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IMenuBannerRepository" />
/// <remarks>Rows map through the private mutable <c>MenuBannerRow</c> DTO. Each method opens its own connection.</remarks>
public sealed class SqliteMenuBannerRepository(string connectionString) : IMenuBannerRepository
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<MenuBanner>> FetchAllAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var rows = await connection.QueryAsync<MenuBannerRow>("SELECT * FROM MenuBanners ORDER BY CreatedAt");
		return [.. rows.Select(r => r.ToMenuBanner())];
	}

	/// <inheritdoc />
	public async Task<MenuBanner?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var row = await connection.QuerySingleOrDefaultAsync<MenuBannerRow>(
			"SELECT * FROM MenuBanners WHERE Id = @Id", new { Id = id });
		return row?.ToMenuBanner();
	}

	/// <inheritdoc />
	public async Task<MenuBanner> CreateAsync(string image, string url, DateTime? begins, DateTime? expires,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var createdAt = DateTime.UtcNow;
		var id = await connection.ExecuteScalarAsync<int>(
			"""
			INSERT INTO MenuBanners (Image, Url, Begins, Expires, CreatedAt)
			VALUES (@Image, @Url, @Begins, @Expires, @CreatedAt);
			SELECT last_insert_rowid();
			""",
			new { Image = image, Url = url, Begins = begins, Expires = expires, CreatedAt = createdAt });

		return new MenuBanner(id, image, url, begins, expires, createdAt);
	}

	/// <inheritdoc />
	public async Task<MenuBanner?> UpdateAsync(int id, string? image, string? url, DateTime? begins,
		DateTime? expires, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync(
			"""
			UPDATE MenuBanners
			SET Image = COALESCE(@Image, Image),
			    Url = COALESCE(@Url, Url),
			    Begins = COALESCE(@Begins, Begins),
			    Expires = COALESCE(@Expires, Expires)
			WHERE Id = @Id
			""",
			new { Id = id, Image = image, Url = url, Begins = begins, Expires = expires });

		return await FetchByIdAsync(id, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var affected = await connection.ExecuteAsync("DELETE FROM MenuBanners WHERE Id = @Id", new { Id = id });
		return affected > 0;
	}

	/// <summary>Creates a new SQLite connection using the repository's connection string.</summary>
	private SqliteConnection Connect()
	{
		return SqliteConnectionFactory.Open(connectionString);
	}

	/// <summary>A mutable row DTO matching the MenuBanners table columns.</summary>
	private sealed class MenuBannerRow
	{
		public int Id { get; set; }
		public string Image { get; set; } = "";
		public string Url { get; set; } = "";
		public DateTime? Begins { get; set; }
		public DateTime? Expires { get; set; }
		public DateTime CreatedAt { get; set; }

		/// <summary>Builds a <see cref="MenuBanner" /> from this row.</summary>
		public MenuBanner ToMenuBanner()
		{
			return new MenuBanner(Id, Image, Url, Begins, Expires, CreatedAt);
		}
	}
}