import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class ProductService {
  constructor(private http: HttpClient) {}

  getAll() {
    return this.http.get(`${environment.apiBaseUrl}/product`);
  }

  addProduct(product: any) {
    return this.http.post(`${environment.apiBaseUrl}/product`, product);
  }
  
  getById(id: string) {
    return this.http.get(`${environment.apiBaseUrl}/product/${id}`);
  }
  
  create(product: any) {
    return this.http.post(`${environment.apiBaseUrl}/product`, product);
  }
  
  update(id: string, product: any) {
    return this.http.put(`${environment.apiBaseUrl}/product/${id}`, product);
  }
  
  delete(id: string) {
    return this.http.delete(`${environment.apiBaseUrl}/product/${id}`);
  }
  

}
