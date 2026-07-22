import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { InventoryService } from '../../core/services/inventory.service';
import { Inventory } from '../../core/models/inventory.model';

@Component({
  selector: 'app-inventory',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, MatCardModule, MatFormFieldModule,
    MatInputModule, MatButtonModule, MatProgressSpinnerModule, MatSnackBarModule
  ],
  templateUrl: './inventory.component.html',
  styleUrls: ['./inventory.component.css']
})
export class InventoryComponent implements OnInit {
  inventory: Inventory | null = null;
  loading = true;
  loadFailed = false;
  productId!: string;
  restockForm: FormGroup;

  constructor(
    private route: ActivatedRoute,
    private service: InventoryService,
    private fb: FormBuilder,
    private snackBar: MatSnackBar
  ) {
    this.restockForm = this.fb.group({
      quantity: [0, [Validators.required, Validators.min(0)]]
    });
  }

  ngOnInit() {
    this.productId = this.route.snapshot.paramMap.get('id')!;
    this.loadInventory();
  }

  loadInventory() {
    this.loading = true;
    this.service.getByProductId(this.productId).subscribe({
      next: data => {
        this.inventory = data;
        this.restockForm.patchValue({ quantity: data.quantity });
        this.loading = false;
      },
      error: (err: HttpErrorResponse) => {
        this.loading = false;
        this.inventory = null;
        // A 404 means "never stocked" — a normal empty state, not an app error. Anything else
        // (network/5xx) is a real failure and shouldn't render as if there's just no record.
        this.loadFailed = err.status !== 404;
      }
    });
  }

  updateStock() {
    if (this.restockForm.invalid) {
      return;
    }
    const quantity = this.restockForm.value.quantity;
    this.service.update(this.productId, quantity).subscribe({
      next: () => {
        this.snackBar.open('Inventory updated', 'Dismiss', { duration: 3000 });
        this.loadFailed = false;
        this.loadInventory();
      },
      error: () => {
        this.snackBar.open('Failed to update inventory', 'Dismiss', { duration: 3000 });
      }
    });
  }
}
