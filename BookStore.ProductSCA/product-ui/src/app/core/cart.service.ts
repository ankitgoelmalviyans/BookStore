import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { map } from 'rxjs/operators';
import { CartItem } from './models/cart-item.model';
import { Product } from './models/product.model';

const STORAGE_KEY = 'cart_items';

// Client-side only — the backend has no cart concept. Items live here until "Place order" turns
// them into one PlaceOrderCommand; persisted to localStorage so the cart survives a page reload.
@Injectable({ providedIn: 'root' })
export class CartService {
  private itemsSubject = new BehaviorSubject<CartItem[]>(this.loadFromStorage());
  items$ = this.itemsSubject.asObservable();
  itemCount$ = this.items$.pipe(map(items => items.reduce((sum, i) => sum + i.quantity, 0)));
  total$ = this.items$.pipe(map(items => items.reduce((sum, i) => sum + i.quantity * i.unitPrice, 0)));

  private loadFromStorage(): CartItem[] {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? JSON.parse(raw) : [];
    } catch {
      return [];
    }
  }

  private persist(items: CartItem[]): void {
    this.itemsSubject.next(items);
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(items));
    } catch {
      // Non-fatal — cart just won't survive a reload if storage is unavailable.
    }
  }

  get snapshot(): CartItem[] {
    return this.itemsSubject.value;
  }

  addItem(product: Product, quantity: number = 1): void {
    const items = [...this.snapshot];
    const existing = items.find(i => i.productId === product.id);
    if (existing) {
      existing.quantity += quantity;
    } else {
      items.push({ productId: product.id, name: product.name, unitPrice: product.price, quantity });
    }
    this.persist(items);
  }

  updateQuantity(productId: string, quantity: number): void {
    if (quantity <= 0) {
      this.removeItem(productId);
      return;
    }
    const items = this.snapshot.map(i => (i.productId === productId ? { ...i, quantity } : i));
    this.persist(items);
  }

  removeItem(productId: string): void {
    this.persist(this.snapshot.filter(i => i.productId !== productId));
  }

  clear(): void {
    this.persist([]);
  }
}
