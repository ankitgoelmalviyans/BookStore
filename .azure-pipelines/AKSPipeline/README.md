
# 📘 How to Run AKS CI/CD Pipeline with Azure DevOps

This guide outlines how to set up and run your Kubernetes pipeline for the **BookStore** project using Azure DevOps. It covers everything from creating variable groups to triggering the build and release pipelines.

---

## 🔐 Step 1: Create Azure DevOps Variable Group (Secrets)

1. Navigate to **Pipelines > Library** in Azure DevOps.
2. Click **+ Variable group**.
3. Set the group name: `BookStoreSecrets`
4. Add the following variables:

| Name                   | Example Value                         |
|------------------------|----------------------------------------|
| AzureWebJobsStorage    | EnableProxies                          |
| CosmosEndpoint         | testingthisvalueendpoint               |
| CosmosKey              | testingmyvalue                         |
| inventoryHost          | bookstoremicro.duckdns.org             |
| JwtKey                 | Longsecretkey                          |
| KafkaBootstrapServers  | servername                             |
| KafkaPassword          | test                                   |
| KafkaUsername          | test                                   |
| productHost            | bookstoremicro.duckdns.org             |
| ServiceBusConnection   | test                                   |

> 🔒 Mark sensitive variables as secret by clicking the lock icon.

---

## 📦 Step 2: Run Infrastructure Pipeline (IaC)

Run the IaC pipeline to provision:

- AKS Cluster
- Azure Container Registry (ACR)
- Namespace (`bookstore`)
- Secrets in AKS (`kubectl create secret`)
- NGINX Ingress Controller
- Cert-Manager & ClusterIssuer

### Sample `kubectl` Secrets Script:
```sh
kubectl create secret generic productservice-secrets \
  --from-literal=Jwt__Key="$(JwtKey)" \
  --from-literal=Kafka__BootstrapServers="$(KafkaBootstrapServers)" \
  --from-literal=Kafka__Username="$(KafkaUsername)" \
  --from-literal=Kafka__Password="$(KafkaPassword)" \
  --from-literal=Cosmos__Endpoint="$(CosmosEndpoint)" \
  --from-literal=Cosmos__Key="$(CosmosKey)" \
  --from-literal=ServiceBus__Connection="$(ServiceBusConnection)" \
  --namespace="bookstore" --dry-run=client -o yaml | kubectl apply -f -
```

---

## 🌐 Step 3: Configure DuckDNS & Ingress IP

### ✅ DuckDNS Setup
- Go to [https://www.duckdns.org/](https://www.duckdns.org/)
- Choose a subdomain, e.g., `bookstoremicro`
- Map it to your Ingress Controller's external IP

### 🔍 Get External IP from Ingress
```bash
kubectl get ingress -n bookstore
```

- Copy the external IP and update it in DuckDNS for your chosen subdomain

---

## 🏗️ Step 4: Run Build Pipeline (Docker/Kubernetes)

Trigger the Build pipeline in Azure DevOps to:

- Build Docker image
- Push image to ACR
- Publish deployment artifacts

### Sample YAML:
```yaml
- task: Docker@2
  inputs:
    command: buildAndPush
    containerRegistry: 'bookstore-acr'
    repository: 'productservice'
    tags: '$(Build.BuildId)'
```

> 💡 You can use `docker-compose` or separate Dockerfiles for each microservice.

---

## 🚀 Step 5: Run Release Pipeline (Deploy to AKS)

Trigger your Release pipeline to:

- Download and extract artifacts
- Apply Kubernetes deployment (`kubectl apply`) or Helm chart
- Deploy Ingress rules for `/product`, `/inventory`, etc.
- Set up DNS with DuckDNS and TLS with cert-manager

### Common Tasks:
- `kubectl apply -f deployment.yaml`
- `kubectl apply -f service.yaml`
- `kubectl apply -f ingress.yaml`

---

## ✅ Final Checklist

| Task                                    | Done |
|-----------------------------------------|------|
| Azure DevOps Variable Group created     | ✅    |
| Infrastructure Pipeline completed       | ✅    |
| DuckDNS domain mapped to Ingress IP     | ✅    |
| Docker image built and pushed to ACR    | ✅    |
| Build artifacts published               | ✅    |
| Release pipeline deployed to AKS        | ✅    |
| Application accessible via DuckDNS      | ✅    |

---

## 🧠 Tips

- Use `kubectl describe` and `kubectl logs` for debugging deployments.
- Always version your container images (e.g., `:v1.0.0`).
- Use `kubectl port-forward` for local testing if needed.

```sh
kubectl port-forward svc/productservice 8090:80 -n bookstore
```

---

## 📎 References

- [DuckDNS](https://www.duckdns.org/)
- [Azure DevOps Pipelines](https://learn.microsoft.com/en-us/azure/devops/pipelines/)
- [AKS Documentation](https://learn.microsoft.com/en-us/azure/aks/)
