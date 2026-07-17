import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { OrderService } from '../../core/order.service';
import { OrderSummary } from '../../core/models/order.model';

@Component({
  selector: 'app-order-list',
  templateUrl: './order-list.component.html',
  styleUrls: ['./order-list.component.scss']
})
export class OrderListComponent implements OnInit {
  orders: OrderSummary[] = [];
  loading = true;
  displayedColumns = ['id', 'status', 'itemCount', 'total', 'createdAt', 'actions'];

  constructor(private orderService: OrderService, private router: Router) {}

  ngOnInit(): void {
    this.orderService.getAll().subscribe(orders => {
      this.orders = orders;
      this.loading = false;
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
