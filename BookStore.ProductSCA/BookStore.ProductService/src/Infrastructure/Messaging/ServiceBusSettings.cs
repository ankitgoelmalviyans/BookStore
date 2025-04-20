namespace BookStore.ProductService.Infrastructure.Messaging
{
    public class ServiceBusSettings
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string TopicName { get; set; } = string.Empty;
    }
}
