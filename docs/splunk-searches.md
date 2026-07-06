# BookStore Splunk Searches

## How logs reach Splunk

```text
App (Serilog JSON to stdout)
  → containerd writes /var/log/containers/*_bookstore_*.log  (CRI-wrapped)
  → Fluent Bit DaemonSet:
       cri_bookstore parser   (splits the CRI wrapper, sets _time)
       kubernetes filter      (adds pod/namespace/labels metadata)
       grep x2                (drop fluent-bit's own logs; keep only the 3 services)
       parser json_serilog    (parses the Serilog JSON in "message" into fields)
       nest lift Properties   (moves Serilog Properties.* up to top level)
       modify                 (adds environment=Production, platform=BookStore-AKS)
  → Splunk HEC  (index=main, sourcetype=bookstore:json)
```

Because the whole record is shipped as JSON, every field below is a real, searchable
top-level field — no `spath` needed.

## Fields available for searching

| Field | Source | Example |
|---|---|---|
| `_time` | CRI timestamp | — |
| `Timestamp` | Serilog | `2026-07-05T12:29:25.46+00:00` |
| `Level` | Serilog | `Information`, `Warning`, `Error` |
| `MessageTemplate` | Serilog | `Received ProductCreatedEvent: {Name}` |
| `Application` | Serilog property | `BookStore.ProductService` |
| `CorrelationId` | CorrelationIdMiddleware | request-scoped GUID |
| `TraceId` / `SpanId` / `ParentId` | Serilog.Enrichers.Span | OpenTelemetry trace ids |
| `OperationName` | Serilog.Enrichers.Span | `Microsoft.AspNetCore.Hosting.HttpRequestIn` |
| `DurationMs`, `RequestMethod`, `RequestPath`, `StatusCode` | RequestLoggingMiddleware (per-request completion line) | `12.4`, `POST`, `/api/products`, `201` |
| `SourceContext`, `ThreadId`, `MachineName`, `EventId` | Serilog | — |
| `kubernetes.pod_name`, `kubernetes.container_name` | k8s filter | `productservice-...` |
| `environment`, `platform` | Fluent Bit modify | `Production`, `BookStore-AKS` |
| `message` | preserved raw | the original Serilog JSON string |

> Placeholder values from message templates also become their own fields — e.g. a log
> with template `Received ProductCreatedEvent: {Name}` gives you a searchable `Name` field.

## Find all logs for a specific request

```text
index=main sourcetype="bookstore:json" CorrelationId="YOUR_CORRELATION_ID"
| sort by _time
```

## End-to-end trace across all services

```text
index=main sourcetype="bookstore:json" TraceId="YOUR_TRACE_ID"
| table _time, Application, Level, MessageTemplate, SpanId, CorrelationId
| sort by _time
```

## All errors across all BookStore services

```text
index=main sourcetype="bookstore:json" Level="Error"
| table _time, Application, MessageTemplate, CorrelationId, TraceId
| sort by _time desc
```

## Service-specific logs

```text
index=main sourcetype="bookstore:json" Application="BookStore.ProductService"
| table _time, Level, MessageTemplate, CorrelationId
```

## Service Bus consumer errors

```text
index=main sourcetype="bookstore:json"
Application="BookStore.InventoryService" Level="Error"
| sort by _time desc
```

## Request rate per service (last 1 hour)

```text
index=main sourcetype="bookstore:json" earliest=-1h
| stats count by Application
| sort by count desc
```

## Slow requests (over 1 second)

```text
index=main sourcetype="bookstore:json" DurationMs>1000
| table _time, Application, RequestMethod, RequestPath, StatusCode, DurationMs, CorrelationId
| sort by DurationMs desc
```

> **Note:** `DurationMs` is now emitted per request by `RequestLoggingMiddleware` (added to all three
> services; health-probe requests are excluded). The completion line
> (`HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {DurationMs} ms`) also gives you
> `RequestMethod`, `RequestPath`, and `StatusCode` as searchable fields, alongside the usual
> `CorrelationId`/`TraceId`/`Application`.
