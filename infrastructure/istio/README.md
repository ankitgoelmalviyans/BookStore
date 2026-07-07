# Istio — minimal install for Profile A

Scoped deliberately small for a single `Standard_B2s` AKS node. See `docs/HLD.md` and
`docs/ROADMAP.md` for the full reasoning; this file is the "how do I actually run/verify/undo this"
reference.

## What's here

| File | What it does | Applied how |
|---|---|---|
| `istio-operator-minimal.yaml` | `istiod` control plane, no ingress gateway, tuned-down resource requests | `.github/workflows/infra-bicep.yml` (manual `workflow_dispatch`) |
| `peer-authentication.yaml` | mTLS in **PERMISSIVE** mode (not STRICT — see the file's own comment for why STRICT would break live traffic through NGINX) | Same pipeline run |
| `virtual-service-resilience.yaml` | Retries/timeout/connection-pool policy for `inventoryservice` — the "Polly without code" example | Safe to apply anytime; doesn't touch real ingress traffic |
| `authorization-policy-reference.yaml` | **NOT applied.** Reference only — see the file's own comment for why applying it today would break the live site |
| `fault-injection-demo.yaml` | **NOT applied by default.** Apply manually for a short demo, then delete |

## Install

1. Run `Infrastructure — Bicep (Always-On)` from the Actions tab (`workflow_dispatch`). This installs
   `istiod` and applies the mTLS policy as part of that existing workflow — see its
   "Check node fit after Istio install" step output for `kubectl top nodes` and pod status.
2. **Sidecars don't exist yet after step 1.** Injection is a pod-creation-time mutating webhook —
   existing Product/Inventory pods aren't retroactively modified. Run `CD — Deploy (Profile A
   Cost-Optimised)` next (or push any commit) so those two services' pods get recreated and
   actually pick up the `istio-proxy` sidecar.
3. Confirm injection actually happened:
   ```bash
   kubectl get pods -n bookstore -o jsonpath='{range .items[*]}{.metadata.name}{"\t"}{.spec.containers[*].name}{"\n"}{end}'
   ```
   Product/Inventory pods should show 2 containers each (their app container + `istio-proxy`).
   AuthService should still show 1 — it's deliberately not meshed.
4. Apply the resilience policy (safe, doesn't affect real ingress traffic):
   ```bash
   kubectl apply -f infrastructure/istio/virtual-service-resilience.yaml
   ```

## If it doesn't fit — the concrete signal, and rollback

Watch for `Pending` pods or `Insufficient cpu`/`Insufficient memory` events:
```bash
kubectl get pods -n bookstore -n istio-system
kubectl get events -n bookstore --sort-by='.lastTimestamp' | grep -i insufficient
```

To back out cleanly:
```bash
kubectl delete -f infrastructure/istio/virtual-service-resilience.yaml --ignore-not-found
kubectl delete -f infrastructure/istio/peer-authentication.yaml --ignore-not-found
istioctl uninstall -y --purge
kubectl delete namespace istio-system
```
Then remove the `sidecar.istio.io/inject` annotation from Product/Inventory's Helm values (see
`infrastructure/helm/productservice/values.yaml` / `inventoryservice/values.yaml`) and redeploy —
this drops the sidecar from those pods on their next recreation.

## Actually seeing it work (there's no real internal caller yet)

Neither ProductService nor InventoryService calls the other over HTTP today (they communicate via
Service Bus). NGINX Ingress bypasses the mesh entirely (no sidecar). So there's no *real* traffic
flowing through Istio's L7 routing to casually observe. To generate real mesh traffic for
verification:

```bash
# Exec into productservice's pod (it's meshed) and call inventoryservice from inside it —
# this call actually goes through both pods' sidecars, so mTLS/retries/fault-injection all apply.
kubectl exec -it deploy/productservice -n bookstore -c productservice -- \
  curl -s -o /dev/null -w "%{http_code}\n" http://inventoryservice/api/inventory
```

To prove mTLS is active on that call:
```bash
istioctl proxy-config secret deploy/productservice -n bookstore
```

To prove the fault-injection/retry policy actually does something (not just trusted YAML):
```bash
kubectl apply -f infrastructure/istio/fault-injection-demo.yaml
# Re-run the curl above a few times — you should see some 503s (the injected fault) and some 200s
# (where the retry policy in virtual-service-resilience.yaml successfully retried past the fault).
kubectl delete -f infrastructure/istio/fault-injection-demo.yaml
```

## Envoy access logs in Splunk

Once sidecars are injected, `istio-proxy` containers write JSON access logs to stdout — the same
Fluent Bit DaemonSet already tailing every container in this namespace picks them up automatically
(the grep allowlist in `infrastructure/helm/fluent-bit/values.yaml` was updated to include
`istio-proxy`). Search Splunk for `kubernetes.container_name="istio-proxy"` to see every proxied
call, independent of whether the application itself logged anything — see `docs/LLD.md` for the
full app-log-vs-infra-log distinction.
