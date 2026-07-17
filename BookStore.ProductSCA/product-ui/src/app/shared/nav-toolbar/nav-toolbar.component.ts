import { Component, OnDestroy, OnInit } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { Subscription, filter } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { CartService } from '../../core/cart.service';

@Component({
  selector: 'app-nav-toolbar',
  templateUrl: './nav-toolbar.component.html',
  styleUrls: ['./nav-toolbar.component.scss']
})
export class NavToolbarComponent implements OnInit, OnDestroy {
  loggedIn = false;
  username: string | null = null;
  itemCount$ = this.cart.itemCount$;

  private routerSub?: Subscription;

  constructor(private auth: AuthService, private cart: CartService, private router: Router) {}

  ngOnInit(): void {
    this.refreshAuthState();
    // Login/logout happen via SPA navigation, not a full reload — re-check auth state on every
    // route change so the toolbar (nav links, username, cart) updates immediately.
    this.routerSub = this.router.events
      .pipe(filter(e => e instanceof NavigationEnd))
      .subscribe(() => this.refreshAuthState());
  }

  ngOnDestroy(): void {
    this.routerSub?.unsubscribe();
  }

  private refreshAuthState(): void {
    this.loggedIn = this.auth.isLoggedIn();
    this.username = this.loggedIn ? this.auth.getUsername() : null;
  }

  logout(): void {
    this.auth.logout();
  }
}
