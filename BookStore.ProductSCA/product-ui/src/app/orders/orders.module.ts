import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Routes } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { OrderListComponent } from './order-list/order-list.component';
import { OrderDetailComponent } from './order-detail/order-detail.component';
import { CartComponent } from './cart/cart.component';
import { PaymentStatusComponent } from './payment-status/payment-status.component';

// 'cart' must come before ':id' — otherwise the ':id' route would swallow /orders/cart, matching
// it as an order id instead of the cart page.
const routes: Routes = [
  { path: '', component: OrderListComponent },
  { path: 'cart', component: CartComponent },
  { path: ':id', component: OrderDetailComponent }
];

@NgModule({
  declarations: [OrderListComponent, OrderDetailComponent, CartComponent, PaymentStatusComponent],
  imports: [
    CommonModule,
    RouterModule.forChild(routes),
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule
  ]
})
export class OrdersModule {}
