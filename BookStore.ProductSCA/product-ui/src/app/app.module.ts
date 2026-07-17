import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { RouterModule } from '@angular/router';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { HttpClientModule } from '@angular/common/http';
import { ReactiveFormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatBadgeModule } from '@angular/material/badge';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { HTTP_INTERCEPTORS } from '@angular/common/http';
import { AuthInterceptor } from './core/auth.interceptor';
import { ErrorInterceptor } from './core/error.interceptor';
import { AuthGuard } from './core/auth.guard';
import { MatTableModule } from '@angular/material/table';


import { LoginComponent } from './auth/login/login.component';
import { ProductListComponent } from './products/product-list/product-list.component';
import { InventoryComponent } from './inventory/inventory/inventory.component';
import { ProductFormComponent } from './products/product-form/product-form.component';
import { NavToolbarComponent } from './shared/nav-toolbar/nav-toolbar.component';


import { AppComponent } from './app.component';

@NgModule({
  declarations: [AppComponent,
    LoginComponent,
    ProductListComponent,
    InventoryComponent,
    ProductFormComponent,
    NavToolbarComponent],
  imports: [
    BrowserModule,
    HttpClientModule,
    ReactiveFormsModule,
    BrowserAnimationsModule,
    MatCardModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatToolbarModule,
    MatBadgeModule,
    MatSnackBarModule,
    MatProgressSpinnerModule,
    MatTableModule,
    RouterModule.forRoot([
      { path: 'login', component: LoginComponent },
      { path: 'products', component: ProductListComponent, canActivate: [AuthGuard] },
      { path: 'products/add', component: ProductFormComponent, canActivate: [AuthGuard] },
      { path: 'products/edit/:id', component: ProductFormComponent, canActivate: [AuthGuard] },
      { path: 'inventory/:id', component: InventoryComponent, canActivate: [AuthGuard] },
      {
        path: 'orders',
        canActivate: [AuthGuard],
        loadChildren: () => import('./orders/orders.module').then(m => m.OrdersModule)
      },
      { path: '', redirectTo: 'login', pathMatch: 'full' }
    ])
  ],
  providers: [
    { provide: HTTP_INTERCEPTORS, useClass: AuthInterceptor, multi: true },
    { provide: HTTP_INTERCEPTORS, useClass: ErrorInterceptor, multi: true }
  ],
  bootstrap: [AppComponent]
})
export class AppModule { }