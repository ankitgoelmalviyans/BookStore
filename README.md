# 📚 BookStore Microservices Project

---

## 📌 Project Summary

The **BookStore Project** is a real-world enterprise-grade cloud-native architecture built on:

- ✅ ASP.NET Core 8 Web APIs with Clean Architecture
- ✅ Angular 17+ for UI (per Self-Contained System)
- ✅ Kafka (Confluent Cloud) for Async Messaging
- ✅ Azure Service Bus (Pluggable alternative to Kafka)
- ✅ Azure Cosmos DB / In-Memory DB (switchable per environment)
- ✅ Azure API Management (APIM) for secure external API gateway
- ✅ Azure App Services for hosting (no Docker for now)
- ✅ Azure DevOps for CI/CD Pipelines
- ✅ Self-Contained Systems (SCS) model with each domain independently deployable

---

## 🧹 Project Status Overview

| Category                         | Items Covered                                                                                  | Status     |
|----------------------------------|-----------------------------------------------------------------------------------------------|------------|
| Core Services Completed          | `ProductService`, `InventoryService`, `AuthService`                                             | ✅ Done     |
| Frontend Completed               | `product-ui` (Angular 17+ with Auth and Product Listing)                                       | ✅ Done     |
| Messaging Infrastructure         | Kafka (Confluent Cloud) setup + Azure Service Bus fallback support                            | ✅ Done     |
| CI/CD Pipelines                  | Azure DevOps pipeline for Product SCS (build, release, deploy)                                | ✅ Done     |
| API Management (APIM) Integration | APIM proxy setup + subscription key validation + Swagger import automated                    | ✅ Done     |
| Kubernetes Migration             | Move to Docker & Azure Kubernetes Service (AKS) for scalable deployment                       | 🔜 Planned  |
| JWT Validation at APIM Level     | JWT policy inbound validation (planned for Product/Inventory APIs)                            | 🔜 Planned  |
| Retry + Dead-Letter for Kafka    | Retry logic and DLQ fallback strategy implementation                                           | 🔜 Planned  |
| Remaining Services               | `OrderService`, `CustomerService`, and their respective Angular apps (`order-ui`, `customer-ui`) | 🔜 Planned  |
| Advanced Patterns                | Event Sourcing, CQRS (especially for Orders Domain)                                            | 🔜 Future Phase |

---

👉 **Current Position:**
- Product SCS = **Fully Functional**
- Core Infrastructure (Auth, Messaging, Pipelines, APIM) = **Ready**
- Small Improvements Pending = **JWT enforcement at APIM inbound**, **Order SCS next**

> 🔸 *Note:* Currently, Azure API Management (APIM) acts as a **secure proxy** with subscription key validation. JWT validation is performed by the microservices themselves. In the future, **APIM inbound policies** will handle **JWT validation**.

---

## 🧱 Architecture Overview

### 🎯 Core Building Blocks

| Domain Area       | Frontend        | Microservices                        | Status         |
|------------------|------------------|--------------------------------------|----------------|
| **Product SCS**   | `product-ui`     | `ProductService`, `InventoryService` | ✅ Complete     |
| **Order SCS**     | `order-ui`       | `OrderService`, `PaymentService (optional)` | 🚧 Planned |
| **Customer SCS**  | `customer-ui`    | `CustomerService`                    | 🚧 Planned     |
| **Auth Service**  | -                | `AuthService` (JWT Issuer)           | ✅ Complete     |

Each SCS has its own **frontend**, **microservices**, and **database**, and is independently deployable.

---

## 💬 Messaging Support

| Type | Tool | Status |
|------|------|--------|
| Async Event Communication | ✅ **Kafka (Confluent Cloud)** | Enabled |
| Azure Alternative | ✅ **Azure Service Bus** | Supported (Pluggable) |
| Retry + DLQ + Logging | Kafka Retry + Fallback | 🔄 Planned |

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
| Angular UI (Product SCS) | Local & Azure | ✅ Done |
| Angular UI (Order & Customer) | Planned | 🔄 |
| API Gateway (APIM) | Azure API Management | ✅ Done |
| CI/CD Pipeline | Azure DevOps | ✅ Done for Product SCS 🔄 Planned for others |

---

## 🛠 Tech Stack

| Layer        | Stack                                  |
|--------------|-----------------------------------------|
| Backend      | .NET 8 Web API + Clean Architecture     |
| Frontend     | Angular 17 + Angular Material           |
| Messaging    | Kafka (Confluent) / Azure Service Bus   |
| Auth         | JWT Token via Custom AuthService        |
| Gateway      | Azure API Management                    |
| Database     | Cosmos DB or In-Memory DB               |
| DevOps       | Azure Pipelines (CI/CD)                 |
| Hosting      | Azure App Services                      |

---

## 📂 Folder Structure

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
| Intra-SCS              | Kafka / Azure Service Bus / REST | Messaging within same SCS (e.g., ProductService → InventoryService). Asynchronous messaging ensures loose coupling. |
| Inter-SCS              | Kafka / Azure Service Bus        | Messaging across different SCSs (e.g., ProductService → OrderService). Event-driven. |
| External Access        | Azure API Management (APIM)      | UIs interact via APIM. |
| Auth                   | JWT Token via AuthService        | Authentication via tokens in API calls. |

> 🧟 Note: Only UI interacts via HTTP REST. Services use async events internally.

---

## 🛡️ Azure API Management (APIM)

### ✅ Why APIM?

- Secure API Gateway for external users
- Subscription key validation (Done)
- JWT inbound validation (Planned)
- Hide backend URLs
- Support rate limiting, analytics, versioning

### 🧹 Usage in BookStore

| UI App         | Routed SCS APIs    |
|----------------|--------------------|
| `product-ui`   | `/product`, `/inventory` |
| `order-ui`     | `/order`            |
| `customer-ui`  | `/customer`         |

> Services are private and accessible only via APIM.

---

## 🧐 Architecture Diagram

```
[ Browser/Client ]
       ↓
┌───────────────────────────────┐
│ Azure API Management (APIM)   │
└────────────├─────────────┐
             ↓
  ┌────────├──────────┐
  ↓        ↓          ↓
Product UI  Order UI  Customer UI
  ↓        ↓          ↓
Product SCS Order SCS Customer SCS
(CosmosDB+Kafka)
```

---

## ✅ Completed Milestones

- ✅ ProductService + InventoryService developed
- ✅ Kafka event-driven communication
- ✅ Angular `product-ui` with JWT auth and product listing
- ✅ AuthService for JWT issuance
- ✅ Product SCS build + deploy pipeline (Azure DevOps)
- ✅ Kafka/Azure Bus switchable design
- ✅ Cosmos DB and In-Memory DB setup
- ✅ Initial API Management integration

---

## 🗓️ Upcoming / Future Plans

- 🔜 Full APIM automation (rate-limiting, analytics)
- 🔜 Switch deployment to Docker and Kubernetes
- 🔜 JWT inbound validation in APIM
- 🔜 Develop OrderService + `order-ui`
- 🔜 Develop CustomerService + `customer-ui`
- 🔜 Retry logic + DLQ for Kafka
- 🔜 Explore Event Sourcing + CQRS

---

## 🔐 Security Practices

- ✅ JWT issued by AuthService
- ✅ Angular Interceptors for token
- ✅ Services validate JWT (currently)
- 🔒 Backend restricted to APIM IPs

---

## 🚀 DevOps & Deployment

- Full YAML pipelines (Azure DevOps)
- Product SCS build and release automated
- Swagger publishing automated
- Future APIM config push via CLI

---

## 🧱 Contributions & Improvements

> This project is built for learning enterprise microservice design with Azure event-driven architectures.

PRs, issues, forks are welcome!

---

## 📨 Contact

**Maintainer:** Ankit Goel  
📧 ankitmalviyans@gmail.com  
🌐 [LinkedIn](https://www.linkedin.com/in/ankit-goel-72722321/)

---

