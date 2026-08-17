using Basil.Application.Abstractions.Content;
using Basil.Domain.Content;

namespace Basil.IntegrationTests;

/// <summary>
///     A real, stateful in-memory <see cref="IMenuBannerRepository" />, standing in for the SQLite
///     repository in tests that run without a database (needs read-your-writes across write then read).
/// </summary>
internal sealed class InMemoryMenuBannerRepository : IMenuBannerRepository
{
	private readonly Dictionary<int, MenuBanner> _banners = [];
	private int _nextId = 1;

	public Task<IReadOnlyList<MenuBanner>> FetchAllAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult<IReadOnlyList<MenuBanner>>(
			[.. _banners.Values.OrderBy(b => b.CreatedAt)]);
	}

	public Task<MenuBanner?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_banners.GetValueOrDefault(id));
	}

	public Task<MenuBanner> CreateAsync(string image, string url, DateTime begins, DateTime expires,
		CancellationToken cancellationToken = default)
	{
		var banner = new MenuBanner(_nextId++, image, url, begins, expires, DateTime.UtcNow);
		_banners[banner.Id] = banner;
		return Task.FromResult(banner);
	}

	public Task<MenuBanner?> UpdateAsync(int id, string? image, string? url, DateTime? begins, DateTime? expires,
		CancellationToken cancellationToken = default)
	{
		if (!_banners.TryGetValue(id, out var existing)) return Task.FromResult<MenuBanner?>(null);

		var updated = existing with
		{
			Image = image ?? existing.Image,
			Url = url ?? existing.Url,
			Begins = begins ?? existing.Begins,
			Expires = expires ?? existing.Expires
		};
		_banners[id] = updated;
		return Task.FromResult<MenuBanner?>(updated);
	}

	public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_banners.Remove(id));
	}
}
