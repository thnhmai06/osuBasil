namespace Basil.Domain.Users;

/// <summary>A rule of the users module was broken: accounts, profiles and silences.</summary>
public abstract class UsersException(string message) : BasilException(message);

/// <summary>A rule of an account was broken.</summary>
public abstract class AccountException(string message) : UsersException(message);

/// <summary>A user name does not follow the rules for names.</summary>
public sealed class InvalidUserNameException() : AccountException("The user name is not valid.");

/// <summary>A rule of a profile was broken.</summary>
public abstract class ProfileException(string message) : UsersException(message);

/// <summary>A country is not a defined country.</summary>
public sealed class InvalidCountryException() : ProfileException("The country is not defined.");

/// <summary>A rule of a silence was broken.</summary>
public abstract class SilenceException(string message) : UsersException(message);

/// <summary>A silence would end before it starts.</summary>
public sealed class InvalidSilenceException() : SilenceException("A silence must end in the future.");
