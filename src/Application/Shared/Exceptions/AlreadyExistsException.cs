namespace Basil.Application.Shared.Exceptions;

public sealed class AlreadyExistsException(string message) : Exception(message);