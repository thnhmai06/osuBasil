using Basil.Testing.Persistence;

namespace Basil.Composition.Tests;

/// <summary>Serializes the database tests of this project against their shared PostgreSQL server fixture.</summary>
[CollectionDefinition("Persistence")]
public sealed class PersistenceCollection : ICollectionFixture<PersistenceFixture>;