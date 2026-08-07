using System.Text;
using Basil.Application.Services.Authentication;
using Basil.Web.Auth;
using Basil.Web.OpenApi;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>
///     Registers the REST endpoints for reading and managing the server's admin key.
/// </summary>
/// <remarks>
///     Reading or changing the key itself still requires the admin role, which a request always
///     holds while the server is in bypass mode (no key configured) — an operator can always set a
///     key back without needing one first.
/// </remarks>
internal static class AdminKeyRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>
	///     Registers the `/adminkey` read/write/clear routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapAdminKeyRoutes(this RouteGroupBuilder group)
	{
		group.MapGet("/adminkey", async (AdminKeyService adminKey, CancellationToken cancellationToken) =>
			{
				var lastChanged = await adminKey.GetLastChangedAsync(cancellationToken);
				return Results.Json(new AdminKeyStatusView(lastChanged));
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("getAdminKeyStatus")
			.WithSummary("Get admin key status.")
			.WithDescription("""
			                 Returns when the admin key was last set or cleared, never the key or its hash.

			                 `lastChanged` is `null` on a never-configured install.
			                 """ + AdminKeyNote)
			.WithTags("Admin Key")
			.Produces<AdminKeyStatusView>()
			.WithExample(StatusCodes.Status200OK, new AdminKeyStatusView(DateTimeOffset.UtcNow));

		group.MapPut("/adminkey", HandleSetKey)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("setAdminKey")
			.WithSummary("Set the admin key.")
			.WithDescription("""
			                 Body: `{ key }`. Replaces whatever key is currently set, or sets one if the server is in bypass mode.

			                 Takes effect immediately for every subsequent request, no restart required.
			                 """ + AdminKeyNote)
			.WithTags("Admin Key")
			.Produces<AdminKeyChangedView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new AdminKeyChangedView(true, "Admin key updated."))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Key must be 1 to 72 bytes long."));

		group.MapDelete("/adminkey", async (AdminKeyService adminKey, ILogger<AdminKeyRoutesLog> logger,
				CancellationToken cancellationToken) =>
			{
				await adminKey.ClearAsync(cancellationToken);
				logger.LogInformation("Admin key cleared via admin API — server is now in bypass mode");
				return Results.Json(new AdminKeyChangedView(true,
					"Admin key cleared. The server is now in bypass mode."));
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("deleteAdminKey")
			.WithSummary("Clear the admin key.")
			.WithDescription("""
			                 Puts the server into bypass mode: every admin-gated action and in-game registration
			                 succeeds without a key until a new one is set.
			                 """ + AdminKeyNote)
			.WithTags("Admin Key")
			.Produces<AdminKeyChangedView>()
			.WithExample(StatusCodes.Status200OK,
				new AdminKeyChangedView(true, "Admin key cleared. The server is now in bypass mode."));
	}

	private static async Task<IResult> HandleSetKey(AdminKeyBody body, AdminKeyService adminKey,
		ILogger<AdminKeyRoutesLog> logger, CancellationToken cancellationToken)
	{
		var keyLength = Encoding.UTF8.GetByteCount(body.Key);
		if (keyLength is 0 or > AdminKeyService.MaxKeyLengthBytes)
			return Results.BadRequest(new ErrorResponse(
				$"Key must be 1 to {AdminKeyService.MaxKeyLengthBytes} bytes long."));

		await adminKey.SetKeyAsync(body.Key, cancellationToken);
		logger.LogInformation("Admin key updated via admin API");
		return Results.Json(new AdminKeyChangedView(true, "Admin key updated."));
	}

	/// <summary>Response body for `GET /adminkey`.</summary>
	public sealed record AdminKeyStatusView(DateTimeOffset? LastChanged);

	/// <summary>Request body for `PUT /adminkey`.</summary>
	public sealed record AdminKeyBody(string Key);

	/// <summary>Confirmation body for the admin key write routes.</summary>
	public sealed record AdminKeyChangedView(bool Success, string Message);
}

/// <summary>A dedicated logger category marker for the static <see cref="AdminKeyRoutes" /> class.</summary>
internal sealed class AdminKeyRoutesLog;