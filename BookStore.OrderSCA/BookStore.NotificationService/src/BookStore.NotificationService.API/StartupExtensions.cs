using BookStore.NotificationService.Application;
using BookStore.NotificationService.Core.Abstractions;
using BookStore.NotificationService.Core.Messaging;
using BookStore.NotificationService.Core.Notifications;
using BookStore.NotificationService.Infrastructure.Messaging;
using BookStore.NotificationService.Infrastructure.Notifications;

namespace BookStore.NotificationService.Extensions
{
    public static class StartupExtensions
    {
        public static IServiceCollection AddNotificationServices(this IServiceCollection services, IConfiguration config)
        {
            services.AddHealthChecks();

            // Stateless: the notifier and handler hold no data, so they're singletons. The subscriber
            // is gated behind Notifications:Enabled (default off) — it depends on the
            // order-events/payment-events topology, which isn't provisioned yet, so deploying this code
            // changes nothing at runtime until an operator enables it.
            if (config.GetValue<bool>("Notifications:Enabled"))
            {
                services.AddSingleton<INotifier, LogNotifier>();
                services.AddSingleton<INotificationHandler, NotificationHandler>();
                services.AddSingleton<IEventSubscriber, AzureServiceBusSubscriber>();
            }

            return services;
        }
    }
}
