import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ProductService } from '../../core/product.service';

@Component({
  selector: 'app-product-form',
  templateUrl: './product-form.component.html'
})
export class ProductFormComponent implements OnInit {
  productForm: FormGroup;
  isEdit = false;
  productId: string | null = null;

  constructor(
    private fb: FormBuilder,
    private route: ActivatedRoute,
    private router: Router,
    private productService: ProductService
  ) {
    this.productForm = this.fb.group({
      name: [''],
      description: [''],
      quantity: [0]
    });
  }

  ngOnInit(): void {
    this.productId = this.route.snapshot.paramMap.get('id');
    if (this.productId) {
      this.isEdit = true;
      this.productService.getById(this.productId).subscribe((product: any) => {
        this.productForm.patchValue(product);
      });
    }
  }

  onSubmit(): void {
    const product = this.productForm.value;
    if (this.isEdit && this.productId) {
      const updatedProduct = {
        ...product,
        id: this.productId 
      };
      this.productService.update(this.productId, updatedProduct).subscribe(() => {
        this.router.navigate(['/products']);
      });
    } else {
      this.productService.create(product).subscribe(() => {
        this.router.navigate(['/products']);
      });
    }
  }
}
