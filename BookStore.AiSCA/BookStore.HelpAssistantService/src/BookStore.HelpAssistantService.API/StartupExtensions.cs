using BookStore.HelpAssistantService.API.Middleware;
using BookStore.HelpAssistantService.Core.Abstractions;
using BookStore.HelpAssistantService.Infrastructure.Agents;

namespace BookStore.HelpAssistantService.Extensions
{
    public static class StartupExtensions
    {
        public static IServiceCollection AddHelpAssistantServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddHealthChecks();
            services.AddSingleton<ExceptionMiddleware>();

            services.Configure<FoundryAgentOptions>(config.GetSection(FoundryAgentOptions.SectionName));
            services.AddSingleton<IHelpAssistantService, Application.Services.HelpAssistantChatService>();

            // Real Foundry agent only once the one-time setup (infra/setup-foundry-agent.sh) has
            // been run and its app-registration credentials are in the K8s secret — otherwise the
            // deterministic fake, same "builds/demos with no credentials" posture as
            // PaymentService's FakePaymentGateway and AiService's FakeAnswerGenerator.
            var foundryOptions = config.GetSection(FoundryAgentOptions.SectionName).Get<FoundryAgentOptions>();
            if (foundryOptions?.IsConfigured == true)
            {
                services.AddHttpClient<IHelpAssistantAgentClient, FoundryAgentClient>();
            }
            else
            {
                services.AddSingleton<IHelpAssistantAgentClient, FakeFoundryAgentClient>();
            }

            return services;
        }
    }
}
