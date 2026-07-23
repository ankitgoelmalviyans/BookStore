namespace BookStore.ProductService.Infrastructure.Persistence;

/// <summary>
/// Fixed pool of demo ProductIds/names/prices used by <see cref="SeedRunner"/>. Same literal ids as
/// OrderService's own <c>DemoCatalog</c> (duplicated — no shared contracts assembly between services,
/// same convention used everywhere else in this platform) so OrderService's seeded order history
/// references real Product documents, and AiService's ingestion has real book descriptions to embed.
/// </summary>
public static class DemoCatalog
{
    public static readonly Guid ThePragmaticProgrammer = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid CleanCode = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid CleanArchitecture = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid DomainDrivenDesign = Guid.Parse("10000000-0000-0000-0000-000000000004");
    public static readonly Guid DesigningDataIntensiveApplications = Guid.Parse("10000000-0000-0000-0000-000000000005");
    public static readonly Guid ThePhoenixProject = Guid.Parse("10000000-0000-0000-0000-000000000006");
    public static readonly Guid SiteReliabilityEngineering = Guid.Parse("10000000-0000-0000-0000-000000000007");
    public static readonly Guid BuildingMicroservices = Guid.Parse("10000000-0000-0000-0000-000000000008");
    public static readonly Guid ReleaseIt = Guid.Parse("10000000-0000-0000-0000-000000000009");
    public static readonly Guid EffectiveJava = Guid.Parse("10000000-0000-0000-0000-000000000010");
    public static readonly Guid TheMythicalManMonth = Guid.Parse("10000000-0000-0000-0000-000000000011");
    public static readonly Guid Refactoring = Guid.Parse("10000000-0000-0000-0000-000000000012");
    public static readonly Guid WorkingEffectivelyWithLegacyCode = Guid.Parse("10000000-0000-0000-0000-000000000013");
    public static readonly Guid ContinuousDelivery = Guid.Parse("10000000-0000-0000-0000-000000000014");
    public static readonly Guid Accelerate = Guid.Parse("10000000-0000-0000-0000-000000000015");

    public static readonly IReadOnlyList<(Guid Id, string Name, string Category, decimal Price, string Description)> Books = new List<(Guid, string, string, decimal, string)>
    {
        (ThePragmaticProgrammer, "The Pragmatic Programmer", "Software Engineering", 44.99m,
            "A classic guide to becoming a more effective and adaptable software developer, covering practical techniques from cutting-edge practices."),
        (CleanCode, "Clean Code", "Software Engineering", 39.99m,
            "A handbook of agile software craftsmanship, teaching principles and practices for writing readable, maintainable code."),
        (CleanArchitecture, "Clean Architecture", "Software Engineering", 42.99m,
            "A guide to structuring software systems so business rules stay independent of frameworks, databases, and UI."),
        (DomainDrivenDesign, "Domain-Driven Design", "Software Engineering", 54.99m,
            "Tackling complexity in the heart of software by modeling software to match a domain according to input from domain experts."),
        (DesigningDataIntensiveApplications, "Designing Data-Intensive Applications", "Software Engineering", 49.99m,
            "The big ideas behind reliable, scalable, and maintainable systems, covering databases, queues, and distributed data."),
        (ThePhoenixProject, "The Phoenix Project", "DevOps", 29.99m,
            "A novel about IT, DevOps, and helping a business win, following a team's struggle to deliver a critical project."),
        (SiteReliabilityEngineering, "Site Reliability Engineering", "DevOps", 45.99m,
            "How Google runs production systems, covering the engineering practices behind large-scale reliable services."),
        (BuildingMicroservices, "Building Microservices", "Software Engineering", 41.99m,
            "Designing fine-grained systems, covering the principles and practices of building independently deployable services."),
        (ReleaseIt, "Release It!", "DevOps", 38.99m,
            "Design and deployment patterns for production-ready software, focused on stability and resilience under real-world conditions."),
        (EffectiveJava, "Effective Java", "Programming Languages", 47.99m,
            "Best practices for the Java platform, distilled into concrete, actionable items for writing clearer, more correct code."),
        (TheMythicalManMonth, "The Mythical Man-Month", "Software Engineering", 24.99m,
            "Essays on software engineering and project management, exploring why adding manpower to a late project makes it later."),
        (Refactoring, "Refactoring", "Software Engineering", 46.99m,
            "Improving the design of existing code through a catalog of small, behavior-preserving transformations."),
        (WorkingEffectivelyWithLegacyCode, "Working Effectively with Legacy Code", "Software Engineering", 43.99m,
            "Techniques for understanding, testing, and safely changing code that lacks adequate tests."),
        (ContinuousDelivery, "Continuous Delivery", "DevOps", 44.99m,
            "Reliable software releases through build, test, and deployment automation, covering the deployment pipeline end to end."),
        (Accelerate, "Accelerate", "DevOps", 27.99m,
            "The science of building and scaling high-performing technology organizations, backed by years of research.")
    };
}
