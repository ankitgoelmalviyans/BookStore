import { Component, OnInit } from '@angular/core';
import { ProductService } from '../../core/product.service';
import { Router } from '@angular/router';

@Component({
  selector: 'app-product-form',
  templateUrl: './product-list.component.html'
})
// @Component({
//   selector: 'app-product-list',
//   template: `
//   <mat-card>
//     <h2>Product List</h2>
//     <table mat-table [dataSource]="products" class="mat-elevation-z8">
//       <ng-container matColumnDef="name">
//         <th mat-header-cell *matHeaderCellDef> Name </th>
//         <td mat-cell *matCellDef="let p"> {{p.name}} </td>
//       </ng-container>
//       <ng-container matColumnDef="price">
//         <th mat-header-cell *matHeaderCellDef> Price </th>
//         <td mat-cell *matCellDef="let p"> ₹{{p.price}} </td>
//       </ng-container>
//       <ng-container matColumnDef="action">
//         <th mat-header-cell *matHeaderCellDef> Inventory </th>
//         <td mat-cell *matCellDef="let p">
//           <button mat-button (click)="viewInventory(p.id)">View</button>
//         </td>
//       </ng-container>

//       <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
//       <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
//     </table>
//   </mat-card>
//   `
// })
export class ProductListComponent implements OnInit {
  products: any[] = [];
  displayedColumns = ['name', 'price', 'action'];
  constructor(private productService: ProductService, private router: Router) {}

  ngOnInit(): void {
    this.loadProducts();
  }

  loadProducts(): void {
    this.productService.getAll().subscribe((data: any) => {
      this.products = data;
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

}
