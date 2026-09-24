using Xunit;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// Every browser test joins this collection (and is tagged <c>[Trait("Category", "E2E")]</c>), so the app is
/// published and served once per run.
/// </summary>
[CollectionDefinition(Name)]
public sealed class E2ECollection : ICollectionFixture<PublishedApp>
{
    public const string Name = "E2E";
}
