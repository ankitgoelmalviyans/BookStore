# Splunk Observability Guide — BookStore

> Grounded in `infrastructure/helm/fluent-bit/values.yaml`, the Serilog config in each service, and
> the existing `docs/splunk-searches.md`. The Fluent Bit → Splunk pipeline **is deployed** (see
> `cd-costopt.yml`), tailing only the three BookStore containers.

---

## Architecture

```text
App (Serilog JsonFormatter → stdout, one JSON object per line)
  → containerd writes /var/log/containers/<pod>_bookstore_<container>.log   (CRI-wrapped)
  → Fluent Bit DaemonSet (one pod per node):
        INPUT tail  Path /var/log/containers/*_bookstore_*.log  Parser cri_bookstore
        FILTER kubernetes  (adds pod_name, namespace_name, container_name, host)
        FILTER grep exclude fluent-bit
        FILTER grep keep ^(authservice|productservice|inventoryservice)$
        FILTER parser json_serilog  (Serilog JSON inside "message" → real fields)
        FILTER nest lift Properties (CorrelationId/TraceId/Application → top level)
        FILTER modify  (+ environment=Production, + platform=BookStore-AKS)
  → OUTPUT splunk  Host prd-p-opur1.splunkcloud.com  Port 8088  TLS On
        Event_Index main   Event_Sourcetype bookstore:json
```
Splunk index: `main`; sourcetype: `bookstore:json`. The whole record ships as JSON, so **every field
is a real, top-level searchable field — no `spath` needed.**

---

## Why Fluent Bit
- **Lightweight DaemonSet** (requests 50m CPU / 64Mi) — one pod per node, critical on a single B2s.
- **Zero code change** in the services — they just write JSON to stdout; Fluent Bit does the rest.
- **CRI parser** handles the AKS/containerd log format natively.
- **Kubernetes filter** enriches each line with pod/namespace/container metadata from the API server.

## Why the CRI parser, not the Docker parser
AKS runs **containerd**, not Docker, as its container runtime. containerd (CRI) writes log lines as:
```text
2026-07-05T12:29:25.460+00:00 stdout F {"Timestamp":"...","Level":"Information",...}
└──────── time ────────────┘ stream tag └────────── the actual app log ──────────┘
```
The **Docker** JSON parser expects `{"log":"...","stream":"...","time":"..."}` and would **fail** on
the CRI space-delimited format — dropping or mangling every line. So we define a custom `cri_bookstore`
regex parser:
```text
Regex  ^(?<time>[^ ]+) (?<stream>stdout|stderr) (?<logtag>[^ ]*) (?<message>.*)$
```
This is a **real production gotcha**: pick the parser that matches your runtime, or you get no logs.

---

## Log Structure

A single BookStore event in Splunk, after Fluent Bit processing, has both Serilog fields and
Kubernetes metadata as top-level fields:

```json
{
  "_time": "2026-07-05T12:29:25.460+00:00",
  "Timestamp": "2026-07-05T12:29:25.46+00:00",
  "Level": "Information",
  "MessageTemplate": "Received ProductCreatedEvent: {Name} - Qty: {Quantity}",
  "Name": "The Pragmatic Programmer",
  "Quantity": 12,
  "Application": "BookStore.InventoryService",
  "CorrelationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "TraceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "SpanId": "00f067aa0ba902b7",
  "OperationName": "Microsoft.AspNetCore.Hosting.HttpRequestIn",
  "MachineName": "productservice-6d8f7c9b4-abcde",
  "ThreadId": 14,
  "kubernetes": {
    "pod_name": "inventoryservice-7c5b6d8f9-xyz12",
    "namespace_name": "bookstore",
    "container_name": "inventoryservice",
    "host": "aks-nodepool1-...."
  },
  "environment": "Production",
  "platform": "BookStore-AKS",
  "message": "{...the original raw Serilog JSON string...}"
}
```
- **Serilog fields:** `Timestamp`, `Level`, `MessageTemplate`, `Application`, `CorrelationId`,
  `TraceId`, `SpanId`, `OperationName`, `MachineName`, `ThreadId`.
- **Message-template placeholders** become their own fields (e.g. `Name`, `Quantity`).
- **Kubernetes metadata (added by Fluent Bit):** `kubernetes.pod_name`, `kubernetes.namespace_name`,
  `kubernetes.container_name`, `kubernetes.host`.
- **Fluent Bit modify:** `environment`, `platform`.

---

## Complete Splunk Search Reference (25)

```text
# 1. All BookStore logs
index=main sourcetype="bookstore:json"

# 2. AuthService only
index=main sourcetype="bookstore:json" Application="BookStore.AuthService"

# 3. ProductService only
index=main sourcetype="bookstore:json" Application="BookStore.ProductService"

# 4. InventoryService only
index=main sourcetype="bookstore:json" Application="BookStore.InventoryService"

# 5. Errors only, everywhere
index=main sourcetype="bookstore:json" Level="Error"
| table _time, Application, MessageTemplate, CorrelationId, TraceId | sort by _time desc

# 6. Warnings only
index=main sourcetype="bookstore:json" Level="Warning"

# 7. End-to-end by CorrelationId (business transaction across all services incl. async hop)
index=main sourcetype="bookstore:json" CorrelationId="YOUR_CORRELATION_ID" | sort by _time

# 8. Distributed trace by TraceId (spans within a service's request)
index=main sourcetype="bookstore:json" TraceId="YOUR_TRACE_ID"
| table _time, Application, Level, MessageTemplate, SpanId, CorrelationId | sort by _time

# 9. Error investigation with context — pull the CorrelationId of an error, then see everything for it
index=main sourcetype="bookstore:json" Level="Error" Application="BookStore.ProductService"
| table _time, MessageTemplate, CorrelationId

# 10. Service Bus consumer events (inventory processing)
index=main sourcetype="bookstore:json" Application="BookStore.InventoryService"
  MessageTemplate="Received ProductCreatedEvent*"

# 11. Service Bus consumer errors
index=main sourcetype="bookstore:json" Application="BookStore.InventoryService" Level="Error"
| sort by _time desc

# 12. Exclude health-probe noise
index=main sourcetype="bookstore:json" NOT RequestPath="/health"

# 13. Last 1 hour
index=main sourcetype="bookstore:json" earliest=-1h

# 14. Last 24 hours, errors
index=main sourcetype="bookstore:json" Level="Error" earliest=-24h

# 15. Count events by service
index=main sourcetype="bookstore:json" | stats count by Application | sort by count desc

# 16. Count events by level
index=main sourcetype="bookstore:json" | stats count by Level

# 17. Recent errors table
index=main sourcetype="bookstore:json" Level="Error"
| table _time, Application, MessageTemplate, CorrelationId | sort by _time desc | head 50

# 18. Exclude Fluent Bit's own logs (belt-and-braces; Fluent Bit already drops them)
index=main sourcetype="bookstore:json" NOT kubernetes.container_name="fluent-bit"

# 19. Filter by kubernetes.container_name
index=main sourcetype="bookstore:json" kubernetes.container_name="productservice"

# 20. Filter by a specific pod
index=main sourcetype="bookstore:json" kubernetes.pod_name="inventoryservice-7c5b6d8f9-xyz12"

# 21. Error rate over time (per-service timechart)
index=main sourcetype="bookstore:json" Level="Error"
| timechart span=5m count by Application

# 22. Request/log volume by hour
index=main sourcetype="bookstore:json" | timechart span=1h count by Application

# 23. Top message templates (what is the system mostly doing/saying?)
index=main sourcetype="bookstore:json" | stats count by MessageTemplate | sort by count desc

# 24. Product creations observed (producer side)
index=main sourcetype="bookstore:json" Application="BookStore.ProductService"
  MessageTemplate="Product created with ID: {ProductId}"
| table _time, ProductId, CorrelationId

# 25. Publish failures (the best-effort dual-write gap surfacing)
index=main sourcetype="bookstore:json"
  MessageTemplate="Failed to publish ProductCreatedEvent to Service Bus"
| table _time, Application, CorrelationId, TraceId

# 26. Slow requests (over 1 second) — DurationMs is emitted by RequestLoggingMiddleware
index=main sourcetype="bookstore:json" DurationMs>1000
| table _time, Application, RequestMethod, RequestPath, StatusCode, DurationMs, CorrelationId
| sort by DurationMs desc

# 27. Per-service average/95th-percentile latency
index=main sourcetype="bookstore:json" DurationMs=*
| stats avg(DurationMs) as avg_ms, p95(DurationMs) as p95_ms, count by Application
```
> Note: `DurationMs` is now emitted per request by `RequestLoggingMiddleware` in all three services
> (health probes excluded), so the duration searches above return real data. Fields available on the
> request-completion line: `RequestMethod`, `RequestPath`, `StatusCode`, `DurationMs` (+ the usual
> `CorrelationId`/`TraceId`/`Application`).

---

## How to Investigate a Production Issue

1. **User reports an error** ("I clicked Save and it failed at ~12:29"). Get their CorrelationId if
   the UI surfaced it, or the approximate time + action.
2. **Find the error:**
   ```text
   index=main sourcetype="bookstore:json" Level="Error" earliest=-15m
   | table _time, Application, MessageTemplate, CorrelationId, TraceId
   ```
3. **Pivot on CorrelationId** — take the `CorrelationId` from the error line and see the *whole*
   transaction, across services and the async hop:
   ```text
   index=main sourcetype="bookstore:json" CorrelationId="<that id>" | sort by _time
   ```
   This shows the ProductService request **and** the InventoryService message processing for the
   same action — the payoff of threading CorrelationId through Service Bus.
4. **Zoom in with TraceId** — for the technical spans of one service's request:
   ```text
   index=main sourcetype="bookstore:json" TraceId="<trace id>" | sort by _time
   ```
5. **If Splunk is missing events** (e.g. a very recent failure, or a suspected pipeline problem),
   fall back to pod logs directly:
   ```text
   kubectl logs -l app=productservice -n bookstore --tail=100
   kubectl logs -l app=inventoryservice -n bookstore --tail=100
   kubectl logs -l app.kubernetes.io/name=fluent-bit -n bookstore --tail=50   # is the shipper healthy?
   ```

---

## Splunk Dashboard Guide

Create a **BookStore Monitoring** dashboard with four panels:

- **Panel 1 — Events per service (bar chart)**
  ```text
  index=main sourcetype="bookstore:json" | stats count by Application
  ```
- **Panel 2 — Error rate over time (line chart)**
  ```text
  index=main sourcetype="bookstore:json" Level="Error" | timechart span=5m count by Application
  ```
- **Panel 3 — Recent errors (table)**
  ```text
  index=main sourcetype="bookstore:json" Level="Error"
  | table _time, Application, MessageTemplate, CorrelationId | sort by _time desc | head 25
  ```
- **Panel 4 — Request volume by hour (column chart)**
  ```text
  index=main sourcetype="bookstore:json" | timechart span=1h count by Application
  ```
Save each search as a dashboard panel; set the dashboard's time picker to default `Last 24 hours`.

---

## How to Set Up Alerts

- **Alert: error rate exceeds 5/min**
  ```text
  index=main sourcetype="bookstore:json" Level="Error"
  ```
  Save As → Alert; type **Scheduled**, run every 1 minute over the last 1 minute; **Trigger when
  number of results > 5**; action: email / webhook.

- **Alert: a service has gone silent (0 logs for 10 min = likely down)**
  ```text
  index=main sourcetype="bookstore:json" earliest=-10m
  | stats count by Application
  | append
      [| makeresults | eval Application="BookStore.AuthService", count=0
       | append [| makeresults | eval Application="BookStore.ProductService", count=0]
       | append [| makeresults | eval Application="BookStore.InventoryService", count=0]]
  | stats sum(count) as total by Application
  | where total=0
  ```
  Save As → Alert; run every 5 minutes; **Trigger when number of results > 0** (any service with a
  zero total appears → alert). This catches a crashed service or a broken log pipeline.
