using Basil.Application.Configurations;
using Basil.Application.Services.Bot;
using Microsoft.Extensions.Options;

namespace Basil.Web.Routing.Bancho;

// This server stores avatars locally rather than proxying a remote CDN.
// Files are stored flat as "{userId}.{ext}" under StorageOptions.AvatarsPath.

/// <summary>
///     Registers the `a.{domain}` host's per-user avatar route: the BasilBot/default fallback images.
/// </summary>
/// <remarks>
///     A user who actually has an uploaded avatar never reaches this handler — ImageSharp.Web's
///     <c>AvatarImageProvider</c> (registered ahead of routing) serves that case directly. This route
///     stays registered for the fallback path and for the `avatar` OpenAPI document's shape, since
///     ImageSharp-handled requests aren't ASP.NET Core endpoints and so don't otherwise appear there.
/// </remarks>
internal static class AvatarRoutes
{
	/// <summary>
	///     Registers the `a.{domain}` host's per-user avatar route.
	/// </summary>
	/// <param name="group">The `a.{domain}` route group.</param>
	public static void MapAvatarGroup(this RouteGroupBuilder group)
	{
		group.MapGet("/{userId:int}", (int userId, HttpContext context) =>
			{
				var storage = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
				Directory.CreateDirectory(storage.AvatarsPath);

				// BasilBot
				if (userId == BotBootstrapService.BotId)
				{
					const string botResourceName = "Basil.Web.Resources.Avatars.basilbot.png";
					var botPath = Path.Combine(storage.AvatarsPath,
						$"{BotBootstrapService.BotId}{Path.GetExtension(botResourceName)}");
					if (!File.Exists(botPath)) TryWriteEmbeddedResource(botResourceName, botPath);
					if (File.Exists(botPath)) return Results.File(botPath, ContentTypes.Resolve(botPath));
				}

				// Regular user
				const string defaultResourceName = "Basil.Web.Resources.Avatars.default.png";
				var defaultPath = Path.Combine(storage.AvatarsPath, $"default{Path.GetExtension(defaultResourceName)}");
				if (!File.Exists(defaultPath)) TryWriteEmbeddedResource(defaultResourceName, defaultPath);
				return File.Exists(defaultPath)
					? Results.File(defaultPath, ContentTypes.Resolve(defaultPath))
					: Results.NotFound();
			})
			.WithGroupName("avatar")
			.WithSummary("Retrieve a user's avatar")
			.WithDescription(
				"Returns the user's uploaded avatar.\n\n" +
				"If the user has not uploaded one, a default avatar is returned: BasilBot's own icon for user id 0, " +
				"or a generic default avatar for every other id.\n\n" +
				"The response Content-Type matches the image format.")
			.WithTags("Avatars");
	}

	/// <summary>
	///     Extracts an embedded resource to a file, if the resource exists.
	/// </summary>
	/// <param name="resourceName">The fully qualified name of the embedded resource.</param>
	/// <param name="destinationPath">The destination file path.</param>
	private static void TryWriteEmbeddedResource(string resourceName, string destinationPath)
	{
		using var stream = typeof(AvatarRoutes).Assembly.GetManifestResourceStream(resourceName);
		if (stream is null) return;

		var tempPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
		using (var fileStream = File.Create(tempPath))
		{
			stream.CopyTo(fileStream);
		}

		File.Move(tempPath, destinationPath, true);
	}
}