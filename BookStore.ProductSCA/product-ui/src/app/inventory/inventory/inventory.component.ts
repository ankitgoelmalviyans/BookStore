import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { InventoryService } from '../../core/services/inventory.service';
import { Inventory } from '../../core/models/inventory.model';

@Component({
  selector: 'app-inventory',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatProgressSpinnerModule],
  templateUrl: './inventory.component.html',
  styleUrls: ['./inventory.component.css']
})
export class InventoryComponent implements OnInit {
  inventory: Inventory | null = null;
  loading = true;
  loadFailed = false;

  constructor(private route: ActivatedRoute, private service: InventoryService) {}

  ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id');
    this.service.getByProductId(id!).subscribe({
      next: data => {
        this.inventory = data;
        this.loading = false;
      },
      error: (err: HttpErrorResponse) => {
        this.loading = false;
        // A 404 means "never stocked" — a normal empty state, not an app error. Anything else
        // (network/5xx) is a real failure and shouldn't render as if there's just no record.
        this.loadFailed = err.status !== 404;
      }
    });
  }
}
