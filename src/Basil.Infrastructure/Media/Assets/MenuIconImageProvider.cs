using Basil.Application.Configurations;
using Basil.Application.Services.Content;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp.Web.Providers;
using SixLabors.ImageSharp.Web.Resolvers;

namespace Basil.Infrastructure.Media.Assets;

/// <summary>
///     Serves the menu icon on the `assets.` host through ImageSharp.Web, when it's an uploaded file.
/// </summary>
/// <remarks>
///     Doesn't match when the icon is set to an external URL or isn't set at all — the request falls
///     through to the plain `GET /menu/icon` endpoint, which redirects or 404s accordingly.
/// </remarks>
public sealed class MenuIconImageProvider : IImageProvider
{
	private readonly MenuIconService _icon;

	/// <summary>Initializes a new instance of the <see cref="MenuIconImageProvider" /> class.</summary>
	/// <param name="icon">The service that resolves the current icon's path.</param>
	/// <param name="server">The server's configured domain, used to scope this provider to `assets.` hosts.</param>
	public MenuIconImageProvider(MenuIconService icon, IOptions<ServerOptions> server)
	{
		_icon = icon;
		var hosts = AssetsHost.AssetsHostsFor(server.Value.Domain);
		Match = context => AssetsHost.Matches(context, hosts) && context.Request.Path == "/menu/icon";
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
	public async Task<IImageResolver?> GetAsync(HttpContext context)
	{
		var path = await _icon.GetPathAsync(context.RequestAborted);
		if (path is null || MenuIconService.IsExternalUrl(path) || !File.Exists(path)) return null;

		return new PhysicalImageResolver(new FileInfo(path));
	}
}