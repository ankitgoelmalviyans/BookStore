import { AfterViewInit, Component, ElementRef, EventEmitter, Input, OnDestroy, Output, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { loadStripe, Stripe, StripeCardElement } from '@stripe/stripe-js';
import { environment } from '../../../environments/environment';
import { OrderService } from '../../core/services/order.service';
import { PaymentService } from '../../core/services/payment.service';

// Test-mode Stripe Elements card form — collects a real Stripe PaymentMethod client-side (no raw
// card data ever reaches our backend), then hands just that id to PaymentService's /confirm
// endpoint. Cancel goes through OrderService's /cancel instead, which is what actually triggers the
// saga's compensation (releasing the reserved stock).
@Component({
  selector: 'app-payment-form',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './payment-form.component.html',
  styleUrls: ['./payment-form.component.scss']
})
export class PaymentFormComponent implements AfterViewInit, OnDestroy {
  @Input() orderId!: string;
  @Output() paid = new EventEmitter<void>();
  @Output() cancelled = new EventEmitter<void>();

  @ViewChild('cardElement') cardElementRef!: ElementRef<HTMLDivElement>;

  processing = false;
  cardReady = false;
  errorMessage = '';

  private stripe: Stripe | null = null;
  private card: StripeCardElement | null = null;

  constructor(private orderService: OrderService, private paymentService: PaymentService) {}

  async ngAfterViewInit(): Promise<void> {
    this.stripe = await loadStripe(environment.stripePublishableKey);
    if (!this.stripe) {
      this.errorMessage = 'Could not load the payment form. Please refresh and try again.';
      return;
    }
    const elements = this.stripe.elements();
    this.card = elements.create('card');
    this.card.mount(this.cardElementRef.nativeElement);
    this.cardReady = true;
  }

  ngOnDestroy(): void {
    this.card?.unmount();
  }

  async pay(): Promise<void> {
    if (!this.stripe || !this.card || this.processing) {
      return;
    }
    this.processing = true;
    this.errorMessage = '';

    const { paymentMethod, error } = await this.stripe.createPaymentMethod({ type: 'card', card: this.card });
    if (error || !paymentMethod) {
      this.errorMessage = error?.message ?? 'Could not process card details.';
      this.processing = false;
      return;
    }

    this.paymentService.confirm(this.orderId, paymentMethod.id).subscribe({
      next: () => {
        this.processing = false;
        this.paid.emit();
      },
      error: (err) => {
        this.processing = false;
        this.errorMessage = err?.error?.error ?? 'Payment failed. Please try again.';
      }
    });
  }

  cancel(): void {
    if (this.processing) {
      return;
    }
    this.processing = true;
    this.errorMessage = '';

    this.orderService.cancel(this.orderId).subscribe({
      next: () => {
        this.processing = false;
        this.cancelled.emit();
      },
      error: () => {
        this.processing = false;
        this.errorMessage = 'Could not cancel the order. Please try again.';
      }
    });
  }
}
