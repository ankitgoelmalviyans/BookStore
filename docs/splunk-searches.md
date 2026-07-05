# BookStore Splunk Searches

## Find all logs for a specific request

```text
index=main sourcetype="bookstore:json" CorrelationId="YOUR_CORRELATION_ID"
| sort by _time
```

## End-to-end trace across all services

```text
index=main sourcetype="bookstore:json" TraceId="YOUR_TRACE_ID"
| table _time, Application, Level, Message, SpanId, CorrelationId
| sort by _time
```

## All errors across all BookStore services

```text
index=main sourcetype="bookstore:json" Level="Error"
| table _time, Application, Message, CorrelationId, TraceId
| sort by _time desc
```

## Service-specific logs

```text
index=main sourcetype="bookstore:json" Application="BookStore.ProductService"
| table _time, Level, Message, CorrelationId
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
index=main sourcetype="bookstore:json"
SpanOperationName=* DurationMs>1000
| table _time, Application, SpanOperationName, DurationMs, CorrelationId
| sort by DurationMs desc
```

> **Note:** the "Slow requests" search assumes a `DurationMs` field. Nothing in the current OpenTelemetry/Serilog setup emits that field yet — `Serilog.Enrichers.Span` only adds `TraceId`, `SpanId`, and (with `IncludeOperationName`) `SpanOperationName`. To make this search return results, add request-duration logging (e.g. Serilog's `UseSerilogRequestLogging()` middleware, or a custom enricher) that writes a `DurationMs` property.
