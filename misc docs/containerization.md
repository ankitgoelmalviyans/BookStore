# 🚀 Docker + Kubernetes (AKS) Crash Course + Bookstore Migration Summary

---

## ✅ PART 1: Docker Crash Course (Bookstore Context)

### What is Docker?
Docker is a tool to package applications into containers that include the app + dependencies.

### Key Concepts:
- **Dockerfile**: Defines how to build your container
- **Image**: A packaged version of your app (built from Dockerfile)
- **Container**: A running instance of an image

### Essential Commands:
```bash
# Build Docker image for ProductService
docker build -t bookstore-productservice .

# Run it locally for testing
docker run -p 8080:80 bookstore-productservice

# List images and containers
docker images
docker ps

# Push to Docker Hub / ACR
docker tag bookstore-productservice <registry>/bookstore-productservice

docker push <registry>/bookstore-productservice
```

### In Bookstore:
- Each microservice (ProductService, AuthService, etc.) gets its own Dockerfile.
- Used in pipeline to create versioned container images for AKS.

---

## ✅ PART 2: Kubernetes (AKS) Crash Course

### What is Kubernetes (K8s)?
A container orchestration platform for deploying, scaling, and managing containerized applications.

### Key Concepts:
- **Pod**: Smallest deployable unit (usually 1 container)
- **Deployment**: Defines how many pods to run, image, env vars
- **Service**: Exposes your deployment (internally or externally)
- **Ingress**: Optional HTTP router for domain-based routing
- **ConfigMap/Secret**: Inject configs/secrets into pods

### Basic Commands:
```bash
# See cluster resources
kubectl get pods
kubectl get svc
kubectl get deployments

# Apply deployment
kubectl apply -f deployment.yaml

# Check logs and pod health
kubectl logs <pod-name>
kubectl describe pod <pod-name>

# Delete resources
kubectl delete -f deployment.yaml

# Create secret with Kafka/Cosmos keys
kubectl create secret generic mysecrets   --from-literal=Kafka__BootstrapServers=xyz   --from-literal=Cosmos__Endpoint=abc   -n bookstore

# Port forward a service for local testing
kubectl port-forward svc/productservice 8091:80 -n bookstore

# Check cluster events
kubectl get events -n bookstore
```

---

## ✅ PART 3: Ingress + TLS (NGINX + DuckDNS)

### What is Ingress?
Ingress is an API object that manages external access to services in a cluster, typically via HTTP/HTTPS.

### Key Concepts:
- Route external traffic to internal services
- Supports path and host-based routing
- TLS termination via cert-manager

### Useful Commands:
```bash
# Get ingress and TLS info
kubectl get ingress -n bookstore
kubectl describe ingress bookstore-ingress -n bookstore

# Check cert-manager logs
kubectl logs -l app=cert-manager -n cert-manager

# Validate TLS certificates
kubectl get certificates -A
kubectl get certificaterequests -A

# Validate DNS
nslookup bookstoremicro.duckdns.org
```

### In Bookstore:
- NGINX Ingress + cert-manager
- TLS certificates via Let's Encrypt (ClusterIssuer)
- DuckDNS used to map Ingress External IP (fetched during provisioning) to domain
- Single DuckDNS domain (e.g., `bookstoremicro.duckdns.org`) used for both ProductService and InventoryService
- Routing follows path-based pattern:
  - `https://bookstoremicro.duckdns.org/product/api/product`
  - `https://bookstoremicro.duckdns.org/inventory/api/inventory`
- Ingress annotations for CORS:
```yaml
nginx.ingress.kubernetes.io/enable-cors: "true"
nginx.ingress.kubernetes.io/cors-allow-origin: "*"
```

---

## ✅ Bookstore Migration to AKS – Key Interview Talking Points

### 1. Unified AKS Deployment
- ProductService → Deployed on AKS
- InventoryService → Also migrated to AKS
- Both services routed securely via Azure API Management (APIM)

### 2. Configuration Strategy
- Kafka and Azure Service Bus are external services
- Connection details provided to AKS via ConfigMap or Secret
- No code change needed when moving to AKS

### 3. Secure Access
- ProductService in AKS exposed via **Internal LoadBalancer**
- Blocked from public internet
- Only **APIM** allowed to route traffic

### 4. Auto-Scaling Strategy
- Azure Web App: Can only scale by CPU/Memory
- AKS: Can scale based on **CPU, Memory, Kafka lag, Service Bus queue depth** (using KEDA)

### 5. DevOps Pipeline Impact
- Build step stays same (dotnet build → docker build → push)
- Release step changes: use `kubectl apply` instead of AzureWebApp task
- APIM `service-url` needs to point to new AKS internal/external IP

### 6. Real Use Case Advantage
- Flexible scaling for event-driven microservices
- Better isolation + network security
- Real-world hybrid pattern many companies use

---

## ✅ Interview Soundbites (Say These!)

- "We migrated both ProductService and InventoryService to AKS to gain full control over deployment, scaling, and network security."
- "Kafka and Azure Service Bus remained external — containerization didn’t require code change, just proper configuration."
- "AKS allows us to use KEDA to scale based on Kafka topic lag or Service Bus queue depth — not possible in Web Apps."
- "We use Azure API Management as the single entry point, routing to both AKS and App Services — providing a secure and unified API surface."
- "Deployment is fully automated via Azure DevOps pipeline — we build Docker images and deploy via `kubectl` in release stage."

---

## ✅ Differences: Azure Web App vs AKS

| Feature                          | Azure Web App                      | Azure Kubernetes Service (AKS)          |
|----------------------------------|------------------------------------|-----------------------------------------|
| Hosting Type                     | Platform-as-a-Service (PaaS)       | Container Orchestration (IaaS/CaaS)     |
| Deployment                       | Code or ZIP                        | Container Image (Docker)                |
| Auto-Scaling                     | CPU/Memory-based                   | CPU/Memory + Event-driven (KEDA)        |
| Load Balancer Control            | Managed by Azure internally        | Full control (internal/external LBs)    |
| Sidecar/Init Containers          | Not supported                      | Fully supported                         |
| Secrets & Config Injection       | App Settings / Key Vault binding   | Kubernetes Secrets & ConfigMaps         |
| Service Mesh / Inter-Service DNS | Not supported                      | Fully supported (`*.svc.cluster.local`) |
| Health Probes                    | Basic (via portal)                 | Advanced (readiness, liveness)          |
| Cost Optimization                | Limited control                    | Full node-level control                 |
| Ideal For                        | Simpler apps, MVPs                 | Scalable microservices, event-driven    |

---

## ✅ Deep Dive: Load Balancer Options (VMs, Web Apps, AKS)

### 1. VM + Azure Load Balancer
- You manually configure Layer 4 Load Balancer (TCP)
- Routes traffic across multiple VM nodes (e.g., WebNode A & B)
- Requires managing health probes, NAT rules, etc.

### 2. Azure Web App Load Balancer (PaaS Internal)
- Azure manages this for you automatically
- You can't configure routing policies or probes manually
- You can only scale using built-in settings (CPU/memory threshold)

### 3. AKS Load Balancer (Full Control)
- You can expose a service via LoadBalancer (public or internal)
- Can be restricted to private VNET or internal IP
- You can also use Ingress Controller (e.g., NGINX, Azure AGIC)
- Supports custom routing, TLS, host-based/path-based routing

---

## ✅ Note on IP Configuration
- We currently use a dynamic external IP assigned by AKS Ingress Controller
- After provisioning Ingress, the external IP is retrieved via:
  ```bash
  kubectl get ingress bookstore-ingress -n bookstore
  ```
- This IP is then manually set as A-record in DuckDNS for `bookstoremicro.duckdns.org`
- Static IP is **not** yet configured; pipeline re-runs may require updating DuckDNS

---

## ✅ CI/CD Pipeline Summary
- Infrastructure-as-Code (IAC), Build, and Release pipelines handle automated versioning and deployment of YAMLs for both ProductService and InventoryService
- These pipelines also apply secrets, configure ingress, and ensure deployments are consistent across environments

---

## ✅ What’s Next?
- Use KEDA for scaling based on Kafka or Service Bus metrics (if needed)
- Monitor Ingress health; update DuckDNS if external IP changes
- Plan to configure a static IP to avoid manual DNS updates
