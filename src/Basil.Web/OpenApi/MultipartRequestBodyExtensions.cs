using Microsoft.OpenApi;

namespace Basil.Web.OpenApi;

/// <summary>Describes one field of a declared `multipart/form-data` request body.</summary>
/// <param name="Name">The multipart field name.</param>
/// <param name="Required">Whether the field must be present.</param>
/// <param name="Kind">What kind of value the field carries.</param>
internal readonly record struct MultipartField(string Name, bool Required, MultipartFieldKind Kind)
{
	/// <summary>Declares a required binary file field.</summary>
	public static MultipartField File(string name)
	{
		return new MultipartField(name, true, MultipartFieldKind.File);
	}

	/// <summary>Declares a field that accepts either a binary file or a plain-text value.</summary>
	public static MultipartField FileOrText(string name, bool required)
	{
		return new MultipartField(name, required, MultipartFieldKind.FileOrText);
	}

	/// <summary>Declares a plain-text field.</summary>
	public static MultipartField Text(string name, bool required)
	{
		return new MultipartField(name, required, MultipartFieldKind.Text);
	}
}

/// <summary>What kind of value a declared <see cref="MultipartField" /> carries.</summary>
internal enum MultipartFieldKind
{
	/// <summary>A plain-text form value.</summary>
	Text,

	/// <summary>A binary file upload.</summary>
	File,

	/// <summary>Either a binary file upload or a plain-text value, under the same field name.</summary>
	FileOrText
}

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
		return builder.WithMultipartBody(MultipartField.File(fieldName));
	}

	/// <summary>
	///     Declares the route's request body as a required <c>multipart/form-data</c> upload carrying the
	///     given fields.
	/// </summary>
	/// <param name="builder">The route to declare the multipart request body on.</param>
	/// <param name="fields">Every field the multipart body carries.</param>
	/// <returns>The <paramref name="builder" /> for continued chaining.</returns>
	public static RouteHandlerBuilder WithMultipartBody(this RouteHandlerBuilder builder,
		params MultipartField[] fields)
	{
		return builder.AddOpenApiOperationTransformer((operation, _, _) =>
		{
			var required = new HashSet<string>();
			var properties = new Dictionary<string, IOpenApiSchema>();

			foreach (var field in fields)
			{
				if (field.Required) required.Add(field.Name);

				properties[field.Name] = field.Kind switch
				{
					MultipartFieldKind.File => new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" },
					MultipartFieldKind.FileOrText => new OpenApiSchema
					{
						AnyOf =
						[
							new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" },
							new OpenApiSchema { Type = JsonSchemaType.String }
						]
					},
					_ => new OpenApiSchema { Type = JsonSchemaType.String }
				};
			}

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
							Required = required,
							Properties = properties
						}
					}
				}
			};

			return Task.CompletedTask;
		});
	}
}