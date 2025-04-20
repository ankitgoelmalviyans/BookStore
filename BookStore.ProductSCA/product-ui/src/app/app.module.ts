import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { RouterModule } from '@angular/router';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { HttpClientModule } from '@angular/common/http';
import { ReactiveFormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { HTTP_INTERCEPTORS } from '@angular/common/http';
import { AuthInterceptor } from './core/auth.interceptor';
import { MatTableModule } from '@angular/material/table';


import { LoginComponent } from './auth/login/login.component';
import { ProductListComponent } from './products/product-list/product-list.component';
import { InventoryComponent } from './inventory/inventory/inventory.component';
import { ProductFormComponent } from './products/product-form/product-form.component';


import { AppComponent } from './app.component';

@NgModule({
  declarations: [AppComponent,
    LoginComponent,
    ProductListComponent,
    InventoryComponent,
    ProductFormComponent],
  imports: [
    BrowserModule,
    HttpClientModule,
    ReactiveFormsModule,
    BrowserAnimationsModule,
    MatCardModule,
    MatInputModule,
    MatButtonModule,
    MatTableModule,
    RouterModule.forRoot([
      { path: 'login', component: LoginComponent },
      { path: 'products', component: ProductListComponent },
      { path: 'products/add', component: ProductFormComponent },          
      { path: 'products/edit/:id', component: ProductFormComponent },     
      { path: 'inventory/:id', component: InventoryComponent },
      { path: '', redirectTo: 'login', pathMatch: 'full' }
    ])
  ],
  providers: [
    { provide: HTTP_INTERCEPTORS, useClass: AuthInterceptor, multi: true }
  ],
  bootstrap: [AppComponent]
})
export class AppModule { }