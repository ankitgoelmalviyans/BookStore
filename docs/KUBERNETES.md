# Kubernetes Operations Guide — BookStore on AKS

> Commands use the real names from this repo: cluster `bookstore-aks-ga`, resource group
> `BookStoreRG_GA`, namespace `bookstore`, static IP `104.211.94.129`.

---

## What is Kubernetes and why we use it

Kubernetes (K8s) is a container orchestrator: you declare the desired state (this image, this many
replicas, this much CPU, this health check) and it continuously makes reality match — rescheduling
crashed pods, wiring service discovery, and load-balancing.

**Why not just VMs for BookStore?**
- Three .NET services + a log shipper on one box would mean hand-managing processes, restarts, ports,
  and rollouts. K8s gives us self-healing (`livenessProbe` restarts a wedged pod), declarative
  rollouts (`helm upgrade`), and rollback (`helm rollback`) for free.
- **AKS** is the *managed* control plane — Azure runs the API server / etcd / scheduler; we only pay
  for and manage the one `Standard_B2s` worker node. That is what keeps this a ~$22/month cluster.

We deliberately run **one node, one replica per service** — availability traded for cost. K8s still
earns its keep through self-healing, rollout/rollback, service discovery, and the ingress model.

---

## BookStore Cluster Architecture

| Namespace | Workloads |
|-----------|-----------|
| **bookstore** | `authservice`, `productservice`, `inventoryservice` Deployments (ClusterIP Services); `fluent-bit` DaemonSet; secrets `authservice-secrets`, `productservice-secrets`, `inventoryservice-secrets`, `splunk-secrets` |
| **ingress-nginx** | `ingress-nginx-controller` Deployment + **LoadBalancer** Service holding static IP **104.211.94.129** |
| **cert-manager** | `cert-manager` (+ `letsencrypt-prod` ClusterIssuer) — TLS wiring is PARTIAL (nip.io blocks HTTP-01) |

External traffic: `http://104.211.94.129.nip.io/{auth|product|inventory}/...` → NGINX (ingress-nginx)
→ strips the prefix → ClusterIP Service → pod `:80`.

---

## Helm Deep Dive

### What is Helm and why over raw `kubectl apply`
Helm is the K8s package manager. Instead of static YAML per service, a **chart** is a *template* +
*values*. Benefits here:
- **DRY:** all three services share one templated Deployment/Service/Ingress/HPA (see the library
  chart below) instead of three near-identical copies.
- **Parameterisation:** `--set image.tag=<sha>` and `--set ingress.host=<ip>.nip.io` at deploy time.
- **Release lifecycle:** `helm upgrade --install`, `helm history`, `helm rollback` — versioned,
  reversible releases. Raw `kubectl apply` has none of that.

### Why the `bookstore-lib` library chart
`infrastructure/helm/bookstore-lib` is a **library chart** (`type: library`) that defines reusable
named templates:
- `bookstore-lib.deployment` — Deployment with env from `*-secrets`, `/health` readiness+liveness
  probes, resource requests/limits, Prometheus scrape annotations.
- `bookstore-lib.service` — ClusterIP Service on port 80.
- `bookstore-lib.ingress` — NGINX ingress with `rewrite-target: /$2`, `use-regex`, per-service path.
- `bookstore-lib.hpa` — HorizontalPodAutoscaler (rendered only if `autoscaling.enabled`).

### How per-service charts use it
Each service chart (`authservice`, `productservice`, `inventoryservice`) declares `bookstore-lib` as
a dependency (`Chart.yaml`) and its templates are one-liners:
```yaml
# infrastructure/helm/authservice/templates/deployment.yaml
{{- include "bookstore-lib.deployment" . }}
```
The service's own `values.yaml` supplies the specifics (name, image repo, ingress path, resources,
`autoscaling.enabled`). ProductService sets `autoscaling.enabled: true` (min 1 / max 3); Auth and
Inventory leave it `false`.

### How `values-costopt.yaml` vs `values-demo.yaml` work
These are **profile overlays** applied on top of a service's own values at deploy time
(`helm upgrade --install ... --values values-costopt.yaml`). They carry global toggles
(`gateway.useApim`, `llm.*`) and pipeline-injected fields (`image.tag`, `ingress.host`). Profile A
(costopt) = NGINX + GitHub Models; Profile B (demo) = APIM + Azure OpenAI. Same images, different
overlay.

---

## Helm commands reference

```bash
# List all releases in the namespace and their revision/status
helm list -n bookstore

# Full revision history of a release (each helm upgrade = a new revision)
helm history authservice -n bookstore

# Show the merged values a release was deployed with
helm get values authservice -n bookstore

# Render templates locally WITHOUT touching the cluster (dry run) — what CI does
helm template authservice infrastructure/helm/authservice \
  --values infrastructure/helm/values-costopt.yaml \
  --set ingress.host=104.211.94.129.nip.io \
  --set image.tag=abc1234

# Roll back to a previous good revision (e.g. revision 1)
helm rollback authservice 1 -n bookstore

# Remove a release entirely
helm uninstall authservice -n bookstore
```

---

## Daily Operations

### Start AKS routine
```bash
# 1. Start the stopped cluster
az aks start --name bookstore-aks-ga --resource-group BookStoreRG_GA

# 2. Pull kubeconfig credentials
az aks get-credentials --resource-group BookStoreRG_GA --name bookstore-aks-ga --overwrite-existing

# 3. Re-apply the NGINX LB health-probe fix (see war story below) — external :80 won't route without it
kubectl annotate svc ingress-nginx-controller -n ingress-nginx \
  service.beta.kubernetes.io/azure-load-balancer-health-probe-request-path=/healthz \
  --overwrite

# 4. Verify pods are back
kubectl get pods -n bookstore
```

### Stop AKS
```bash
az aks stop --name bookstore-aks-ga --resource-group BookStoreRG_GA
```
Stopping deallocates the node → no compute charge while idle. The static IP and all Azure PaaS
(Cosmos, Service Bus, ACR) persist.

---

## Debugging Commands — Basic to Advanced

### Level 1 — Basic status
```bash
kubectl get pods -n bookstore                 # are pods Running / Ready?
kubectl get pods -n bookstore -o wide         # + node, pod IP
kubectl get svc -n bookstore                  # ClusterIP services
kubectl get ingress -n bookstore              # ingress hosts/paths
kubectl get pods -n ingress-nginx             # is the ingress controller up?
```

### Level 2 — Pod logs
```bash
kubectl logs -l app=authservice -n bookstore --tail=50       # last 50 lines by label
kubectl logs -l app=authservice -n bookstore --follow        # live tail
kubectl logs -l app=authservice -n bookstore --previous      # logs from the crashed instance
kubectl logs authservice-6d8f7c9b4-abcde -n bookstore        # a specific pod
# All three services at once:
kubectl logs -l app=authservice -n bookstore --tail=20 & \
kubectl logs -l app=productservice -n bookstore --tail=20 & \
kubectl logs -l app=inventoryservice -n bookstore --tail=20 &
```

### Level 3 — Pod inspection
```bash
kubectl describe pod -l app=authservice -n bookstore          # events, probes, image, restarts
kubectl exec deploy/authservice -n bookstore -- printenv | grep -E 'Jwt|Auth|Cosmos'  # env vars
kubectl get secrets -n bookstore
kubectl port-forward deployment/authservice 8080:80 -n bookstore   # then:
curl http://localhost:8080/health
kubectl exec -it deploy/authservice -n bookstore -- /bin/sh        # shell inside the pod
```

### Level 4 — Cluster events & capacity
```bash
kubectl get events -n bookstore --sort-by='.lastTimestamp'   # ordered event stream
kubectl top nodes                                            # node CPU/mem (needs metrics-server)
kubectl top pods -n bookstore                                # per-pod usage
az aks show --name bookstore-aks-ga --resource-group BookStoreRG_GA --query "powerState" -o table
```

### Level 5 — Ingress & networking
```bash
kubectl describe ingress authservice-ingress -n bookstore
kubectl logs deployment/ingress-nginx-controller -n ingress-nginx --tail=30
# Test routing from INSIDE the cluster with the right Host header:
kubectl run test-curl --rm -it --image=curlimages/curl -n bookstore -- \
  curl -H "Host: 104.211.94.129.nip.io" http://ingress-nginx-controller.ingress-nginx/auth/health
# What external IP did the LB get?
kubectl get svc ingress-nginx-controller -n ingress-nginx \
  -o jsonpath='{.status.loadBalancer.ingress[0].ip}'
# NSG rules (80/443 must be Allowed) — resolve the AKS node resource group first:
MC_RG=$(az aks show -g BookStoreRG_GA -n bookstore-aks-ga --query nodeResourceGroup -o tsv)
NSG=$(az network nsg list -g $MC_RG --query "[0].name" -o tsv)
az network nsg rule list -g $MC_RG --nsg-name $NSG -o table
```

### Level 6 — Secrets & config
```bash
kubectl get secrets -n bookstore
kubectl describe secret authservice-secrets -n bookstore              # keys, not values
# Decode a single value (base64):
kubectl get secret authservice-secrets -n bookstore -o jsonpath='{.data.Jwt__Key}' | base64 -d
kubectl get configmap -n bookstore
kubectl describe configmap <name> -n bookstore
```

---

## Common Problems and Solutions

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| **ImagePullBackOff** | Wrong tag, or ACR not attached to AKS | `kubectl describe pod` to see the exact image; confirm `az aks update --attach-acr bookstoreaurega`; verify the short-SHA tag exists in ACR |
| **CrashLoopBackOff** | App throws on startup — usually a missing/empty secret (`Jwt__Key`, Cosmos endpoint) | `kubectl logs --previous`; check `*-secrets`; the CD job recreates them from GitHub Secrets |
| **Pod Pending** | No schedulable capacity on the single B2s node | `kubectl describe pod` (Insufficient cpu/memory); lower requests or scale the node pool |
| **External URL times out** | **Azure LB health probe hitting `/` returns 404 → LB marks backend unhealthy** | Annotate the ingress LB service health-probe path to `/healthz` (see war story) |
| **404 from NGINX** | Ingress path/rewrite mismatch, or wrong Host | `kubectl describe ingress`; confirm the path regex `/auth(/\|$)(.*)` + `rewrite-target: /$2`; use the correct `*.nip.io` Host header |
| **Service Bus errors** | Bad/empty `AzureServiceBus__ConnectionString`, or topic/subscription missing | Check the secret; confirm topic `product-events` + subscription `inventory-subscription` exist (Bicep creates them) |
| **Cosmos errors** | Empty endpoint/key, wrong DB/container, or throttling (429) | Check `CosmosDb__*` secret values; confirm DB `BookStoreDB` + containers `Products`/`Inventory`; transient 429s → the subscriber abandons+retries |
| **CD pipeline fails at ACR** | Rotated/incorrect `ACR_USERNAME`/`ACR_PASSWORD`, or ACR admin disabled | Refresh the GitHub Secrets from `az acr credential show`; (Managed Identity for ACR is a Phase-5 fix) |
| **AKS API unreachable after session expiry** | Cluster was stopped, or kubeconfig token expired | `az aks start …` then `az aks get-credentials … --overwrite-existing` |

### The Azure LB health-probe war story (great interview answer)
**Symptom:** After a fresh infra deploy, everything looked healthy *inside* the cluster —
`kubectl get pods` all Running, `kubectl port-forward` to a service returned 200 — but hitting
`http://104.211.94.129.nip.io/auth/health` from a browser **timed out**.

**Diagnosis:** The NGINX ingress is exposed by a Kubernetes `Service` of type `LoadBalancer`, which
provisions an **Azure Load Balancer**. Azure's LB only forwards traffic to backends its **health
probe** considers healthy. By default that probe did an HTTP GET on **`/`** of the NGINX controller —
and NGINX returns **404** for `/` (there's no route there). A 404 is "unhealthy" to the probe, so the
LB pulled the node out of rotation and silently dropped all external traffic on port 80. Internally
everything worked because internal traffic never goes through the Azure LB.

**Fix:** Point the LB health probe at NGINX's dedicated `/healthz` endpoint (which returns 200) via a
service annotation:
```bash
kubectl annotate svc ingress-nginx-controller -n ingress-nginx \
  service.beta.kubernetes.io/azure-load-balancer-health-probe-request-path=/healthz \
  --overwrite
```
This is baked into both `infra-bicep.yml` (at install) and `cd-costopt.yml` (re-applied every
deploy), **and** into the daily start routine — because a cluster stop/start can reset the LB
configuration. The lesson: *"pods healthy" and "reachable from the internet" are different questions,
and the Azure Load Balancer health probe sits exactly on that boundary.*
