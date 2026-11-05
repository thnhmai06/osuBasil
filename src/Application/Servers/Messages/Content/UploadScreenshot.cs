using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages;

/// <summary>Asks Servers to store a screenshot; the image is read to its end and not disposed.</summary>
/// <param name="Actor">The connection acting.</param>
public sealed record UploadScreenshot(Connection Actor, Stream Image)
	: IRequest<(ContentReference? Screenshot, Result<ServerOutcome> Result)>;