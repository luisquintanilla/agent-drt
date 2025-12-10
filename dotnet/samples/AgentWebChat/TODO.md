# AgentWebChat - Observability & Monitoring Implementation

This document tracks the implementation of comprehensive observability, tracing, and monitoring capabilities for the AgentWebChat sample. The goal is to provide deep visibility into workflow execution, worker health, and system performance through structured tracing, a monitoring API, and a real-time dashboard.

## Architecture Overview

```
+-------------------+     HTTP/SSE      +------------------+     HTTP/SSE      +-------------------+
|                   | ---------------->  |                  | ---------------->  |                   |
|  MonitorDashboard |                   |   AgentGateway   |                   |    AgentHost(s)   |
|     (React)       | <-----------------|   (Orleans)      | <-----------------|   (Workflows)     |
|                   |   SSE Events      |                  |   State Callbacks |                   |
+-------------------+                   +------------------+                   +-------------------+
        |                                       |                                       |
        |                               +-------+-------+                               |
        |                               |               |                               |
        +-------> Monitoring API <------+   Tracing     +-----> OpenTelemetry <---------+
                  /v1/monitor/*         |   & Metrics   |       OTLP Export
                                        +---------------+
```

**Key Components:**
1. **Tracing & Observability** - OpenTelemetry-based distributed tracing across Gateway and Workers
2. **Monitoring API** - REST/SSE API on Gateway for system-wide monitoring
3. **MonitorDashboard** - React-based real-time monitoring UI

---

## Phase 1: Extensive Tracing & Observability

### 1.1 Create Tracing Infrastructure

**Location:** `AgentContracts/Telemetry/`

- [ ] Create `WorkflowActivitySource.cs` - Central ActivitySource for workflow tracing
  - Define activity source name: `AgentWebChat.Workflows`
  - Create span factory methods for common operations
  - Define semantic conventions for workflow attributes

- [ ] Create `TelemetryConstants.cs` - Standardized tag/attribute names
  - `workflow.run_id`, `workflow.name`, `workflow.status`
  - `workflow.step.name`, `workflow.step.index`
  - `worker.id`, `worker.address`
  - `signal.type`, `signal.request_id`

- [ ] Create `WorkflowMetrics.cs` - Metrics definitions using System.Diagnostics.Metrics
  - `workflow.runs.active` (UpDownCounter)
  - `workflow.runs.total` (Counter) with status tags
  - `workflow.run.duration` (Histogram)
  - `workflow.step.duration` (Histogram)
  - `workflow.signals.pending` (UpDownCounter)
  - `worker.dispatches.total` (Counter)
  - `worker.dispatches.failed` (Counter)

### 1.2 AgentGateway Tracing

**Location:** `AgentGateway/`

- [ ] Update `WorkflowGrain.cs` - Add activity spans
  - Span for `StartWorkflowAsync` with workflow metadata
  - Span for `UpdateStatusAsync` with status transition info
  - Span for `RecordStepStartedAsync` / `RecordStepCompletedAsync`
  - Span for `SendSignalAsync` with signal details
  - Span for `SaveCheckpointAsync` / `GetCheckpointAsync`
  - Add workflow context tags to all spans
  - Record exceptions as span events

- [ ] Update `WorkflowIndexGrain.cs` - Add activity spans
  - Span for `ListWorkflowsAsync` with filter parameters
  - Span for index operations (add/update/remove)

- [ ] Update `WorkerWorkflowExecutor.cs` - Add activity spans
  - Span for `DispatchExecutionAsync` with worker selection details
  - Span for `DispatchResumeAsync`
  - Link parent trace context to worker request
  - Record HTTP response details

- [ ] Update `WorkflowHttpApi.cs` - Add activity spans
  - Span for each HTTP endpoint with request details
  - Propagate trace context in responses
  - Record validation failures

- [ ] Update `WorkerDiscoveryCache.cs` - Add activity spans
  - Span for discovery refresh operations
  - Record discovered entities count

- [ ] Update `WorkerHealthCheckService.cs` - Add activity spans
  - Span for health check cycles
  - Record healthy/unhealthy worker counts

- [ ] Create `Telemetry/GatewayTelemetryExtensions.cs`
  - Extension method `AddGatewayTelemetry(this IServiceCollection)`
  - Register ActivitySource and Meters
  - Configure OpenTelemetry tracing and metrics

### 1.3 AgentHost Tracing

**Location:** `AgentWebChat.AgentHost/`

- [ ] Update `WorkflowHostService.cs` - Add activity spans
  - Span for `ExecuteAsync` covering entire workflow execution
  - Span for `ResumeAsync` with checkpoint details
  - Child spans for each workflow step
  - Record workflow input/output sizes
  - Capture exceptions with full context

- [ ] Update `GatewayWorkflowStateClient.cs` - Add activity spans
  - Span for each state callback HTTP request
  - Record request/response details
  - Link to parent workflow span

- [ ] Update `MarketingContentWorkflow.cs` - Add activity spans
  - Span for each agent invocation (Writer, Reviewer)
  - Span for human approval wait
  - Record content generation details

- [ ] Update `WorkflowHttpApi.cs` (AgentHost) - Add activity spans
  - Span for execute/resume endpoints
  - Extract and continue trace context from Gateway

- [ ] Create `Telemetry/HostTelemetryExtensions.cs`
  - Extension method `AddHostTelemetry(this IServiceCollection)`
  - Register ActivitySource and Meters
  - Configure OpenTelemetry tracing and metrics

### 1.4 Structured Logging Enhancement

**Location:** `AgentGateway/` and `AgentWebChat.AgentHost/`

- [ ] Create `LoggerMessageDefinitions.cs` in each project
  - Use `[LoggerMessage]` source generators for high-performance logging
  - Define log messages for all workflow lifecycle events
  - Include structured parameters (runId, workflowName, status, etc.)

- [ ] Update all services to use LoggerMessage definitions
  - Replace `logger.LogXxx()` calls with source-generated methods
  - Ensure consistent log levels across projects
  - Add correlation IDs to all log messages

---

## Phase 2: Monitoring HTTP API

### 2.1 Monitoring Data Models

**Location:** `AgentContracts/Monitoring/`

- [ ] Create `MonitoringModels.cs` - Data transfer objects
  ```csharp
  // System overview
  record SystemStatus(
      DateTimeOffset Timestamp,
      int ActiveWorkflows,
      int QueuedWorkflows,
      int CompletedWorkflows24h,
      int FailedWorkflows24h,
      int RegisteredWorkers,
      int HealthyWorkers,
      TimeSpan Uptime);
  
  // Worker details
  record WorkerStatus(
      string WorkerId,
      string Address,
      WorkerHealthState Health,
      DateTimeOffset LastHealthCheck,
      int ActiveWorkflows,
      IReadOnlyList<string> SupportedWorkflows,
      IReadOnlyList<string> SupportedAgents,
      WorkerMetrics Metrics);
  
  record WorkerMetrics(
      long TotalWorkflowsExecuted,
      long TotalWorkflowsFailed,
      double AvgExecutionTimeMs,
      double CpuUsage,
      long MemoryUsageMB);
  
  // Workflow summary for monitoring
  record WorkflowMonitoringSummary(
      string RunId,
      string WorkflowName,
      WorkflowStatus Status,
      DateTimeOffset CreatedAt,
      DateTimeOffset? StartedAt,
      DateTimeOffset? CompletedAt,
      TimeSpan? Duration,
      string? AssignedWorker,
      int StepCount,
      int CompletedSteps,
      string? CurrentStep,
      bool HasPendingSignal);
  
  // Real-time events for SSE
  record MonitoringEvent(
      string EventType,  // "workflow.started", "workflow.completed", "worker.registered", etc.
      DateTimeOffset Timestamp,
      object Payload);
  ```

- [ ] Create `MonitoringInterfaces.cs` - Service contracts
  ```csharp
  interface IMonitoringService
  {
      Task<SystemStatus> GetSystemStatusAsync(CancellationToken ct);
      Task<IReadOnlyList<WorkerStatus>> GetWorkersAsync(CancellationToken ct);
      Task<WorkerStatus?> GetWorkerAsync(string workerId, CancellationToken ct);
      Task<IReadOnlyList<WorkflowMonitoringSummary>> GetActiveWorkflowsAsync(CancellationToken ct);
      Task<IReadOnlyList<WorkflowMonitoringSummary>> GetRecentWorkflowsAsync(int count, CancellationToken ct);
      Task<WorkflowMetricsSnapshot> GetWorkflowMetricsAsync(TimeSpan window, CancellationToken ct);
      IAsyncEnumerable<MonitoringEvent> StreamEventsAsync(CancellationToken ct);
  }
  ```

### 2.2 Monitoring Service Implementation

**Location:** `AgentGateway/Monitoring/`

- [ ] Create `MonitoringService.cs` - Implements `IMonitoringService`
  - Aggregate data from WorkerRegistry, WorkflowIndexGrain
  - Compute real-time metrics from Orleans grains
  - Maintain event stream channel for SSE
  - Subscribe to workflow/worker state changes

- [ ] Create `MonitoringEventBroadcaster.cs` - Event distribution
  - Singleton service for broadcasting monitoring events
  - Channel-based pub/sub pattern
  - Event types: workflow lifecycle, worker registration, health changes, errors

- [ ] Update `WorkflowGrain.cs` - Publish monitoring events
  - Inject `IMonitoringEventBroadcaster`
  - Publish events on status changes
  - Publish events on signal requests

- [ ] Update `WorkerRegistry.cs` - Publish monitoring events
  - Publish events on worker registration/deregistration
  - Publish events on health state changes

### 2.3 Monitoring HTTP Endpoints

**Location:** `AgentGateway/Monitoring/MonitoringHttpApi.cs`

- [ ] Create `MonitoringHttpApi.cs` - Minimal API endpoints
  ```
  GET  /v1/monitor/status           - System status overview
  GET  /v1/monitor/workers          - List all workers with status
  GET  /v1/monitor/workers/{id}     - Get specific worker details
  GET  /v1/monitor/workflows/active - List active workflows
  GET  /v1/monitor/workflows/recent - List recent workflows (last N)
  GET  /v1/monitor/metrics          - Aggregated metrics snapshot
  GET  /v1/monitor/events           - SSE stream of monitoring events
  POST /v1/monitor/workers/{id}/drain - Drain worker (stop accepting new work)
  POST /v1/monitor/workers/{id}/enable - Re-enable drained worker
  ```

- [ ] Add OpenAPI documentation for all endpoints
  - Use `.WithName()`, `.WithDescription()`, `.WithTags()`
  - Document response types with `.Produces<T>()`
  - Document error responses with `.ProducesProblem()`

- [ ] Map endpoints in `Program.cs`
  - Add `app.MapMonitoring()` call
  - Configure CORS for dashboard access

### 2.4 Gateway Program.cs Updates

**Location:** `AgentGateway/Program.cs`

- [ ] Register monitoring services
  - Add `builder.Services.AddMonitoringServices()`
  - Register `IMonitoringService`, `IMonitoringEventBroadcaster`

- [ ] Configure CORS policy for MonitorDashboard
  - Allow dashboard origin in development
  - Configure SSE-friendly headers

- [ ] Map monitoring endpoints
  - Add `app.MapMonitoring()` after other endpoints

---

## Phase 3: MonitorDashboard React Application

### 3.1 Project Setup

**Location:** `MonitorDashboard/`

- [ ] Initialize Vite + React + TypeScript project
  ```bash
  npm create vite@latest MonitorDashboard -- --template react-ts
  ```

- [ ] Configure project structure
  ```
  MonitorDashboard/
  ├── public/
  ├── src/
  │   ├── api/              # API clients
  │   ├── components/       # UI components
  │   │   ├── charts/      # Chart components
  │   │   ├── layout/      # Layout components
  │   │   └── widgets/     # Dashboard widgets
  │   ├── hooks/           # Custom React hooks
  │   ├── types/           # TypeScript types
  │   ├── utils/           # Utility functions
  │   ├── App.tsx
  │   ├── App.css
  │   └── main.tsx
  ├── index.html
  ├── package.json
  ├── tsconfig.json
  └── vite.config.ts
  ```

- [ ] Configure Vite
  - Set up path aliases (`@/` -> `src/`)
  - Configure API proxy to Gateway (`/v1/monitor/*`)
  - Set up environment variables

- [ ] Install dependencies
  - `recharts` - Charting library
  - `date-fns` - Date formatting
  - `clsx` - Conditional classnames

### 3.2 TypeScript Types

**Location:** `MonitorDashboard/src/types/`

- [ ] Create `monitoring.ts` - Type definitions matching C# models
  - `SystemStatus`, `WorkerStatus`, `WorkerMetrics`
  - `WorkflowMonitoringSummary`, `MonitoringEvent`
  - `WorkerHealthState` enum
  - `WorkflowStatus` enum

- [ ] Create `api.ts` - API request/response types

### 3.3 API Client Layer

**Location:** `MonitorDashboard/src/api/`

- [ ] Create `monitoringApi.ts` - REST API client
  - `getSystemStatus()` - Fetch system overview
  - `getWorkers()` - Fetch worker list
  - `getWorker(id)` - Fetch worker details
  - `getActiveWorkflows()` - Fetch active workflows
  - `getRecentWorkflows(count)` - Fetch recent workflows
  - `getMetrics()` - Fetch metrics snapshot
  - `drainWorker(id)` - Drain a worker
  - `enableWorker(id)` - Re-enable a worker

- [ ] Create `useMonitoringEvents.ts` - SSE hook
  - Connect to `/v1/monitor/events`
  - Handle reconnection with backoff
  - Parse and dispatch monitoring events
  - Return connection status

### 3.4 Dashboard Components

**Location:** `MonitorDashboard/src/components/`

#### Layout Components (`layout/`)

- [ ] Create `DashboardLayout.tsx` - Main layout wrapper
  - Header with title and connection status
  - Sidebar navigation (if needed)
  - Main content area with grid

- [ ] Create `Header.tsx` - Dashboard header
  - System name/logo
  - Real-time connection indicator
  - Last update timestamp
  - Refresh button

#### Widget Components (`widgets/`)

- [ ] Create `SystemStatusWidget.tsx` - System overview card
  - Uptime display
  - Active/Queued/Completed/Failed workflow counts
  - Worker count (healthy/total)
  - Visual status indicators

- [ ] Create `WorkerListWidget.tsx` - Workers overview
  - Table/grid of workers
  - Health status badge (Healthy/Unhealthy/Unknown)
  - Active workflows per worker
  - Supported workflows/agents
  - Actions (Drain/Enable)

- [ ] Create `WorkerDetailWidget.tsx` - Single worker details
  - Full worker metrics
  - Recent workflow history
  - Performance charts

- [ ] Create `ActiveWorkflowsWidget.tsx` - Running workflows
  - List of active workflows with progress
  - Status badge, current step, duration
  - Assigned worker
  - Click to view details

- [ ] Create `RecentWorkflowsWidget.tsx` - Recent workflow history
  - Tabular list with sorting
  - Status, duration, timestamps
  - Filter by status
  - Pagination or infinite scroll

- [ ] Create `WorkflowTimelineWidget.tsx` - Visual timeline
  - Gantt-style view of workflow steps
  - Color-coded by status
  - Time scale

- [ ] Create `EventFeedWidget.tsx` - Real-time event stream
  - Scrolling list of recent events
  - Event type icon and description
  - Timestamp
  - Auto-scroll with pause on hover

#### Chart Components (`charts/`)

- [ ] Create `WorkflowStatusChart.tsx` - Pie/donut chart
  - Breakdown by status (Running, Completed, Failed, Waiting)
  - Interactive legend

- [ ] Create `WorkflowThroughputChart.tsx` - Line/area chart
  - Workflows started/completed over time
  - Configurable time window (1h, 6h, 24h)

- [ ] Create `WorkerLoadChart.tsx` - Bar chart
  - Active workflows per worker
  - Visual load distribution

- [ ] Create `LatencyHistogramChart.tsx` - Histogram
  - Workflow duration distribution
  - P50, P95, P99 markers

#### Common Components

- [ ] Create `StatusBadge.tsx` - Reusable status badge
  - Support workflow status colors
  - Support worker health colors

- [ ] Create `ConnectionIndicator.tsx` - SSE connection status
  - Connected/Disconnected/Reconnecting states
  - Visual pulse animation

- [ ] Create `RefreshButton.tsx` - Manual refresh trigger
  - Spin animation while refreshing
  - Debounced clicks

- [ ] Create `TimeAgo.tsx` - Relative time display
  - "2 minutes ago" format
  - Auto-update

### 3.5 Custom Hooks

**Location:** `MonitorDashboard/src/hooks/`

- [ ] Create `useSystemStatus.ts` - System status polling
  - Fetch on mount and interval
  - Merge with SSE updates
  - Return status, loading, error

- [ ] Create `useWorkers.ts` - Workers data hook
  - Fetch and cache worker list
  - Update on SSE events
  - Provide worker actions (drain/enable)

- [ ] Create `useWorkflows.ts` - Workflows data hook
  - Active and recent workflows
  - Filtering and sorting
  - SSE updates

- [ ] Create `useMetrics.ts` - Metrics data hook
  - Periodic metrics refresh
  - Time window selection
  - Historical data accumulation

- [ ] Create `useInterval.ts` - Polling helper
  - Configurable interval
  - Pause/resume

### 3.6 Main Application

**Location:** `MonitorDashboard/src/`

- [ ] Implement `App.tsx` - Main dashboard view
  - Grid layout with widgets
  - SSE event subscription
  - Global state coordination

- [ ] Create `App.css` - Dashboard styles
  - Dark theme (monitoring-friendly)
  - Grid layout system
  - Widget card styles
  - Responsive breakpoints

- [ ] Create `index.css` - Global styles
  - CSS reset
  - CSS variables (colors, spacing)
  - Typography

### 3.7 Aspire Integration

**Location:** `AgentWebChat.AppHost/`

- [ ] Add MonitorDashboard as Aspire resource
  - Configure as Node.js/npm resource
  - Set environment variables (Gateway URL)
  - Configure service discovery

- [ ] Update `Program.cs` to include MonitorDashboard
  - Add reference to Gateway for API proxy
  - Configure port allocation

---

## Phase 4: Integration & Testing

### 4.1 End-to-End Integration

- [ ] Verify trace context propagation Gateway -> Worker
- [ ] Verify SSE events flow from services to dashboard
- [ ] Test reconnection behavior for SSE streams
- [ ] Validate metrics accuracy against actual workflow counts

### 4.2 Manual Testing Checklist

1. [ ] Start system via Aspire
2. [ ] Open MonitorDashboard
3. [ ] Verify system status displays correctly
4. [ ] Verify worker list shows registered workers
5. [ ] Start a workflow via MarketR
6. [ ] Verify workflow appears in active workflows widget
7. [ ] Verify real-time event feed shows workflow events
8. [ ] Verify status transitions update in real-time
9. [ ] Complete workflow and verify it moves to recent
10. [ ] Drain a worker and verify status updates
11. [ ] View traces in Aspire dashboard / OTLP collector

### 4.3 Unit Tests

**Location:** `AgentGateway.UnitTests/Monitoring/`

- [ ] `MonitoringServiceTests.cs` - Service behavior tests
- [ ] `MonitoringHttpApiTests.cs` - HTTP endpoint tests
- [ ] `MonitoringEventBroadcasterTests.cs` - Event distribution tests

---

## File Reference

| Component | Key Files |
|-----------|-----------|
| Telemetry Contracts | `AgentContracts/Telemetry/*.cs` |
| Monitoring Contracts | `AgentContracts/Monitoring/*.cs` |
| Gateway Telemetry | `AgentGateway/Telemetry/*.cs` |
| Gateway Monitoring | `AgentGateway/Monitoring/*.cs` |
| Host Telemetry | `AgentWebChat.AgentHost/Telemetry/*.cs` |
| Dashboard API | `MonitorDashboard/src/api/*.ts` |
| Dashboard Components | `MonitorDashboard/src/components/**/*.tsx` |
| Dashboard Hooks | `MonitorDashboard/src/hooks/*.ts` |

---

## Dependencies to Add

### AgentGateway.csproj
```xml
<PackageReference Include="System.Diagnostics.DiagnosticSource" Version="9.0.0" />
```

### AgentWebChat.AgentHost.csproj
```xml
<PackageReference Include="System.Diagnostics.DiagnosticSource" Version="9.0.0" />
```

### MonitorDashboard/package.json
```json
{
  "dependencies": {
    "react": "^19.1.0",
    "react-dom": "^19.1.0",
    "recharts": "^2.15.0",
    "date-fns": "^4.1.0",
    "clsx": "^2.1.1"
  },
  "devDependencies": {
    "@types/react": "^19.1.0",
    "@types/react-dom": "^19.1.0",
    "@vitejs/plugin-react": "^4.4.0",
    "typescript": "~5.8.0",
    "vite": "^6.3.0"
  }
}
```

---

## Priority Order

1. **Phase 1.1-1.3** - Tracing infrastructure (enables debugging)
2. **Phase 2.1-2.3** - Monitoring API (enables any client)
3. **Phase 3.1-3.3** - Dashboard setup and API client
4. **Phase 3.4-3.6** - Dashboard UI components
5. **Phase 1.4** - Structured logging enhancement
6. **Phase 3.7** - Aspire integration
7. **Phase 4** - Testing and validation

---

*Last updated: December 2024*
