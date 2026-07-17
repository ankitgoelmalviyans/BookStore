import { Component, Input } from '@angular/core';
import { Payment } from '../../core/models/payment.model';

@Component({
  selector: 'app-payment-status',
  templateUrl: './payment-status.component.html',
  styleUrls: ['./payment-status.component.scss']
})
export class PaymentStatusComponent {
  @Input() payment: Payment | null = null;
  @Input() loading = false;

  get chipClass(): string {
    switch (this.payment?.status) {
      case 'Captured':
        return 'chip-success';
      case 'Failed':
        return 'chip-danger';
      default:
        return 'chip-pending';
    }
  }
}
