
# 📝 Daily Notes - Azure, API Security & Architecture (BookStore Project)

## 📅 Date: 2025-04-25

---

## ✅ Topics Covered Today

### 🔐 1. Azure API Management (APIM)
- Used as a **secure gateway** to expose microservices externally.
- Validates **JWT tokens** using `AuthService`.
- Hides direct service URLs (`ProductService`, `InventoryService`).
- Can import APIs using **OpenAPI (Swagger) JSON**.
- Integrated into **DevOps Release pipeline** for automated API import.
- Angular UI communicates with backend only via APIM.

---

### 🔁 2. Intra-SCS vs Inter-SCS Communication

| Term         | Meaning                            | Your Setup        |
|--------------|-------------------------------------|-------------------|
| Intra-SCS    | Communication within same SCS       | ✅ Kafka/Azure Bus |
| Inter-SCS    | Communication between different SCS | ✅ Kafka/Azure Bus |

- REST is not used between services — everything is async = **loosely coupled**.
- Only Angular UI calls backend using HTTP REST via APIM.

---

### 🧾 3. OpenAPI (Swagger)

- `/swagger/v1/swagger.json` → Auto-generated spec file.
- Enables API testing via Swagger UI (`/swagger/index.html`).
- Can be imported into:
  - APIM
  - Swagger Editor Online (https://editor.swagger.io/)
  - Client generators like NSwag, AutoRest
- Can support **versioning** with multiple Swagger docs (v1, v2).
- Can also be **checked into project** as static `.json` files.

---

### 🔐 4. Shared Access Signature (SAS)

- SAS = **Shared Access Signature** (NOT to be confused with "Service Authorization Service").
- Time-limited token to grant access to:
  - Azure Blob Storage
  - Azure Files
  - Azure Queues
- Does not expose full storage account key.

---

### 🛡️ 5. Service Authorization Service (SAS)

- **Different from Shared Access Signature**.
- A custom/internal API that validates:
  - "Is Service A allowed to call Service B?"
- Usually synchronous call during request pipeline.
- Can return `authorized: true/false` based on policies or scopes.
- Could be used in BookStore if REST calls are introduced across SCSs.

---

### 🔄 6. OAuth 2.0 vs JWT

| Feature          | OAuth 2.0                      | JWT                           |
|------------------|--------------------------------|-------------------------------|
| What it is       | Authorization Protocol         | Token Format                  |
| Usage            | Defines flow to get tokens     | Actual token used             |
| Are they related?| ✅ Often used together         | ✅ Yes, issued in OAuth flows |
| In BookStore     | Used via simplified AuthService| ✅ JWTs issued to clients     |

- OAuth 2.0 defines **how** to get the token.
- JWT defines **what** the token looks like.

---

