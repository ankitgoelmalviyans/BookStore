namespace BookStore.OrderService.Infrastructure.Persistence;

/// <summary>
/// Fixed pool of demo ProductIds used by <see cref="SeedRunner"/> to generate a realistic order
/// history. The same literal ids/names/prices are duplicated in ProductService's own seed (no shared
/// contracts assembly between services, same convention used everywhere else in this platform) so
/// the two seeds describe the same 15 books. Grouped into three loose "clusters" — books that tend to
/// get bought together — purely so RecommendationService's co-occurrence counting has real signal to
/// learn from instead of 15 uniformly-random, un-correlated products.
/// </summary>
public static class DemoCatalog
{
    // "Clean code" cluster
    public static readonly Guid CleanCode = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid CleanArchitecture = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid Refactoring = Guid.Parse("10000000-0000-0000-0000-000000000012");
    public static readonly Guid WorkingEffectivelyWithLegacyCode = Guid.Parse("10000000-0000-0000-0000-000000000013");

    // "Ops / delivery" cluster
    public static readonly Guid ThePhoenixProject = Guid.Parse("10000000-0000-0000-0000-000000000006");
    public static readonly Guid SiteReliabilityEngineering = Guid.Parse("10000000-0000-0000-0000-000000000007");
    public static readonly Guid BuildingMicroservices = Guid.Parse("10000000-0000-0000-0000-000000000008");
    public static readonly Guid ReleaseIt = Guid.Parse("10000000-0000-0000-0000-000000000009");
    public static readonly Guid ContinuousDelivery = Guid.Parse("10000000-0000-0000-0000-000000000014");
    public static readonly Guid Accelerate = Guid.Parse("10000000-0000-0000-0000-000000000015");

    // "Foundations" cluster
    public static readonly Guid ThePragmaticProgrammer = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid DomainDrivenDesign = Guid.Parse("10000000-0000-0000-0000-000000000004");
    public static readonly Guid DesigningDataIntensiveApplications = Guid.Parse("10000000-0000-0000-0000-000000000005");
    public static readonly Guid EffectiveJava = Guid.Parse("10000000-0000-0000-0000-000000000010");
    public static readonly Guid TheMythicalManMonth = Guid.Parse("10000000-0000-0000-0000-000000000011");

    public static readonly Guid[] CleanCodeCluster =
    {
        CleanCode, CleanArchitecture, Refactoring, WorkingEffectivelyWithLegacyCode
    };

    public static readonly Guid[] OpsClusterProducts =
    {
        ThePhoenixProject, SiteReliabilityEngineering, BuildingMicroservices, ReleaseIt, ContinuousDelivery, Accelerate
    };

    public static readonly Guid[] FoundationsCluster =
    {
        ThePragmaticProgrammer, DomainDrivenDesign, DesigningDataIntensiveApplications, EffectiveJava, TheMythicalManMonth
    };

    public static readonly IReadOnlyDictionary<Guid, (string Name, string Category, decimal Price)> Catalog =
        new Dictionary<Guid, (string, string, decimal)>
        {
            [ThePragmaticProgrammer] = ("The Pragmatic Programmer", "Software Engineering", 44.99m),
            [CleanCode] = ("Clean Code", "Software Engineering", 39.99m),
            [CleanArchitecture] = ("Clean Architecture", "Software Engineering", 42.99m),
            [DomainDrivenDesign] = ("Domain-Driven Design", "Software Engineering", 54.99m),
            [DesigningDataIntensiveApplications] = ("Designing Data-Intensive Applications", "Software Engineering", 49.99m),
            [ThePhoenixProject] = ("The Phoenix Project", "DevOps", 29.99m),
            [SiteReliabilityEngineering] = ("Site Reliability Engineering", "DevOps", 45.99m),
            [BuildingMicroservices] = ("Building Microservices", "Software Engineering", 41.99m),
            [ReleaseIt] = ("Release It!", "DevOps", 38.99m),
            [EffectiveJava] = ("Effective Java", "Programming Languages", 47.99m),
            [TheMythicalManMonth] = ("The Mythical Man-Month", "Software Engineering", 24.99m),
            [Refactoring] = ("Refactoring", "Software Engineering", 46.99m),
            [WorkingEffectivelyWithLegacyCode] = ("Working Effectively with Legacy Code", "Software Engineering", 43.99m),
            [ContinuousDelivery] = ("Continuous Delivery", "DevOps", 44.99m),
            [Accelerate] = ("Accelerate", "DevOps", 27.99m)
        };

    public static decimal PriceOf(Guid productId) =>
        Catalog.TryGetValue(productId, out var entry) ? entry.Price : 19.99m;
}
