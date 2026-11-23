using System.Net;
using Basil.Host.Shared.Documentation;
using Markdig;

namespace Basil.Host.Rest.Routes;

internal static class DocsRoutes
{
	private static readonly MarkdownPipeline Pipeline =
		new MarkdownPipelineBuilder().UsePipeTables().DisableHtml().Build();

	public static void MapDocsSite(this IEndpointRouteBuilder app)
	{
		app.MapGet("/", () => Results.Redirect("/docs/")).ExcludeFromDescription();
		app.MapGet("/docs", (HttpContext context) => context.Request.Path.Value!.EndsWith('/')
				? Html("Basil Documentation", "<h1>Basil Documentation</h1><ul>" +
				                              "<li><a href=\"api/\">HTTP API</a></li>" +
				                              "<li><a href=\"irc/\">IRC</a></li>" +
				                              "<li><a href=\"bot/\">BasilBot</a></li></ul>")
				: Results.Redirect("/docs/"))
			.ExcludeFromDescription();
		app.MapGet("/docs/{slug}", (string slug, IEnumerable<IDocumentPage> pages) =>
				pages.FirstOrDefault(page => page.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase)) is { } page
					? Html(page.Title, Markdown.ToHtml(page.Markdown(), Pipeline))
					: Results.NotFound())
			.ExcludeFromDescription();
	}

	private static IResult Html(string title, string body)
	{
		return Results.Content("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
		                       "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
		                       $"<title>{WebUtility.HtmlEncode(title)}</title>" +
		                       "<style>body{font:16px/1.5 system-ui,sans-serif;max-width:60rem;margin:2rem auto;padding:0 1rem}" +
		                       "table{border-collapse:collapse}td,th{border:1px solid #8884;padding:.25rem .6rem;text-align:left}" +
		                       "code{background:#8882;padding:0 .2rem;border-radius:3px}</style></head>" +
		                       $"<body>{body}</body></html>", "text/html; charset=utf-8");
	}
}