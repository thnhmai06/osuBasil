namespace Basil.Domain;

/// <summary>Base type of the errors that break a rule of the domain, so a caller can catch them apart from other exceptions.</summary>
public abstract class BasilException(string message) : Exception(message);
