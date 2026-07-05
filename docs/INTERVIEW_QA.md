# Interview Q&A — BookStore Project

> 60 questions, conversational answers, grounded in the real code. **PLANNED** marks anything not yet
> built. Answers are sized for a 2–3 minute spoken response.

---

## Section 1 — About You and Project Overview

**1. Tell me about your BookStore project.**
BookStore is a .NET 8 event-driven microservices platform I built and run on Azure to demonstrate
architect-level decision-making end to end. There are three services — AuthService issues JWTs,
ProductService owns the product catalog in Cosmos DB and publishes a `ProductCreatedEvent`, and
InventoryService subscribes to that event over Azure Service Bus and keeps stock in sync. An Angular
17 frontend on GitHub Pages sits in front. It all runs on a single-node AKS cluster behind an NGINX
ingress at `104.211.94.129.nip.io`, provisioned with Bicep and deployed entirely through GitHub
Actions, with Serilog logs shipped by Fluent Bit to Splunk. The whole thing costs about $22 a month,
which was a deliberate constraint.

**2. Why did you build this?**
To have a real, running system where I could practice and defend every architect-level trade-off —
async messaging vs HTTP, Cosmos vs SQL, Clean Architecture, IaC, CI/CD, observability — rather than
just talk about them abstractly. It's a portfolio and interview-prep project. The domain is
intentionally tiny so the architecture is the star, not the business logic.

**3. What is the most complex thing you built here?**
The end-to-end traceability across the async boundary. It's easy to trace a synchronous request; it's
harder when a message crosses a broker. I generate a CorrelationId in the Angular interceptor, thread
it through ProductService's middleware into the Serilog LogContext, copy it onto the Service Bus
message's `ApplicationProperties` in the producer, and read it back in the InventoryService
subscriber. So one id ties the whole business transaction together in Splunk even across the async
hop — because the OpenTelemetry TraceId doesn't propagate across Service Bus in this setup.

**4. What would you do differently if starting over?**
Three things. First, I'd standardise the middleware pipeline and error handling from day one — right
now AuthService returns a structured error with CorrelationId but ProductService just returns plain
text, and Inventory has no global handler. Second, I'd build the Outbox pattern into ProductService
immediately, because today the event publish is best-effort — it catches the failure and returns
success, which is a dual-write gap. Third, I'd wire an OpenTelemetry OTLP exporter early so I had a
real trace waterfall, not just TraceIds in logs.

**5. How does this relate to your work at Blackbaud?**
The AI layer on my roadmap is a direct port of things I built at Blackbaud — a RAG assistant
(`LocalRagAssistant`), a Semantic-Kernel enterprise agent (`EnterpriseAgent.Api`) doing intent
routing, and a text-to-SQL API. BookStore is where I re-implement those patterns on my own Azure
footprint, plus the microservices/messaging/observability foundation underneath them. **(The AI layer
is PLANNED — Phase 3.)**

**6. How long did this take to build?**
Phase 1 — the three services, messaging, Cosmos, the AKS/Bicep/Helm infrastructure, the CI/CD, and
the Splunk pipeline — is what's running today. It was built incrementally, and a lot of the time went
into the operational reality: the Azure load-balancer health-probe issue, CRI vs Docker log parsing,
the ingress path rewrites. The "make it actually reachable and observable" work was as big as the code.

**7. What is running in production right now?**
All of Phase 1: AuthService, ProductService, InventoryService, the Angular UI on GitHub Pages, Azure
Service Bus, Cosmos DB, the NGINX ingress on a static IP, Fluent Bit shipping to Splunk, and the full
GitHub Actions CI/CD. Live URLs are under `http://104.211.94.129.nip.io/{auth,product,inventory}`.

**8. What is planned but not built yet?**
OrderService (CQRS), PaymentService (Saga), NotificationService, the Inbox and Outbox patterns, the
AI layer, Istio canary, full APIM policy wiring, KEDA, an OTLP trace exporter, and a test suite.
It's all in `docs/ROADMAP.md`, clearly marked as planned.

**9. What was the hardest problem you solved?**
The "pods are healthy but the site times out from the internet" problem. Everything inside the
cluster worked, but external requests on port 80 hung. It turned out the Azure Load Balancer's health
probe was hitting `/` on the NGINX controller, which returns 404, so the LB marked the backend
unhealthy and dropped all external traffic. The fix was annotating the ingress service to point the
LB probe at `/healthz`. It's a great lesson that "running" and "reachable" are different questions.

**10. How do you keep costs low?**
Deliberate choices at every layer: Cosmos free tier, ACR Basic, one `Standard_B2s` node, GitHub Pages
for the UI so the frontend never touches AKS compute, nip.io instead of a paid domain, and a
stop/start schedule so the node deallocates when idle. Plus the two-profile model — the expensive
APIM/Azure-OpenAI profile is manual-only and self-destructs after four hours. That keeps the always-on
bill around $22/month.

---

## Section 2 — Architecture Questions

**11. Why microservices over a monolith for this?**
Honestly, for this domain size a monolith would be simpler — and I'd say that in a real review. I
chose microservices because the *point* is to demonstrate service boundaries, independent
deployability, and async messaging. The split is meaningful though: Auth, Product, and Inventory have
genuinely different responsibilities, data, and scaling profiles — ProductService has an HPA and
publishes events; InventoryService is a background consumer. The boundaries aren't arbitrary.

**12. Why does ProductService use Service Bus to talk to InventoryService instead of HTTP?**
Decoupling and resilience. With a direct HTTP call, ProductService would need to know Inventory's
URL, block on its latency, and lose the update if Inventory were down. With Service Bus, ProductService
publishes `ProductCreatedEvent` to the `product-events` topic and moves on — it's never heard of
InventoryService. If Inventory is down, the message waits in the subscription and is processed when it
recovers. I get automatic retry and a dead-letter queue for free. That's the whole async-messaging
value proposition, in code.

**13. What happens if InventoryService is down when a product is created?**
Nothing is lost. ProductService saves the product and publishes the event; Service Bus holds it on
`inventory-subscription`. When InventoryService comes back, its `ServiceBusProcessor` picks up the
backlog and applies each update. If a message keeps failing on a transient error, the subscriber
abandons it for redelivery, and after `MaxDeliveryCount` it goes to the dead-letter queue for
inspection — it never blocks the subscription forever.

**14. How do you handle distributed transactions across services?**
Today, I don't hold a distributed transaction — I use eventual consistency via events, which is the
right default for microservices. I'm honest that there's a current gap: ProductService's write to
Cosmos and its publish to Service Bus aren't atomic, and the code returns success even if the publish
fails. The **PLANNED** fix is the **Outbox pattern** — write the event to an outbox row in the same
Cosmos transactional batch as the product, and a relay ships it reliably. For multi-step business
transactions like order→payment, the plan is the **Saga pattern** with compensating actions, since
there's no two-phase commit across services.

**15. What's the difference between Profile A and Profile B?**
Same images, different Helm value overlay. Profile A (`values-costopt.yaml`) is cost-optimised: NGINX
ingress in-cluster, GitHub Models for the future LLM, always-on, deploys on every push to main,
~$22/month. Profile B (`values-demo.yaml`) is the enterprise demo: Azure API Management in front of
NGINX and Azure OpenAI, provisioned manually and torn down after four hours. The `llm` and APIM
*policy* wiring in B is PLANNED plumbing — the values are stubbed ahead of the services.

**16. Why Cosmos DB over SQL Server?**
Cost and fit. Cosmos has a free tier that keeps my always-on bill near zero, it's schema-flexible for
an evolving catalog, and point reads/writes by partition key are single-digit-ms. My documents are
keyed and partitioned by `/id`, so I never need joins. The trade-off is no relational querying, but I
don't need it here. And because I put a `IProductRepository` interface in the Core layer, swapping to
SQL Server later is a one-class, one-DI-line change in Infrastructure.

**17. How does Clean Architecture help here?**
It enforces the dependency rule: my Core layer defines `Product`, `IProductRepository`, and
`IMessagePublisher` and depends on nothing. Application orchestrates the use case against those
interfaces. Infrastructure implements them with the Cosmos and Service Bus SDKs. API is just HTTP. The
concrete payoff: `ProductService.CreateAsync` has no idea Cosmos or Service Bus exist, so I can unit
test it with fakes, and I can swap the database or the broker without touching business logic. That's
the thing I can literally point at in the code.

**18. What are the boundaries between services?**
AuthService owns identity — it issues JWTs and knows nothing else. ProductService owns the `Products`
container and the product lifecycle, and publishes `ProductCreatedEvent`; it doesn't know Inventory
exists. InventoryService owns the `Inventory` container and subscribes to product events; it never
calls ProductService. Each service owns its own Cosmos container — no shared database. That
"database-per-service" rule is what keeps them independently deployable.

**19. What is the `IMessagePublisher` interface and why do you have it?**
It's a one-method abstraction in Core — `PublishAsync<T>(T message, string topic)` — implemented by
`AzureServiceBusProducer`. It exists so the Application layer depends on the *idea* of publishing, not
on Service Bus. That gives me two things: testability (inject a mock and assert an event was
published) and swappability (a `KafkaPublisher` would be a new class plus one DI line, zero
business-logic changes). It's Dependency Inversion doing exactly its job.

**20. How would you add a new service to this architecture?**
Say I add NotificationService. It just adds its own subscription to the existing `product-events`
topic and starts receiving every event — ProductService doesn't change at all, because I used a Topic,
not a Queue. I'd give it its own Cosmos container if it needs state (this one wouldn't — it's
stateless), a Helm chart that includes the `bookstore-lib` library templates, a values file, and a
build/deploy job in the pipeline. The topic is the seam that keeps the producer closed for modification.

**21. What patterns are you using for resilience?**
Async messaging with a broker that buffers and retries; manual message settlement in the consumer
(`AutoCompleteMessages = false`) so I explicitly complete, abandon-for-retry, or dead-letter; the
dead-letter queue for poison messages; Kubernetes liveness/readiness probes on `/health` for pod
self-healing; and `helm rollback` on a failed deploy in the CD pipeline. PLANNED additions are the
Inbox pattern for idempotent consumers and KEDA for backlog-based scaling.

**22. How do you handle configuration per environment?**
Layered config: `appsettings.json` → environment-specific JSON → `serilog.json` → environment
variables, where env vars win. The committed `appsettings.json` ships empty placeholders for
secrets. At runtime, Kubernetes injects the real values as env vars from `*-secrets` via
`envFrom.secretRef`, using the `__` convention — `Jwt__Key` maps to `Jwt:Key`. So the same image runs
anywhere and picks up its environment purely from injected config.

**23. What's your data partitioning strategy in Cosmos DB?**
Both containers partition on `/id`. In `Products`, the id is the product's GUID. In `Inventory`, I key
each row by `ProductId` and set the document id equal to it — so there's exactly one inventory row per
product and the id equals the partition key value. That gives me efficient point reads and writes by
key. One subtlety: I annotate the id property with both `[JsonPropertyName("id")]` and Newtonsoft's
`[JsonProperty("id")]`, because the Cosmos SDK v3 serializer is Newtonsoft and ignores
System.Text.Json attributes — Cosmos requires a lowercase `id`.

**24. How do services authenticate with each other?**
Right now it's the shared JWT model, not service-to-service certs. AuthService signs tokens with a
symmetric `Jwt:Key`, and ProductService and InventoryService validate incoming bearer tokens against
that same key. All three share the key via the injected `Jwt__Key` secret. The inter-service hop
between Product and Inventory is over Service Bus, not HTTP, so it's broker-authenticated by the
connection string, not a JWT. A production system would move to asymmetric signing so services only
hold the public key.

**25. What would change if you needed to support 100x more load?**
Several levers. Scale out the consumers with **KEDA** on Service Bus queue depth instead of CPU
(PLANNED), because backlog is the real signal for a message consumer. Bump ProductService's HPA
ceiling and add nodes to the pool. Move Cosmos from free tier to provisioned or autoscale RU/s and
review the partition strategy for hot partitions. Add the **Inbox pattern** so at-least-once
redelivery under load doesn't double-apply updates. And I'd finally wire distributed tracing export so
I could actually see where the time goes under load.

---

## Section 3 — Kubernetes and AKS Questions

**26. What is a Helm chart and why do you use it?**
A Helm chart is a templated, versioned package of Kubernetes manifests plus a values file. I use it
so my three services share one set of templates instead of three copies of near-identical YAML — I
have a `bookstore-lib` library chart with the Deployment, Service, Ingress, and HPA templates, and
each service chart just includes them and supplies its own values. It also gives me release
lifecycle: `helm upgrade --install`, `helm history`, and `helm rollback`, which raw `kubectl apply`
doesn't.

**27. What's the difference between a Deployment and a DaemonSet?**
A Deployment runs *N* replicas of a pod, scheduled wherever there's room — that's my three services,
each `replicaCount: 1`. A DaemonSet runs *one* pod **per node**. Fluent Bit is a DaemonSet because
it's a log collector — it has to be on every node to tail that node's container logs from
`/var/log/containers`. One per node is exactly right for a node-level agent.

**28. How do your services discover each other inside AKS?**
Kubernetes DNS. Each service has a ClusterIP Service, so `authservice`, `productservice`, and
`inventoryservice` are resolvable names inside the `bookstore` namespace on port 80. The NGINX ingress
routes external path prefixes to those service names. That said, Product and Inventory don't call each
other directly — they communicate through Service Bus — so service discovery mostly matters for the
ingress-to-service hop.

**29. What is an Ingress and what does NGINX do in your setup?**
An Ingress is a rule set for routing external HTTP into cluster services. The NGINX Ingress Controller
is the thing that actually implements those rules. In my setup it's exposed by a LoadBalancer service
holding the static IP, and it routes by path: `/auth` to authservice, `/product` to productservice,
`/inventory` to inventoryservice. It also rewrites the path — it strips the `/auth` prefix so the pod
sees its native `/api/auth/login`. That's the `rewrite-target: /$2` annotation with a regex capture.

**30. How do secrets get into your pods?**
The CD pipeline reads them from GitHub Secrets and creates Kubernetes Secrets — `authservice-secrets`,
`productservice-secrets`, `inventoryservice-secrets` — with `kubectl create secret ... --dry-run
| kubectl apply`. The Deployment template pulls them in with `envFrom.secretRef`, so they land in the
pod as environment variables like `Jwt__Key` and `CosmosDb__AccountKey`, which .NET maps to
`Jwt:Key` and `CosmosDb:AccountKey`. Nothing sensitive is ever in git — `appsettings.json` ships empty
placeholders.

**31. What happens when a pod crashes? Walk me through the recovery.**
The Deployment's controller notices the pod's actual state no longer matches the desired replica count
and schedules a replacement. The liveness probe on `/health` catches a wedged-but-not-exited process
and restarts the container; the readiness probe keeps it out of the Service's endpoints until
`/health` returns 200, so no traffic hits it before it's ready. If it keeps crashing on startup —
usually a missing secret — it goes CrashLoopBackOff, and I check `kubectl logs --previous`.

**32. How would you scale your services under load?**
ProductService already has a HorizontalPodAutoscaler — min 1, max 3, target 70% CPU. Auth and
Inventory have HPAs defined but disabled. For the message consumer, CPU is the wrong signal, so the
PLANNED approach is KEDA scaling InventoryService on Service Bus queue depth — scale up when the
backlog grows, back down when it's empty. Node-level, I'd scale the AKS node pool since I'm currently
on a single node.

**33. What is KEDA and how would you use it here? (PLANNED)**
KEDA is Kubernetes Event-Driven Autoscaling — it scales workloads on external metrics that a normal
HPA can't see, like queue depth. I'd put a `ScaledObject` on InventoryService keyed to the
`inventory-subscription` backlog: scale from 1 to 5 pods when more than 10 messages are waiting, and
back to 1 when the subscription drains. That matches the metric that actually reflects a consumer's
load, unlike CPU.

**34. Walk me through a deployment from git push to running pod.**
Push to main triggers `ci.yml`, which builds all three services and the UI, lints and templates the
Helm charts, validates the Bicep, and scans for hardcoded secrets. On CI success, `cd-costopt.yml`
fires via `workflow_run`: it builds and pushes the three images to ACR tagged with the short SHA, then
the deploy job gets AKS credentials, re-applies the LB health-probe annotation, refreshes the
Kubernetes secrets, and runs `helm upgrade --install` for each service with the SHA tag and the
costopt values, waiting on rollout status. If anything fails, it `helm rollback`s.

**35. What is the Azure Load Balancer health-probe issue you hit and how did you fix it?**
This is my favourite war story. After a fresh deploy, everything inside the cluster was healthy — pods
Running, port-forward returned 200 — but hitting the public URL on port 80 timed out. The NGINX
ingress is fronted by an Azure Load Balancer, and the LB only sends traffic to backends its health
probe considers healthy. By default that probe did an HTTP GET on `/` of the NGINX controller, which
returns 404 because there's no route there. A 404 reads as unhealthy, so the LB pulled the node out
and silently dropped all external traffic — while internal traffic, which never touches the Azure LB,
kept working. The fix was a service annotation pointing the probe at `/healthz`, which returns 200. I
bake it into the infra install, every deploy, and my daily start routine, because a cluster stop/start
can reset it. The takeaway: "pods healthy" and "reachable from the internet" are different questions,
and the LB probe sits right on that line.

**36. What's the difference between ClusterIP and LoadBalancer service types?**
ClusterIP is internal-only — a stable in-cluster IP/DNS name, which is what my three services use;
they're only meant to be reached via the ingress. LoadBalancer provisions an actual cloud load
balancer with a public IP — that's the `ingress-nginx-controller` service, which holds my static IP
`104.211.94.129`. So external traffic hits the one LoadBalancer (NGINX), which then routes internally
to the ClusterIP services.

**37. How does Fluent Bit collect logs without any code change in services?**
The services just write structured JSON to stdout via Serilog. Kubernetes/containerd captures stdout
to files under `/var/log/containers`. Fluent Bit runs as a DaemonSet with those host paths mounted and
tails them — so it's collecting logs entirely out-of-band. The app doesn't know it exists. That
separation is the whole point: logging is the app's job, shipping is the platform's job.

**38. What is a DaemonSet and why is it the right choice for Fluent Bit?**
A DaemonSet guarantees one pod per node. Fluent Bit needs to read the container logs written to each
node's local disk, so it must have a presence on every node — a Deployment with a replica count
wouldn't guarantee coverage of all nodes. As I add nodes, the DaemonSet automatically puts a Fluent
Bit pod on each new one. It's the canonical pattern for node-level agents like log and metrics
collectors.

**39. How do you handle AKS node restarts?**
Kubernetes reschedules the pods when the node comes back, and the probes gate traffic until they're
ready. The thing I specifically watch for is that a stop/start can reset the Azure LB config, so my
start routine re-applies the `/healthz` probe annotation. State is safe because it all lives in Azure
PaaS — Cosmos and Service Bus — not on the node. PLANNED hardening would add a PodDisruptionBudget so
planned drains don't take a service fully offline.

**40. What is the static IP setup and why does it matter?**
The infra pipeline provisions a Standard static public IP in the AKS node resource group and assigns
it to the NGINX ingress LoadBalancer service. It matters because the URL — `104.211.94.129.nip.io` —
has to stay stable across cluster stop/start cycles. If I used the default ephemeral LB IP, it could
change on recreate and break every bookmarked URL, the CORS config, and the Angular environment. The
static IP plus nip.io gives me a stable, DNS-resolvable hostname for free.

---

## Section 4 — CI/CD Questions

**41. Walk me through your complete CI/CD pipeline from PR to deployment.**
On a PR to main, `ci.yml` runs seven jobs in parallel: build each of the three .NET services (with a
Docker build dry-run), build the Angular UI, lint and template the Helm charts, validate both Bicep
files, and a secret scan — all fanning into one required `ci-success` check. When code lands on main
and CI completes successfully, `cd-costopt.yml` triggers via `workflow_run`: it builds and pushes the
three images to ACR tagged with the short SHA, then deploys them to AKS with `helm upgrade --install`
against the costopt values, waits on rollout, and rolls back on failure. The UI deploys separately via
`cd-ui.yml` to GitHub Pages when `product-ui/**` changes.

**42. How do you ensure only tested code gets deployed?**
CD is gated on CI: `cd-costopt.yml` triggers on `workflow_run` of the CI workflow with
`types: [completed]`, and CI's single `ci-success` job depends on all seven checks passing. So an
image only builds and deploys after builds, Helm/Bicep validation, and the secret scan are green. I'm
candid that there's no automated *test suite* yet — CI validates that everything builds and is
structurally valid; unit/integration tests are PLANNED for Phase 5.

**43. What is Bicep and why did you choose it over Terraform?**
Bicep is Azure's first-party infrastructure-as-code language that transpiles to ARM. I chose it over
Terraform mainly because there's **no state file to store, lock, or corrupt** — Azure Resource Manager
is the source of truth. It also gets same-day support for new Azure resource types and is more concise
than raw ARM JSON. The trade-off is it's Azure-only, but this whole platform is Azure-native, so
multi-cloud portability isn't a requirement I'm paying for.

**44. How do you manage secrets in your pipeline without exposing them?**
Everything sensitive lives in GitHub Secrets — Azure credentials, ACR login, the JWT key, Cosmos
endpoint/key, Service Bus connection string. The pipeline reads them to authenticate and to plant
Kubernetes Secrets, and GitHub masks them in logs. The committed code has only empty placeholders, and
CI's secret-scan job actually fails the build if it finds a hardcoded `AccountKey=`, `Password=`, or an
`azurewebsites.net` URL. So there are two layers: secrets are injected, and hardcoded ones are blocked.

**45. What happens if a deployment fails? How does rollback work?**
The deploy job runs each `helm upgrade --install` with `--wait`, so Helm blocks until the pods are
actually ready or it times out. If any step fails, a `Rollback on failure` step runs `helm rollback`
for each release, reverting to the last good revision. Because every upgrade is a versioned Helm
release, rollback is deterministic — and I can also do it manually with `helm rollback <release>
<revision> -n bookstore`.

**46. What's the difference between your infra pipeline and your app pipeline?**
Different lifecycles and triggers. The infra pipeline (`infra-bicep.yml`) runs only when
`infrastructure/bicep/**` changes — it creates the resource group, deploys the ACR/AKS/Service
Bus/Cosmos/Key Vault, provisions the static IP, and installs NGINX and cert-manager. That's rare,
foundational, slow. The app pipeline (`cd-costopt.yml`) runs on every code change — it just builds
images and `helm upgrade`s. Separating them means routine deploys don't risk re-running infrastructure
provisioning.

**47. How do you handle database migrations in this setup?**
With Cosmos, there's no schema migration in the SQL sense — it's schema-flexible, so adding a field to
a document is just code. The containers themselves (`Products`, `Inventory`, partition key `/id`) are
declared in Bicep and created idempotently by the infra pipeline. If I later moved to SQL Server (a
one-layer change behind `IProductRepository`), I'd add EF Core migrations run as an init container or a
pipeline step — but that's not needed today.

**48. What is CodeRabbit and how does it fit in your workflow?**
CodeRabbit is an AI code-review GitHub App. It's installed directly on the repo — no CI job or webhook
to maintain — and it automatically reviews every PR when it's opened or updated, posting
style/correctness/diff-risk comments. There's no `.coderabbit.yaml`, so it runs under the default
profile. It's a first-pass reviewer that catches things before a human looks, which is useful on a
solo project where I don't have a teammate to review every change.

**49. How would you add a new environment (staging) to this pipeline? (PLANNED)**
I'd add a `values-staging.yaml` overlay and a staging namespace, then a deploy job that runs against a
staging AKS (or the same cluster, separate namespace) before promoting to production — gated on a
manual approval. The image is already tagged by SHA, so staging and prod deploy the *same* artifact;
only the values overlay and secrets differ. That's the strength of the profile-overlay model I already
use — a new environment is another overlay, not a rebuild.

**50. What is the two-profile deployment pattern and why is it useful?**
It's using one set of images with two Helm value overlays to get two very different runtime shapes.
Profile A is the always-on, cost-optimised setup — NGINX ingress, GitHub Models — deployed on every
push. Profile B is the enterprise demo — APIM in front, Azure OpenAI — provisioned manually and torn
down after four hours. It's useful because I can keep the monthly bill tiny while still being able to
stand up and demo the "enterprise gateway" story on demand, without maintaining two codebases or two
image sets.

---

## Section 5 — Observability Questions

**51. How do you debug a production issue in this system?**
I start in Splunk. If the user gives me a CorrelationId or a rough time and action, I find the error
line — `Level="Error"` filtered to the service and time window — grab its CorrelationId, and then
search on that id alone. That returns the *entire* transaction across all services, including the
InventoryService message processing, because I thread CorrelationId through Service Bus. If I need the
technical spans of one request I pivot on TraceId. If Splunk is missing very recent events, I fall
back to `kubectl logs`, including the Fluent Bit pod to check the shipper itself.

**52. What's the difference between a TraceId and a CorrelationId? When do you use each?**
TraceId comes from OpenTelemetry — the runtime assigns it per request, and it's great for correlating
the spans *within* one service's request. But in my setup it doesn't propagate across the Service Bus
hop, so it's effectively per-service. CorrelationId is the business id — the Angular client generates
it, and I deliberately carry it through the middleware, the LogContext, the Service Bus message
properties, and back out in the consumer. So: **CorrelationId** to follow a whole business transaction
end-to-end including async; **TraceId** to zoom into one service's technical spans. In Splunk I reach
for CorrelationId first.

**53. How does a log line from ProductService end up searchable in Splunk?**
ProductService writes it as a single JSON object to stdout via Serilog's JsonFormatter. containerd
captures stdout to `/var/log/containers/*_bookstore_*.log` in the CRI format. Fluent Bit's DaemonSet
tails that file with the `cri_bookstore` parser, enriches it with Kubernetes metadata, parses the
Serilog JSON inside the message into real fields, lifts the nested `Properties` (CorrelationId,
TraceId, Application) to the top level, and ships it over HEC to Splunk Cloud into `index=main`,
`sourcetype=bookstore:json`. Because it arrives as JSON, every field is directly searchable — no
`spath` needed.

**54. If a customer reports an error, walk me through how you find it.**
Get the CorrelationId if the UI showed it; otherwise the time and what they did. Search
`index=main sourcetype="bookstore:json" Level="Error"` around that window, identify the matching line,
and read its CorrelationId. Then `CorrelationId="<that id>" | sort by _time` to replay the full
sequence — the ProductService request that failed and any InventoryService processing tied to it. From
the message template and the exception I know whether it's a Cosmos issue, a publish failure, or a
validation problem, and I act from there.

**55. What is OpenTelemetry and why use it instead of just logging?**
OpenTelemetry is a vendor-neutral standard for traces, metrics, and logs. I use its tracing
instrumentation so every request gets a real TraceId and SpanId that enrich my logs — that's the
structured backbone that logging alone doesn't give you. Logs tell you *what happened at a point*;
traces tell you *how a request flowed and where the time went*. I'm honest that today I create spans
but don't export them to a backend — that's a deliberate choice to keep stdout clean JSON for Splunk,
and wiring an OTLP exporter to App Insights is PLANNED.

**56. What is a Span and how does it relate to a Trace?**
A Trace is the whole journey of one logical operation, identified by a TraceId. A Span is one unit of
work inside that trace — an operation with a name, a start and end time, a status, and tags, plus its
own SpanId and a parent SpanId. So a Trace is a tree of Spans. In BookStore, the ASP.NET Core
instrumentation creates the root request span, and my CorrelationIdMiddleware tags it with
`correlation.id` and `bookstore.service`. A Cosmos call or a Service Bus send would each be child spans
once fully exported.

**57. How does CorrelationId flow through Azure Service Bus messages?**
On the producer side, `AzureServiceBusProducer` reads the CorrelationId from `HttpContext.Items` and
sets it on `message.ApplicationProperties["CorrelationId"]` before sending — so the id travels *on*
the message, not just in the HTTP request. On the consumer side,
`AzureServiceBusSubscriber.ProcessMessageAsync` reads it back out of `ApplicationProperties` and pushes
it into the Serilog LogContext before doing any work. So every InventoryService log line for that
message carries the same id as the original ProductService request. That's the mechanism that makes
the async hop traceable.

**58. What is the CRI parser and why does it matter for AKS?**
AKS uses containerd as its runtime, and containerd writes log lines in the CRI format —
`timestamp stream logtag message` — not the Docker JSON format. If I used Fluent Bit's Docker parser,
it would fail to parse every line and I'd lose my logs. So I define a custom `cri_bookstore` regex
parser that splits the CRI wrapper and extracts the message, then a second parser reads the Serilog
JSON inside. Picking the parser that matches your runtime is a real production gotcha — Docker vs
containerd is exactly where people lose logs on AKS.

**59. How would you set up an alert for when a service goes down?**
In Splunk I'd create a scheduled alert that runs every few minutes over the last 10 minutes, counts
events by `Application`, and fills in zeros for any of the three services with no logs — then triggers
if any service's total is zero. No logs for ten minutes almost certainly means the service crashed or
the log pipeline broke, both of which I want to know about. I'd pair that with an error-rate alert
that fires when errors exceed five per minute. Both are written out in `docs/SPLUNK_GUIDE.md`.

**60. What would you add to observability in Phase 4? (PLANNED)**
The big one is an OpenTelemetry OTLP exporter to Azure Application Insights, so I get a real
distributed-trace waterfall UI and true cross-service TraceId propagation instead of stitching with
CorrelationId. I'd also add request-duration logging — a `DurationMs` field via
`UseSerilogRequestLogging()` — so my "slow requests" Splunk search actually returns data, since
nothing emits that field today. And I'd build the Splunk monitoring dashboard and the down-detection
and error-rate alerts into the standard deploy so observability ships with the platform, not after it.
