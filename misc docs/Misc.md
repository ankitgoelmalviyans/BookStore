
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




BookStore Project - 26th, 27th, and 28th April

26th April 2025 - Setup and Initial Issues

Configured Azure App Services for ProductService, InventoryService, and Product-UI.

Published Angular dist/ folder and learned correct deployment structure for Azure Linux App Services.

Learned how to check logs using Log Stream, SSH, and Advanced Tools (Kudu) in Azure App Service.

Debugged Angular UI deployment issues (404 errors) and fixed proper dist/product-ui packaging.

Identified missing default page setup in Linux Web App (fixed manually).

Observed how deployment behaves differently between .NET Core apps and Angular static files.

27th April 2025 - Azure API Management (APIM) Setup

Created Azure API Management (APIM) instance bookstore-apim.

Provisioned APIM using Azure CLI inside the pipeline.

Imported ProductService and InventoryService into APIM through release pipeline.

Discovered that Swagger import doesn't automatically set Web Service URL (backend URL) and found solution.

Corrected Import Swagger command by adding --service-url.

Understood APIM Developer Portal and added APIs manually to be visible there.

Created Subscription and User (ankit) inside APIM for API access.

Learned APIM enforces Ocp-Apim-Subscription-Key header during requests.

Tested API calls via Developer Portal and Postman.

28th April 2025 - Security and Testing Improvements

Enabled JWT Validation Policies on ProductService API inside APIM.

Understood difference between Service-side JWT Validation vs APIM-side JWT Validation.

Finalized strategy: Let services validate JWT internally (APIM only forwards JWT).

Understood that:

ProductService requires JWT.

InventoryService does not require JWT.

Created a full Postman Collection to test APIs:

AuthService call to get JWT Token.

Call ProductService API with Authorization + Subscription-Key.

Call InventoryService API with only Subscription-Key.

Observed:

Without JWT, ProductService returns 401 Unauthorized.

Without Subscription Key in Developer Portal, APIM returns 401 Subscription Missing.

Direct Postman calls to App Services work if JWT provided but Subscription Key missing (because App Service doesn't block that yet).

Key Learnings

Full understanding of Azure App Service deployment (for both .NET and Angular).

Hands-on Azure API Management creation, configuration, import.

Learned difference between OpenAPI import vs Backend Service configuration.

Built automated provisioning and release pipelines.

Learned Security Policies in APIM (JWT Validation, Subscription Key enforcement).

Developed complete testing strategy using Postman.

Next Goals (Moving Forward)

Automate Swagger imports with --service-url set correctly in pipelines.

Properly manage Subscription Plans and Products inside APIM.

Tighten security further by restricting direct App Service access (optional).

Finalize clean API Documentation inside Developer Portal.

Personal Growth

Improved Azure CLI, Azure DevOps YAML skills.

Gained strong real-world experience with APIM lifecycle.

Improved troubleshooting ability with Azure resources.

Enhanced API security understanding (JWT, API Management, Gateway Security).

Achievements

✅ First successful pipeline-based APIM Import and Testing.
✅ Secured APIs through Subscription Key and JWT.
✅ No manual APIM setup required now except small tweaks (e.g., Developer Portal tweaks).



