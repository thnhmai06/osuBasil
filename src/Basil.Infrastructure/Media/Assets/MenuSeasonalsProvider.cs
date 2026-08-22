using Basil.Application.Configurations;
using Basil.Application.Services.Content;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp.Web.Providers;
using SixLabors.ImageSharp.Web.Resolvers;

namespace Basil.Infrastructure.Media.Assets;

/// <summary>
///     Serves menu seasonal backgrounds on the `assets.` host through
///     ImageSharp.Web.
/// </summary>
/// <remarks>
///     Menu icon is handled separately: it has no filename in its URL (<c>/menu/icon</c>), and can be
///     an external URL instead of a local file, so resolving it needs the async
///     <see cref="MenuIconService" /> rather than a plain <see cref="IFileProvider" /> lookup — see
///     <c>MenuIconImageProvider</c>.
/// </remarks>
public sealed class MenuSeasonalsProvider : IImageProvider
{
	private const string SeasonalsPrefix = "/menu/seasonals/";

	private readonly PhysicalFileProvider _seasonals;

	/// <summary>Initializes a new instance of the <see cref="MenuSeasonalsProvider" /> class.</summary>
	/// <param name="storage">The storage folders this provider resolves files against.</param>
	/// <param name="server">The server's configured domain, used to scope this provider to `assets.` hosts.</param>
	public MenuSeasonalsProvider(IOptions<StorageOptions> storage, IOptions<ServerOptions> server)
	{
		Directory.CreateDirectory(storage.Value.MenuSeasonalsPath);
		_seasonals = new PhysicalFileProvider(storage.Value.MenuSeasonalsPath);

		var hosts = AssetsHost.AssetsHostsFor(server.Value.Domain);
		Match = context => AssetsHost.Matches(context, hosts) &&
		                  context.Request.Path.StartsWithSegments(SeasonalsPrefix.TrimEnd('/'));
	}

	/// <inheritdoc />
	public ProcessingBehavior ProcessingBehavior => ProcessingBehavior.CommandOnly;

	/// <inheritdoc />
	public Func<HttpContext, bool> Match { get; set; }

	/// <inheritdoc />
	public bool IsValidRequest(HttpContext context)
	{
		return true;
	}

	/// <inheritdoc />
	public Task<IImageResolver?> GetAsync(HttpContext context)
	{
		var path = context.Request.Path.Value ?? "";

		var relativePath = path[SeasonalsPrefix.Length..];
		var fileInfo = _seasonals.GetFileInfo(relativePath);
		IImageResolver? resolver = fileInfo.Exists ? new FileProviderImageResolver(fileInfo) : null;
		return Task.FromResult(resolver);
	}
}