import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { filter, map } from 'rxjs/operators';
import { NavigationEnd, Router } from '@angular/router';
import { CartItem } from './models/cart-item.model';
import { Product } from './models/product.model';
import { AuthService } from './auth.service';

const STORAGE_PREFIX = 'cart_items_';

function isValidCartItem(value: unknown): value is CartItem {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const item = value as CartItem;
  return (
    typeof item.productId === 'string' &&
    typeof item.name === 'string' &&
    typeof item.unitPrice === 'number' &&
    Number.isInteger(item.quantity) &&
    item.quantity > 0
  );
}

// Client-side only — the backend has no cart concept. Items live here until "Place order" turns
// them into one PlaceOrderCommand; persisted to localStorage, keyed per authenticated user so a
// logout followed by a different user logging in on the same browser can't see (or check out)
// the previous user's items.
@Injectable({ providedIn: 'root' })
export class CartService {
  private itemsSubject = new BehaviorSubject<CartItem[]>([]);
  items$ = this.itemsSubject.asObservable();
  itemCount$ = this.items$.pipe(map(items => items.reduce((sum, i) => sum + i.quantity, 0)));
  total$ = this.items$.pipe(map(items => items.reduce((sum, i) => sum + i.quantity * i.unitPrice, 0)));

  private currentKey: string | null = null;

  constructor(private auth: AuthService, router: Router) {
    this.reloadForCurrentUser();
    // Login/logout happen via SPA navigation, not a full reload — same pattern NavToolbarComponent
    // uses to detect identity changes without a dedicated auth-state event bus.
    router.events.pipe(filter(e => e instanceof NavigationEnd)).subscribe(() => this.reloadForCurrentUser());
  }

  private storageKey(): string | null {
    const username = this.auth.getUsername();
    return username ? `${STORAGE_PREFIX}${username}` : null;
  }

  private reloadForCurrentUser(): void {
    const key = this.storageKey();
    if (key === this.currentKey) {
      return;
    }
    this.currentKey = key;
    this.itemsSubject.next(key ? this.loadFromStorage(key) : []);
  }

  private loadFromStorage(key: string): CartItem[] {
    try {
      const raw = localStorage.getItem(key);
      if (!raw) {
        return [];
      }
      const parsed = JSON.parse(raw);
      return Array.isArray(parsed) ? parsed.filter(isValidCartItem) : [];
    } catch {
      return [];
    }
  }

  private persist(items: CartItem[]): void {
    this.itemsSubject.next(items);
    if (!this.currentKey) {
      return;
    }
    try {
      localStorage.setItem(this.currentKey, JSON.stringify(items));
    } catch {
      // Non-fatal — cart just won't survive a reload if storage is unavailable.
    }
  }

  get snapshot(): CartItem[] {
    return this.itemsSubject.value;
  }

  addItem(product: Product, quantity: number = 1): void {
    if (!Number.isInteger(quantity) || quantity <= 0) {
      return;
    }
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
    if (!Number.isInteger(quantity) || quantity <= 0) {
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
