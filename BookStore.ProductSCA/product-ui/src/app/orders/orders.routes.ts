import { Routes } from '@angular/router';

// 'cart' must come before ':id' — otherwise the ':id' route would swallow /orders/cart, matching
// it as an order id instead of the cart page.
export const ORDERS_ROUTES: Routes = [
  { path: '', loadComponent: () => import('./order-list/order-list.component').then(m => m.OrderListComponent) },
  { path: 'cart', loadComponent: () => import('./cart/cart.component').then(m => m.CartComponent) },
  { path: ':id', loadComponent: () => import('./order-detail/order-detail.component').then(m => m.OrderDetailComponent) }
];
