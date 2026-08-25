using System.Text.RegularExpressions;
using Basil.Application.Configurations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp.Web.Providers;
using SixLabors.ImageSharp.Web.Resolvers;

namespace Basil.Infrastructure.Media.Assets;

/// <summary>
///     Serves uploaded user avatars on the `a.` host through ImageSharp.Web, gaining optional
///     on-the-fly resizing via query string as a side effect.
/// </summary>
/// <remarks>
///     Only handles a user who has actually uploaded an avatar. Doesn't match (and so doesn't
///     resolve) for the BasilBot/default fallback avatars — those are embedded resources bundled with
///     `Basil.Web`, which this Infrastructure-layer provider can't depend on — so those requests fall
///     through to the existing plain `a.` route, unchanged.
/// </remarks>
public sealed partial class AvatarProvider : IImageProvider
{
	private readonly IOptions<StorageOptions> _storage;

	/// <summary>Initializes a new instance of the <see cref="AvatarProvider" /> class.</summary>
	/// <param name="storage">The storage folders this provider resolves avatar files against.</param>
	/// <param name="server">The server's configured domain, used to scope this provider to `a.` hosts.</param>
	public AvatarProvider(IOptions<StorageOptions> storage, IOptions<ServerOptions> server)
	{
		_storage = storage;

		var hosts = AssetsHost.AvatarHostsFor(server.Value.Domain);
		Match = context => AssetsHost.Matches(context, hosts) &&
		                   AvatarPathRegex().IsMatch(context.Request.Path.Value ?? "");
	}

	/// <inheritdoc />
	public ProcessingBehavior ProcessingBehavior => ProcessingBehavior.All;

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
		var match = AvatarPathRegex().Match(context.Request.Path.Value ?? "");
		if (!match.Success) return Task.FromResult<IImageResolver?>(null);

		Directory.CreateDirectory(_storage.Value.AvatarsPath);
		var file = Directory.EnumerateFiles(_storage.Value.AvatarsPath, $"{match.Groups["userId"].Value}.*")
			.FirstOrDefault();

		IImageResolver? resolver = file is not null ? new PhysicalImageResolver(new FileInfo(file)) : null;
		return Task.FromResult(resolver);
	}

	[GeneratedRegex(@"^/(?<userId>\d+)$")]
	private static partial Regex AvatarPathRegex();
}