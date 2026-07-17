export type OrderStatus = 'Pending' | 'Confirmed' | 'Cancelled';

export interface OrderSummary {
  id: string;
  status: OrderStatus;
  total: number;
  itemCount: number;
  createdAt: string;
}

export interface OrderItem {
  productId: string;
  quantity: number;
  unitPrice: number;
}

export interface OrderDetail {
  id: string;
  customerId: string;
  status: OrderStatus;
  total: number;
  createdAt: string;
  items: OrderItem[];
}

export interface PlaceOrderItem {
  productId: string;
  quantity: number;
  unitPrice: number;
}

// CustomerId is intentionally omitted here — the API overwrites it server-side from the JWT,
// so the UI never sends one (see OrderService's OrderController).
export interface PlaceOrderCommand {
  items: PlaceOrderItem[];
}
