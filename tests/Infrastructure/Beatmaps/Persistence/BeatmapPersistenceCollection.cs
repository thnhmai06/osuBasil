using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Beatmaps.Tests.Persistence;

/// <summary>Serializes the Beatmaps database tests against their shared PostgreSQL server fixture.</summary>
[CollectionDefinition("Persistence")]
public sealed class BeatmapPersistenceCollection : ICollectionFixture<PersistenceFixture>;