import { Component, OnInit } from '@angular/core';
import { ProductService } from '../../core/product.service';
import { CartService } from '../../core/cart.service';
import { Product } from '../../core/models/product.model';
import { Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';

@Component({
  selector: 'app-product-list',
  templateUrl: './product-list.component.html',
  styleUrls: ['./product-list.component.css']
})
export class ProductListComponent implements OnInit {
  products: Product[] = [];
  loading = true;
  displayedColumns = ['name', 'price', 'category', 'actions'];

  constructor(
    private productService: ProductService,
    private cartService: CartService,
    private router: Router,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.loadProducts();
  }

  loadProducts(): void {
    this.loading = true;
    this.productService.getAll().subscribe({
      next: (data: Product[]) => {
        this.products = data;
        this.loading = false;
      },
      error: () => {
        // ErrorInterceptor already surfaces a toast — just stop the spinner so it isn't stuck.
        this.loading = false;
      }
    });
  }

  viewInventory(productId: string) {
    this.router.navigate(['/inventory', productId]);
  }

  deleteProduct(id: string) {
    if (confirm('Are you sure you want to delete this product?')) {
      this.productService.delete(id).subscribe(() => {
        this.loadProducts();
      });
    }
  }

  editProduct(id: string) {
    this.router.navigate(['/products/edit', id]);
  }

  addProduct() {
    this.router.navigate(['/products/add']);
  }

  addToCart(product: Product) {
    this.cartService.addItem(product, 1);
    this.snackBar.open(`Added "${product.name}" to cart`, 'Dismiss', { duration: 3000 });
  }
}
