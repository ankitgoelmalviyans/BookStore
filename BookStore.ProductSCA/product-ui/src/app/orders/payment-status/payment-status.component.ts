import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Payment } from '../../core/models/payment.model';

@Component({
  selector: 'app-payment-status',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './payment-status.component.html',
  styleUrls: ['./payment-status.component.scss']
})
export class PaymentStatusComponent {
  @Input() payment: Payment | null = null;
  @Input() loading = false;
  @Input() orderStatus: string | null = null;

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
