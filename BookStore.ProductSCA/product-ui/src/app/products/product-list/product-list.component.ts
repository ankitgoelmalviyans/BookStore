import { Component, OnInit } from '@angular/core';
import { ProductService } from '../../core/services/product.service';
import { CartService } from '../../core/services/cart.service';
import { InventoryService } from '../../core/services/inventory.service';
import { Product } from '../../core/models/product.model';
import { Router } from '@angular/router';
import { HttpContext } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { SUPPRESS_404_TOAST } from '../../core/http-context-tokens';

@Component({
  selector: 'app-product-list',
  standalone: true,
  imports: [MatCardModule, MatTableModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule, MatSnackBarModule],
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
    private inventoryService: InventoryService,
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
    if (!product.price || product.price <= 0) {
      this.snackBar.open(`"${product.name}" has no price set yet and can't be ordered.`, 'Dismiss', { duration: 4000 });
      return;
    }

    // Checked at add-to-cart time rather than for the whole list on load — avoids an inventory
    // lookup per row just to render the table, at the cost of one call on the actual add action.
    this.inventoryService.getByProductId(product.id, new HttpContext().set(SUPPRESS_404_TOAST, true)).subscribe({
      next: (inventory: any) => {
        const available = (inventory?.quantity ?? 0) - (inventory?.reserved ?? 0);
        if (available <= 0) {
          this.snackBar.open(`"${product.name}" is out of stock.`, 'Dismiss', { duration: 4000 });
          return;
        }
        this.cartService.addItem(product, 1);
        this.snackBar.open(`Added "${product.name}" to cart`, 'Dismiss', { duration: 3000 });
      },
      error: () => {
        this.snackBar.open(`"${product.name}" has no inventory record yet and can't be ordered.`, 'Dismiss', { duration: 4000 });
      }
    });
  }
}
