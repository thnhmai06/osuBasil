namespace Basil.Application.Storage.Contracts.Common;

/// <summary>The exception thrown when a record cannot be created or changed because another record already has its unique value.</summary>
/// <param name="message">What already exists.</param>
public sealed class AlreadyExistsException(string message) : Exception(message);
