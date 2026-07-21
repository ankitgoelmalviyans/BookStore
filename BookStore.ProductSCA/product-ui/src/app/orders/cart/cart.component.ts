import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Observable } from 'rxjs';
import { CartService } from '../../core/services/cart.service';
import { OrderService } from '../../core/services/order.service';
import { CartItem } from '../../core/models/cart-item.model';

@Component({
  selector: 'app-cart',
  standalone: true,
  imports: [CommonModule, RouterLink, MatCardModule, MatIconModule, MatTableModule, MatButtonModule, MatProgressSpinnerModule, MatSnackBarModule],
  templateUrl: './cart.component.html',
  styleUrls: ['./cart.component.scss']
})
export class CartComponent {
  items$: Observable<CartItem[]> = this.cart.items$;
  total$ = this.cart.total$;
  placing = false;

  constructor(
    private cart: CartService,
    private orderService: OrderService,
    private router: Router,
    private snackBar: MatSnackBar
  ) {}

  updateQuantity(productId: string, quantity: number): void {
    this.cart.updateQuantity(productId, quantity);
  }

  remove(productId: string): void {
    this.cart.removeItem(productId);
  }

  lineTotal(item: CartItem): number {
    return item.quantity * item.unitPrice;
  }

  placeOrder(): void {
    const items = this.cart.snapshot;
    if (items.length === 0 || this.placing) {
      return;
    }

    this.placing = true;
    this.orderService
      .placeOrder({
        items: items.map(i => ({ productId: i.productId, quantity: i.quantity, unitPrice: i.unitPrice }))
      })
      .subscribe({
        next: result => {
          this.cart.clear();
          this.snackBar.open('Order placed!', 'Dismiss', { duration: 4000 });
          this.router.navigate(['/orders', result.id]);
        },
        error: () => {
          // ErrorInterceptor already surfaced a toast — just unstick the button so the user can retry.
          this.placing = false;
        }
      });
  }
}
