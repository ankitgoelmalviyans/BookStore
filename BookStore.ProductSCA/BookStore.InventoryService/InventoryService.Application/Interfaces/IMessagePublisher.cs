using System.Threading.Tasks;

namespace BookStore.InventoryService.Application.Interfaces
{
    /// <summary>
    /// Publishes events to Service Bus. New to InventoryService this phase — until now it only
    /// consumed; the reservation step makes it a producer too (InventoryReserved / …Failed).
    /// </summary>
    public interface IMessagePublisher
    {
        Task PublishAsync<T>(T message, string topic, string? correlationId = null, string? traceParent = null) where T : class;
    }
}
