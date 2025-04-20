import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { InventoryService } from '../../core/inventory.service';

@Component({
  selector: 'app-inventory',
  template: `
  <mat-card *ngIf="inventory">
    <h2>Inventory Info</h2>
    <p><strong>Product ID:</strong> {{inventory.productId}}</p>
    <p><strong>Quantity:</strong> {{inventory.quantity}}</p>
  </mat-card>
  `
})
export class InventoryComponent implements OnInit {
  inventory: any;

  constructor(private route: ActivatedRoute, private service: InventoryService) {}

  ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id');
    this.service.getInventory(id!).subscribe(data => this.inventory = data);
  }
}
