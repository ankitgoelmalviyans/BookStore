import { Component, OnInit } from '@angular/core';
import { ProductService } from '../../core/services/product.service';
import { CartService } from '../../core/services/cart.service';
import { InventoryService } from '../../core/services/inventory.service';
import { RecommendationService } from '../../core/services/recommendation.service';
import { Product } from '../../core/models/product.model';
import { Router } from '@angular/router';
import { HttpContext } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { SUPPRESS_404_TOAST } from '../../core/http-context-tokens';

// A recommended partner, resolved against the already-loaded product catalog so the panel can
// show a name/price — RecommendationService itself only knows productId + co-purchase count
// (see recommendation.model.ts).
interface ResolvedRecommendation {
  productId: string;
  name: string;
  price: number;
  count: number;
}

@Component({
  selector: 'app-product-list',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatTableModule, MatIconModule, MatButtonModule, MatProgressSpinnerModule, MatSnackBarModule],
  templateUrl: './product-list.component.html',
  styleUrls: ['./product-list.component.css']
})
export class ProductListComponent implements OnInit {
  products: Product[] = [];
  loading = true;
  displayedColumns = ['name', 'price', 'category', 'actions'];

  // Which row's recommendations panel is open (one at a time), and a per-product cache so
  // re-opening a row already viewed this session doesn't re-fetch.
  expandedProductId: string | null = null;
  recommendationsLoading = false;
  private recommendationsCache = new Map<string, ResolvedRecommendation[]>();

  constructor(
    private productService: ProductService,
    private cartService: CartService,
    private inventoryService: InventoryService,
    private recommendationService: RecommendationService,
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

  // Classic ML, not AI — RecommendationService counts how often products are bought together
  // from order history; there's no model call involved in serving this panel.
  toggleRecommendations(product: Product): void {
    if (this.expandedProductId === product.id) {
      this.expandedProductId = null;
      return;
    }

    this.expandedProductId = product.id;

    if (this.recommendationsCache.has(product.id)) {
      return;
    }

    this.recommendationsLoading = true;
    this.recommendationService.getRecommendations(product.id).subscribe({
      next: (partners) => {
        const resolved: ResolvedRecommendation[] = partners
          .map((partner) => {
            const match = this.products.find((p) => p.id === partner.productId);
            return match
              ? { productId: partner.productId, name: match.name, price: match.price, count: partner.count }
              : null;
          })
          .filter((r): r is ResolvedRecommendation => r !== null);

        this.recommendationsCache.set(product.id, resolved);
        this.recommendationsLoading = false;
      },
      error: () => {
        // ErrorInterceptor already surfaces a toast for a real failure (network/5xx) — cache an
        // empty result here just so the panel's own "no recommendations yet" state renders
        // instead of leaving the spinner stuck.
        this.recommendationsCache.set(product.id, []);
        this.recommendationsLoading = false;
      }
    });
  }

  recommendationsFor(productId: string): ResolvedRecommendation[] {
    return this.recommendationsCache.get(productId) ?? [];
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
