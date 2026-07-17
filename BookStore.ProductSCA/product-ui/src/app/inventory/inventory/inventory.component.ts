import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { InventoryService } from '../../core/inventory.service';
import { Inventory } from '../../core/models/inventory.model';

@Component({
  selector: 'app-inventory',
  templateUrl: './inventory.component.html',
  styleUrls: ['./inventory.component.css']
})
export class InventoryComponent implements OnInit {
  inventory: Inventory | null = null;
  loading = true;

  constructor(private route: ActivatedRoute, private service: InventoryService) {}

  ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id');
    this.service.getByProductId(id!).subscribe({
      next: data => {
        this.inventory = data;
        this.loading = false;
      },
      error: () => {
        // No inventory record yet for this product (e.g. never stocked) — not an app error,
        // just an empty state the template already handles.
        this.loading = false;
      }
    });
  }
}
