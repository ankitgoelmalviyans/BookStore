# Product SCS - Book Store Microservice Suite

This SCS (Self-Contained System) represents the **Product Subsystem** of the Book Store project. It contains:

- ✅ `ProductService`: Manages product catalog (CRUD, get-by-id).
- ✅ `InventoryService`: Maintains stock based on `ProductCreated` events.
- ✅ Integrated Angular UI (with Product List, Inventory View, Add/Edit Product).

---

## 📦 Microservice Architecture

- Self-contained system with dedicated UI, backend, and data.
- Each service is **independently deployable**, **loosely coupled**, and follows **Domain-Driven Design (DDD)**.
- Communication is event-driven via messaging.

---

## 💬 Messaging Support (Pluggable)

We support **both**:

- ✅ **Azure Service Bus** (enabled via config)
- ✅ **Confluent Kafka (Cloud)** (integration-ready)

The messaging engine is **plug-and-play** using configuration settings in `appsettings.json`. Azure is **currently enabled**.

**Events Handled:**
- `ProductCreatedIntegrationEvent` from `ProductService` to `InventoryService`

---

## 🗃️ Database Support

Supports **In-Memory** and **Azure Cosmos DB** (plug-and-play via config)

- ✅ Cosmos DB used for persistence in production.
- ✅ In-memory useful for local testing/dev.

---

## 🧩 Features

- 🔐 JWT Auth via shared `AuthService`
- 📄 Swagger enabled for all APIs
- 🟢 Health Checks configured
- 📜 Serilog-based structured logging
- 🔁 Retry, error logging & DLQ for messaging (planned for Kafka)

---

## 🌐 Angular UI (Minimal + Material)

- 📥 Login Page
- 📦 Product Listing (Read-only)
- ➕ Add Product (name, description, quantity)
- ✏️ Edit Product (updates product data)
- 🧾 Inventory View (per product)

All calls are secured via **JWT Token** stored in localStorage.

---

## 🚀 Deployment (Planned)

Deployment to Azure (App Services, Cosmos DB, etc.) is planned.
- CI/CD via GitHub Actions or Azure Pipelines
- Resource provisioning through configuration (no Terraform/Bicep)
- Updates will be added to this section soon.

---

## 🧠 Walkthrough Flow

1. **User logs in** (JWT Token received via AuthService)
2. **ProductService** exposed via API (`/api/product`) and secured via token
3. **InventoryService** listens to `ProductCreated` events and adjusts stock
4. Angular UI connects via HttpClient + AuthInterceptor
5. **Events published** to Azure Service Bus or Kafka
6. Cosmos DB stores persistent product/inventory data

---

## 📁 Folder Structure

```
BookStore.ProductSCA/
│
├── ProductService/
│   ├── Application/
│   ├── Core/
│   ├── Infrastructure/
│   ├── Controllers/
│   └── Program.cs
│
├── InventoryService/
│   ├── Application/
│   ├── Core/
│   ├── Infrastructure/
│   ├── Controllers/
│   └── Program.cs
│
├── product-ui/ (Angular)
│   ├── auth/
│   ├── products/
│   ├── inventory/
│   ├── core/
│   └── app.module.ts
```

---

Let us know if you want to switch to another messaging engine or add other services like Order or Customer SCS. ✅

