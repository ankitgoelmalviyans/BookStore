namespace BookStore.HelpAssistantService.Infrastructure.Agents;

/// <summary>
/// Entra ID app registration (client credentials flow) + Foundry Agent Application coordinates.
/// Populated from config — see appsettings.json's "Foundry" section. ClientSecret always comes
/// from the K8s Secret / GitHub secret, never committed (same posture as every other credential
/// in this platform).
/// </summary>
public class FoundryAgentOptions
{
    public const string SectionName = "Foundry";

    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>The Foundry account (Cognitive Services AIServices resource) name, e.g. "foundrybookstore".</summary>
    public string AccountName { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>The published Agent Application name (not the raw agent name) — see infra/setup-foundry-agent.sh.</summary>
    public string ApplicationName { get; set; } = string.Empty;

    public string ApiVersion { get; set; } = "2025-11-15-preview";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret) &&
        !string.IsNullOrWhiteSpace(AccountName) &&
        !string.IsNullOrWhiteSpace(ProjectName) &&
        !string.IsNullOrWhiteSpace(ApplicationName);
}
