# 📘 Book Store Microservices Project - Overview & Status

---

## 🔰 Project Summary

The **Book Store Microservices Project** is a real-world practice implementation designed to explore advanced architecture using:

- ✅ .NET Core Web API (Clean Architecture)
- ✅ Microservices with **Self-Contained System (SCS)** pattern
- ✅ Kafka (Confluent Cloud) for Event-Driven Communication
- ✅ Azure Service Bus (Pluggable alternative to Kafka)
- ✅ Azure Cosmos DB / In-Memory DB (Plug & Play)
- ✅ Azure App Services (Web App Deployment)
- ✅ Angular (v17) Frontend (SCS-based)
- ✅ CI/CD Pipelines (Planned)
- ✅ API Gateway with Azure API Management (Planned)

---

## 🏗️ System Architecture (3 SCSs)

### ✅ Product SCS (Delivered)
- `ProductService`: CRUD for Products
- `InventoryService`: Handles inventory updates (subscribes to ProductCreated event)
- Event publishing via Kafka (Confluent) or Azure Service Bus
- Azure Cosmos DB + InMemory DB (configurable)
- Angular UI for Product Add/Edit/View

### 🔄 Order SCS (Upcoming)
- `OrderService`: Create/Track orders
- `PaymentService` (optional)
- Angular UI for Order Management

### 🔄 Customer SCS (Upcoming)
- `CustomerService`: Manage customer profile
- Angular UI for Customer Details

### ✅ Shared Auth Service
- JWT Token generation for login
- Angular UI integrated with login

---

## 💬 Messaging Support

| Type | Tool | Status |
|------|------|--------|
| Async Event Communication | ✅ **Kafka (Confluent Cloud)** | Enabled |
| Azure Alternative | ✅ **Azure Service Bus** | Supported (Pluggable) |
| Retry + DLQ + Logging | Kafka Retry + Fallback (Planned) | 🔄 |

---

## 🌐 Database Support

- Cosmos DB (Azure) ✅
- In-Memory DB ✅
- Configurable via settings (plug-and-play for services)

---

## 🚀 Deployment Plan

| Resource | Deployment | Status |
|----------|------------|--------|
| Auth Service | Azure App Service | ✅ Done |
| ProductService + InventoryService | Azure App Service | ✅ Done |
| Angular UI (Product SCS) | Local | ✅ Done |
| Angular UI (Others) | Planned | 🔄 |
| API Gateway | Azure API Management | 🔄 Planned |
| CI/CD Pipeline | GitHub Actions | 🔄 Planned |

---

## ✅ Completed Milestones

- [x] Auth Service with JWT Token
- [x] Product + Inventory Services
- [x] Kafka Integration (Working end-to-end)
- [x] Azure Service Bus Supported (Pluggable)
- [x] Cosmos DB + InMemory DB Support
- [x] Angular UI (Login, Products, Inventory)
- [x] Product CRUD with Angular Material
- [x] Kafka/Azure Events tested successfully
- [x] Repository initialized with `.gitignore` and secrets excluded
- [x] Product SCS Backend + Frontend (Except Deployment) Completed

---

## 📌 In Progress / Upcoming

- [ ] Deployment Guide + CI/CD Setup (NOW STARTING ✅)
- [ ] API Gateway with Azure API Management (NEXT 🔜)
- [ ] Angular UI for Customer and Order SCS
- [ ] Retry + DLQ for Kafka consumers
- [ ] Order SCS Microservices
- [ ] Customer SCS Microservices

---

## 📁 Project Repository Structure
```
BookStore/
│
├── AuthService/                    # JWT Auth Microservice
├── BookStore.ProductSCA/          # Product + Inventory SCS
│   ├── ProductService/
│   ├── InventoryService/
│   └── product-ui/   # Angular UI for Product SCS
├── BookStore.OrderSCA/            # (Upcoming)
├── BookStore.CustomerSCA/         # (Upcoming)
└── README.md                      # Project Overview
```

---

## 🔐 Security
- JWT authentication using custom Auth Service
- Token injected via Angular interceptor
- Role-based routing (planned)

---

## 💡 Tech Stack

| Layer | Tech |
|-------|------|
| Backend | .NET Core 8, Clean Arch |
| Frontend | Angular v17 + Material UI |
| Messaging | Kafka / Azure Bus |
| Auth | JWT |
| DB | CosmosDB / InMemory |
| Hosting | Azure App Services |
| DevOps | GitHub Desktop + Actions (Planned) |

---

