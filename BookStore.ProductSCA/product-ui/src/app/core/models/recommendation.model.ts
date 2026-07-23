// Mirrors RecommendationService's CoPurchasePartner — classic ML co-purchase counts, not an
// LLM/AI call. Only productId + count come back; the UI joins against already-loaded Product
// data to get a name/price (see product-list.component.ts) rather than RecommendationService
// owning its own product-data cache.
export interface CoPurchasePartner {
  productId: string;
  count: number;
}
