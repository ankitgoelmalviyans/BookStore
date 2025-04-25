
# 📚 BookStore Microservices Project

---

## 📌 Project Summary

The **BookStore Project** is a real-world enterprise-grade cloud-native architecture built on:

- ✅ ASP.NET Core 8 Web APIs with Clean Architecture
- ✅ Angular 17 + for UI (per Self-Contained System)
- ✅ Kafka (Confluent Cloud) for Async Messaging
- ✅ Azure Service Bus (Pluggable alternative to Kafka)
- ✅ Azure Cosmos DB / In-Memory DB (switchable per environment)
- ✅ Azure API Management (APIM) for secure external API gateway
- ✅ Azure App Services for hosting (no Docker for now)
- ✅ Azure DevOps for CI/CD Pipelines
- ✅ Self-Contained Systems (SCS) model with each domain independently deployable

---

## 🧱 Architecture Overview

### 🎯 Core Building Blocks

| Domain Area       | Frontend        | Microservices                    | Status     |
|------------------|------------------|----------------------------------|------------|
| **Product SCS**   | `product-ui`     | `ProductService`, `InventoryService` | ✅ Complete |
| **Order SCS**     | `order-ui`       | `OrderService`, `PaymentService (optional)` | 🚧 Planned |
| **Customer SCS**  | `customer-ui`    | `CustomerService`                | 🚧 Planned |
| **Auth Service**  | -                | `AuthService` (JWT Issuer)       | ✅ Complete |

Each SCS has its own **frontend**, **microservices**, and **database** and is independently deployable.

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
| CI/CD Pipeline | Azure Devops | ✅ Done for Product 🔄 Planned for other SCS |

---

## 🛠 Tech Stack

| Layer        | Stack                                  |
|--------------|-----------------------------------------|
| Backend      | .NET 8 Web API + Clean Architecture     |
| Frontend     | Angular 17 + Angular Material           |
| Messaging    | Kafka (Confluent) / Azure Service Bus   |
| Auth         | JWT Token via Custom AuthService        |
| Gateway      | Azure API Management (planned)          |
| Database     | Cosmos DB or In-Memory DB               |
| DevOps       | Azure Pipelines (CI/CD)                 |
| Hosting      | Azure App Services                      |

---

## 🗂 Folder Structure

```plaintext
BookStore/
│
├── AuthService/                         # Shared Auth Microservice
├── BookStore.ProductSCA/
│   ├── ProductService/                  # Product Microservice
│   ├── InventoryService/                # Inventory Microservice
│   └── product-ui/                      # Angular Frontend
│
├── BookStore.OrderSCA/
│   ├── OrderService/                    # Order Microservice
│   └── order-ui/                        # Angular Frontend
│
├── BookStore.CustomerSCA/
│   ├── CustomerService/                 # Customer Microservice
│   └── customer-ui/                     # Angular Frontend
└── README.md                            # This guide
```

---


## 🔀 Inter-Service Communication

| Communication Type    | Approach                         | Description |
|------------------------|----------------------------------|-------------|
| Intra-SCS              | Kafka / Azure Service Bus/ REST  | Messaging between services **within the same SCS** (e.g., ProductService → InventoryService). Implemented using async messaging to ensure **loose coupling**. |
| Inter-SCS              | Kafka / Azure Service Bus        | Messaging between services **across different SCSs** (e.g., ProductService → OrderService). Event-driven for loose coupling. |
| External Access        | Azure API Management (APIM)      | Angular UIs access their SCS services via APIM. |
| Auth                   | JWT Token via AuthService        | All clients must authenticate via AuthService and include tokens in API calls. |

> 🧠 Note: Even within the same SCS (e.g., Product), we use **asynchronous messaging** (Kafka or Azure Bus) for microservice communication, so it remains **loosely coupled**. Only UI components interact with services using HTTP REST via APIM.
---

## 🛡️ Azure API Management (APIM)

### ✅ Why APIM?

- Acts as a secure **API Gateway** for external world
- Applies **JWT validation** using AuthService metadata
- Hides backend URLs, exposes only secure endpoints
- Supports **versioning, rate-limiting, analytics, and monitoring**

### 🧩 Usage in BookStore

Each Angular UI accesses its own SCS APIs via APIM:

| UI App         | SCS API Routed via APIM               |
|----------------|----------------------------------------|
| `product-ui`   | `/product`, `/inventory`              |
| `order-ui`     | `/order`                              |
| `customer-ui`  | `/customer`                           |

> All microservices are **private** — only accessible via APIM

---

## 🧠 Architecture Diagram

```
[ Browser/Client ]
       ↓
┌───────────────────────────────┐
│ Azure API Management (APIM)  │  ← Validates JWT via AuthService
└────────────┬──────────────────┘
             ↓
  ┌────────────────┬────────────┬─────────────┐
  ↓                ↓            ↓             ↓
Product UI    Order UI    Customer UI     (Hosted on Azure)
  ↓                ↓            ↓
Product SCS    Order SCS     Customer SCS
(Prod + Inv)    (Order)       (Customer)
   ↓             ↓               ↓
 Cosmos DB   Cosmos DB     Cosmos DB
Kafka/Bus   Kafka/Bus     Kafka/Bus
```

---

## ✅ Completed Milestones

- ✅ `ProductService` + `InventoryService` (Product SCS)
- ✅ Event-driven communication using Kafka
- ✅ Angular `product-ui` with authentication and product list
- ✅ Custom `AuthService` for JWT token issuance
- ✅ Azure DevOps pipeline: Build + Release setup for Product SCS
- ✅ Kafka & Azure Bus interchangeable integration
- ✅ In-memory DB and Cosmos DB support per service

---

## 📅 Upcoming Items

- 🔜 `OrderService` & `order-ui`
- 🔜 `CustomerService` & `customer-ui`
- 🔜 Retry logic and DLQ for Kafka consumers
- 🔜 APIM Swagger auto-import in pipeline
- 🔜 Deploy APIM with route/policy templates
- 🔜 Route Angular UIs through APIM
- 🔜 Add rate-limiting and API analytics to APIM
- 🔜 Visual flow diagrams created for full architecture

---

## 🔐 Security Practices

- ✅ JWT tokens issued by AuthService
- ✅ Angular UI sends tokens via HTTP Interceptors
- ✅ Microservices validate JWT (or APIM does)
- 🔒 Backend services restricted to APIM IP only
- 🔒 No direct public access to microservices

---

## 🚀 DevOps & Deployment

- Azure DevOps used for CI/CD
- All pipelines are YAML based
- Product SCS pipeline deploys:
  - ProductService
  - InventoryService
  - product-ui
  - Publishes artifacts with Swagger for APIM
- APIM integration will be automated via CLI in release pipeline

---

## 🤝 Contributions & Improvements

> This is a learning and reference project for distributed architecture and scalable microservice practices on Azure.

Feel free to fork, clone, raise issues, or submit PRs if you'd like to contribute!

---

## 📬 Contact

**Maintainer:** Ankit Goel  
📧 ankitgoelmalviyans@gmail.com  
🌐 [LinkedIn](https://linkedin.com/in/ankitgoelmalviyans)

---
