
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


# 🛠️ Bookstore Project: AKS, Docker, Ingress Troubleshooting & Learnings (May 2025)

This document captures all relevant debugging steps, configuration tips, and interview question preparation related to the last 15 days of work around Kubernetes (AKS), Docker, Ingress, Certificates, Secrets, and Proxy (APIM + Azure Function).

---

## ✅ Part 1: Kubernetes (AKS) Debugging Checklist

### 🔹 General Troubleshooting
- `kubectl get pods -n <namespace>` — Check pod status (Running, CrashLoopBackOff, etc.)
- `kubectl logs <pod-name> -n <namespace>` — View logs from failing pod
- `kubectl describe pod <pod-name> -n <namespace>` — Deep diagnostics
- `kubectl exec -it <pod-name> -n <namespace> -- /bin/bash` — Log into container shell

### 🔹 Common Errors
- **Credentials Error**: `the server has asked for the client to provide credentials`
  - Fix:  
    ```bash
    az aks get-credentials --resource-group BookStoreRG --name bookstore-aks --overwrite-existing
    ```

- **CORS Issues**:
  - Annotate ingress:
    ```bash
    kubectl annotate ingress bookstore-ingress     nginx.ingress.kubernetes.io/enable-cors="true"     nginx.ingress.kubernetes.io/cors-allow-origin="*"     nginx.ingress.kubernetes.io/cors-allow-methods="PUT, GET, POST, DELETE, PATCH, OPTIONS"     nginx.ingress.kubernetes.io/cors-allow-headers="DNT,X-CustomHeader,Keep-Alive,User-Agent,X-Requested-With,If-Modified-Since,Cache-Control,Content-Type,Authorization"     -n bookstore --overwrite
    ```

- **Ingress Not Routing**:
  - Check ingress address:  
    `kubectl get ingress -n bookstore`
  - Validate DNS: Is it updated to the public IP?
  - Check ingress rules and paths (e.g., `/product`, `/inventory`)

### 🔹 ASP.NET Core Specific
- Ensure `ASPNETCORE_URLS` is set to `http://+:80` in `deployment.yaml`
- Dockerfile must include `EXPOSE 80`

---

## ✅ Part 2: Docker Troubleshooting

### 🔹 Key Dockerfile Essentials
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
COPY . .
RUN dotnet publish -c Release -o out

FROM base AS final
COPY --from=build /app/out .
ENTRYPOINT ["dotnet", "YourApp.dll"]
```

### 🔹 Docker Run Test (Locally)
```bash
docker build -t bookstore-productservice .
docker run -p 8080:80 bookstore-productservice
```

---

## ✅ Part 3: Ingress, TLS & Domain (DuckDNS)

### 🔹 Troubleshooting HTTPS with Ingress
- Validate cert-manager and ClusterIssuer
- Use Let's Encrypt staging issuer for dry run
- Final ingress must use proper TLS secret name linked with DuckDNS

```yaml
  tls:
  - hosts:
    - bookstoremicro.duckdns.org
    secretName: bookstore-tls
```

### 🔹 Ingress Testing
```bash
kubectl describe ingress bookstore-ingress -n bookstore
kubectl get svc -n bookstore
curl -vk https://bookstoremicro.duckdns.org/product/api/products
```

---

## ✅ Part 4: Secret Management via Pipeline

- Inject secrets using Azure DevOps CLI task:
```bash
kubectl create secret generic productservice-secrets --from-literal=Kafka__Username=$(KafkaUsername) --from-literal=Kafka__Password=$(KafkaPassword) --namespace=$(namespace) --dry-run=client -o yaml | kubectl apply -f -
```

### 🔹 Mistake to Avoid:
- Secret key mismatch (e.g., `Kafka__KafkaUsername` vs `Kafka__Username`)
- Not updating secrets after pipeline changes

---

## ✅ Part 5: Proxy Layer with Azure Function

### Why Needed:
- APIM can’t reach DuckDNS directly due to HTTPS/cert restrictions

### Solution:
- Use Azure Function as proxy
- Accept incoming API call from APIM, forward to DuckDNS service, return response

### Azure Function Sample
```csharp
[FunctionName("ProductProxy")]
public async Task<IActionResult> Run(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "product/{*path}")] HttpRequest req,
    string path,
    ILogger log)
{
    var client = new HttpClient();
    var response = await client.GetAsync($"https://bookstoremicro.duckdns.org/product/{path}");
    var content = await response.Content.ReadAsStringAsync();
    return new ContentResult { Content = content, ContentType = "application/json" };
}
```

---

## 🎯 Interview & Scenario-Based Questions (Docker + Kubernetes + AKS)

1. **Explain how Ingress works in Kubernetes. How do you troubleshoot routing issues?**
2. **Why is `ASPNETCORE_URLS=http://+:80` important in containerized .NET apps?**
3. **What are the ways to inject secrets securely in Kubernetes?**
4. **How can you integrate API Management with services hosted behind custom domains (e.g., DuckDNS)?**
5. **What is the difference between ClusterIP, NodePort, and LoadBalancer in AKS?**
6. **When to use cert-manager vs manually created secrets for TLS?**
7. **Can you explain how Azure DevOps pipelines can provision Kubernetes secrets and deploy images?**
8. **How do you enable and test CORS in NGINX Ingress?**

---

This document will continue to evolve as we add more real-world scenarios. Feel free to duplicate, fork, or update in your BookStore project documentation.





