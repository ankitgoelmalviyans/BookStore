import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTableModule } from '@angular/material/table';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { OrderService } from '../../core/services/order.service';
import { OrderSummary } from '../../core/models/order.model';

@Component({
  selector: 'app-order-list',
  standalone: true,
  imports: [CommonModule, RouterLink, MatCardModule, MatIconModule, MatButtonModule, MatTableModule, MatProgressSpinnerModule],
  templateUrl: './order-list.component.html',
  styleUrls: ['./order-list.component.scss']
})
export class OrderListComponent implements OnInit {
  orders: OrderSummary[] = [];
  loading = true;
  loadError = false;
  displayedColumns = ['id', 'status', 'itemCount', 'total', 'createdAt', 'actions'];

  constructor(private orderService: OrderService, private router: Router) {}

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.loadError = false;
    this.orderService.getAll().subscribe({
      next: orders => {
        this.orders = orders;
        this.loading = false;
      },
      error: () => {
        // ErrorInterceptor already surfaced a toast — just stop the spinner and expose a retry.
        this.loading = false;
        this.loadError = true;
      }
    });
  }

  view(id: string): void {
    this.router.navigate(['/orders', id]);
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

  shortId(id: string): string {
    return id.slice(0, 8);
  }
}
