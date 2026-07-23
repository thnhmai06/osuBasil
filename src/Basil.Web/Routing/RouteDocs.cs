namespace Basil.Web.Routing;

/// <summary>Shared OpenAPI description fragments reused across the api. host's route files.</summary>
internal static class RouteDocs
{
    public const string AdminKeyNote = " Requires a valid `X-Admin-Key` request header matching the " +
        "server's configured `Server:AdminKey`.";
}
