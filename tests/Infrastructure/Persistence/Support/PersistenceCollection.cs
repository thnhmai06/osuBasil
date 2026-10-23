namespace Basil.Infrastructure.Persistence.Tests.Support;

/// <summary>
///     Must contain every test class that uses the PostgreSQL container. The container belongs to the collection, so
///     the classes that share it run one after another against one database server.
/// </summary>
[CollectionDefinition("Persistence")]
public sealed class PersistenceCollection : ICollectionFixture<PersistenceFixture>;
