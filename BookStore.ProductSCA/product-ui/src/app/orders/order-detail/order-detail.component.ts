import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { EMPTY, Subscription, catchError, interval, startWith, switchMap, takeWhile } from 'rxjs';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTableModule } from '@angular/material/table';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { OrderService } from '../../core/services/order.service';
import { PaymentService } from '../../core/services/payment.service';
import { OrderDetail } from '../../core/models/order.model';
import { Payment } from '../../core/models/payment.model';
import { PaymentStatusComponent } from '../payment-status/payment-status.component';

const POLL_INTERVAL_MS = 4000;

@Component({
  selector: 'app-order-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, MatIconModule, MatButtonModule, MatTableModule, MatProgressSpinnerModule, PaymentStatusComponent],
  templateUrl: './order-detail.component.html',
  styleUrls: ['./order-detail.component.scss']
})
export class OrderDetailComponent implements OnInit, OnDestroy {
  order: OrderDetail | null = null;
  payment: Payment | null = null;
  loadingOrder = true;
  loadingPayment = true;
  loadError = false;

  private orderId!: string;
  private pollSub?: Subscription;

  constructor(
    private route: ActivatedRoute,
    private orderService: OrderService,
    private paymentService: PaymentService
  ) {}

  ngOnInit(): void {
    this.orderId = this.route.snapshot.paramMap.get('id')!;

    // The saga is asynchronous — inventory reservation and payment happen in the background after
    // placement, so a freshly placed order legitimately starts Pending. Poll until it reaches a
    // terminal state (Confirmed/Cancelled) instead of making the user manually refresh to see
    // whether their order went through.
    //
    // catchError sits *inside* the switchMap's inner observable so a single failed poll (network
    // blip, transient 5xx) skips that tick via EMPTY instead of propagating up and killing the
    // outer interval — without this, one bad poll would silently stop all future polling for the
    // rest of the page's lifetime.
    this.pollSub = interval(POLL_INTERVAL_MS)
      .pipe(
        startWith(0),
        switchMap(() =>
          this.orderService.getById(this.orderId).pipe(
            catchError(() => {
              this.loadingOrder = false;
              this.loadError = true;
              return EMPTY;
            })
          )
        ),
        takeWhile(order => order.status === 'Pending', true)
      )
      .subscribe(order => {
        this.loadError = false;
        this.order = order;
        this.loadingOrder = false;
        this.refreshPayment();
      });
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
  }

  private refreshPayment(): void {
    this.paymentService.getByOrderId(this.orderId).subscribe({
      next: payment => {
        this.payment = payment;
        this.loadingPayment = false;
      },
      error: () => {
        // ErrorInterceptor already surfaced a toast for this (a non-404 failure) — just stop the
        // spinner so the payment section doesn't spin forever.
        this.loadingPayment = false;
      }
    });
  }

  statusClass(status: string): string {
    switch (status) {
      case 'Confirmed':
        return 'chip-success';
      case 'Cancelled':
        return 'chip-danger';
      default:
        return 'chip-pending';
    }
  }

  lineTotal(quantity: number, unitPrice: number): number {
    return quantity * unitPrice;
  }
}
