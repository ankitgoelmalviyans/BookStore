using System.ComponentModel.DataAnnotations;

namespace BookStore.InventoryService.API.Model
{
    public class StockAdjustmentRequest
    {
        [Range(1, int.MaxValue, ErrorMessage = "Quantity must be a positive integer.")]
        public int Quantity { get; set; }
    }
}
