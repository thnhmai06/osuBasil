using Microsoft.OpenApi;

namespace Basil.Web.OpenApi;

/// <summary>
///     Declares a `multipart/form-data` request body for routes that read the upload by hand via
///     <c>HttpContext.Request.ReadFormAsync</c>.
/// </summary>
/// <remarks>
///     <para>
///         There is no bound <c>IFormFile</c> parameter for the default OpenAPI generator to pick up,
///         so this declares the shape explicitly rather than leaving it as prose in `.WithDescription`.
///     </para>
///     <para>
///         Scalar and generated client SDKs use the declared request body to render an upload form for
///         the endpoint.
///     </para>
/// </remarks>
internal static class MultipartRequestBodyExtensions
{
	/// <summary>
	///     Declares the route's request body as a required <c>multipart/form-data</c> upload carrying a
	///     single binary file field.
	/// </summary>
	/// <param name="builder">The route to declare the multipart request body on.</param>
	/// <param name="fieldName">The name of the multipart file field. The default is <c>"file"</c>.</param>
	/// <returns>The <paramref name="builder" /> for continued chaining.</returns>
	public static RouteHandlerBuilder WithMultipartFileUpload(this RouteHandlerBuilder builder,
		string fieldName = "file")
	{
		return builder.AddOpenApiOperationTransformer((operation, _, _) =>
		{
			operation.RequestBody = new OpenApiRequestBody
			{
				Required = true,
				Content = new Dictionary<string, OpenApiMediaType>
				{
					["multipart/form-data"] = new()
					{
						Schema = new OpenApiSchema
						{
							Type = JsonSchemaType.Object,
							Required = new HashSet<string> { fieldName },
							Properties = new Dictionary<string, IOpenApiSchema>
							{
								[fieldName] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" }
							}
						}
					}
				}
			};

			return Task.CompletedTask;
		});
	}
}