export type PaymentStatus = 'Pending' | 'Captured' | 'Failed';

export interface Payment {
  id: string;
  orderId: string;
  status: PaymentStatus;
  amount: number;
  currency: string;
  providerPaymentId: string | null;
  failureReason: string | null;
  createdAt: string;
}
