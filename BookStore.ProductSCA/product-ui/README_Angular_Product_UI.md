# 📘 Angular Product SCS UI - Walkthrough Guide

This guide provides a complete walkthrough of the Angular project used to manage **Auth**, **Product**, and **Inventory** services in the Book Store SCS.

---

## 🧱 1. Project Structure

```
product-ui-full-working/
├── src/
│   ├── app/
│   │   ├── auth/
│   │   │   └── login/
│   │   │       └── login.component.ts / .html / .css
│   │   ├── products/
│   │   │   ├── product-list/
│   │   │   └── product-form/
│   │   ├── inventory/
│   │   │   └── inventory/
│   │   ├── core/
│   │   │   ├── auth.service.ts
│   │   │   ├── product.service.ts
│   │   │   ├── inventory.service.ts
│   │   │   └── auth.interceptor.ts
│   │   ├── app.module.ts
│   │   ├── app.component.ts / .html
├── angular.json
├── package.json
```

---

## 🚀 2. Setup Commands

```bash
npm install           # Install dependencies
npx ng serve -o       # Start development server on http://localhost:4200
```

---

## 🔐 3. Authentication Flow

- **LoginComponent** handles user login.
- `AuthService` sends POST request to `AuthService` API and saves JWT token in local storage.
- All requests are intercepted by `AuthInterceptor` which adds `Authorization` header with Bearer token.

### login.component.ts
```ts
this.auth.login(this.loginForm.value).subscribe((res: any) => {
  this.auth.saveToken(res.token);
  this.router.navigate(['/products']);
});
```

---

## 📦 4. Product Management

### product-list.component.html
- Displays product list in a table
- Provides buttons for **Add**, **Edit**, **Delete**, **View Inventory**

### product-form.component.ts
- Handles **add** and **edit** of product
- If `productId` exists → Edit, else → Create

### Routing Setup in `app.module.ts`
```ts
RouterModule.forRoot([
  { path: 'login', component: LoginComponent },
  { path: 'products', component: ProductListComponent },
  { path: 'products/add', component: ProductFormComponent },
  { path: 'products/edit/:id', component: ProductFormComponent },
  { path: 'inventory/:id', component: InventoryComponent },
  { path: '', redirectTo: 'login', pathMatch: 'full' }
])
```

---

## 🧮 5. Inventory View

- `InventoryComponent` shows stock for a product (using product ID in route)
- Data fetched from `InventoryService` via GET call to Inventory API

```ts
ngOnInit(): void {
  const productId = this.route.snapshot.paramMap.get('id');
  this.inventoryService.getInventory(productId!).subscribe(...);
}
```

---

## ⚙️ 6. Core Services

### AuthService
- `login()` sends credentials to `/api/login`
- `saveToken()` stores JWT in localStorage

### ProductService
- CRUD methods: `getAll()`, `getById()`, `create()`, `update()`, `delete()`

### InventoryService
- `getInventory(productId: string)` returns quantity

---

## 🛡️ 7. HTTP Interceptor

### auth.interceptor.ts
Adds JWT to every outgoing request:
```ts
const token = this.auth.getToken();
if (token) {
  req = req.clone({ setHeaders: { Authorization: `Bearer ${token}` }});
}
```

---

## 🧪 8. Testing APIs

Update these values if deployed to Azure:
```ts
apiUrl = 'https://bookstoreproductservice.azurewebsites.net/api/product';
authUrl = 'https://bookauthservice.azurewebsites.net/api/login';
```
You may configure environment-specific files later if needed.

---

## 🧠 Tips for Beginners

- Use `ng generate component` to scaffold components
- Add routing in `RouterModule.forRoot()`
- Keep `core/` folder for shared services and interceptors
- Use Angular Material components for quick UI
- Use browser DevTools (F12) → Network tab to debug API calls

---

## ✅ Future Enhancements

- Add loading spinners and error messages
- Add form validations with `Validators`
- Separate environments for dev/prod
- Add role-based access and guards
- Polish layout with Material Toolbar, Sidenav, etc.
- So far in pipeline we have to run manual these commands on bash but will take care in the pipeline in future 

   #!/bin/bash 
    cd /home/site/wwwroot
    npm install
    npm start

