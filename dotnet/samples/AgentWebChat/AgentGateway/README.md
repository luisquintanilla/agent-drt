# Agent Gateway

A production-ready ingress gateway for AI agent applications that implements and multiple agentic protocols (OpenAI Responses API, OpenAI Conversations API, Agent-to-Agent (A2A), and more). The Agent Gateway acts as a scalable, stateful, and durable intermediary between clients and worker applications, handling the heavy lifting of protocol implementation while allowing applications to focus on agent behavior.

**Focus on building great agents. Let the gateway handle the protocols.**

Building production AI agents comes with infrastructure challenges: complex protocols, stateful conversations, streaming responses, background execution, failure recovery, and scale-out routing. Instead of spending weeks building response streaming, conversation persistence, worker health checks, cursor-based resumption, and durable task execution, you can leverage Agent Gateway to handle these concerns—freeing your team to focus on what makes your agents unique.

**Agent Gateway is a production-ready ingress layer that implements AI protocols for you.** It sits between clients and your worker applications, handling protocol complexity while your workers focus purely on agent behavior. Think of it as an API gateway specifically designed for AI agents—providing stateful routing, durable execution, stream caching, and conversation management out of the box.

### Why You Need This

**Without Agent Gateway:**
- ❌ Implement OpenAI Responses API streaming from scratch
- ❌ Build conversation state management and persistence
- ❌ Handle cursor-based stream resumption after network failures
- ❌ Ensure background requests survive application restarts
- ❌ Route related requests to the same worker instance
- ❌ Implement health checking and worker discovery
- ❌ Manage protocol versioning and compatibility

**With Agent Gateway:**
- ✅ Full OpenAI Responses & Conversations API out of the box
- ✅ Automatic stream caching with cursor-based resumption
- ✅ Durable background execution (survives gateway restarts)
- ✅ Stateful routing for consistent request handling
- ✅ Built-in worker health monitoring and discovery
- ✅ A2A protocol forwarding for agent-to-agent communication
- ✅ Your workers implement one simple interface: execute agent, return events

**Result:** Deploy agents in hours, not weeks. Scale confidently. Focus on differentiation.

## System Topology

```
┌─────────────┐
│   Client    │
└──────┬──────┘
       │
       ▼
┌────────────────────────────────────────────────────────┐
│              Agent Gateway (Ingress)                   │
│                                                        │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │  Responses   │  │Conversations │  │  A2A/Other   │  │
│  │     API      │  │     API      │  │   Protocols  │  │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘  │
│         │                 │                 │          │
│         ▼                 ▼                 │          │
│  ┌────────────────────────────────┐         │          │
│  │    Orleans Grain Layer         │         │          │
│  │  (ResponseGrain,               │         │          │
│  │   ConversationGrain)           │         │          │
│  │  - Durable state               │         │          │
│  │  - Stream caching              │         │          │
│  │  - Background execution        │         │          │
│  └────────────┬───────────────────┘         │          │
│               │                             │          │
│               ▼                             ▼          │
│  ┌──────────────────────────────────────────────────┐  │
│  │        Worker Registry & Discovery               │  │
│  │  - Health monitoring                             │  │
│  │  - Capability caching                            │  │
│  │  - Stateful routing                              │  │
│  └────────────┬─────────────────────────────────────┘  │
└───────────────┼────────────────────────────────────────┘
                │
                ▼
    ┌───────────────────────┐
    │  Worker Applications  │
    │  - Agent execution    │
    │  - Custom logic       │
    │  - Tool invocation    │
    └───────────────────────┘
```

**Key Components:**

- **Clients**: Any application making HTTP requests to agent APIs
- **Gateway Nodes**: Orleans-based cluster implementing protocols and managing state
  - Exposes OpenAI Responses, Conversations, A2A APIs
  - Manages ResponseGrains and ConversationGrains (virtual actors)
  - Persists to Azure Blob Storage (state) and Table Storage (reminders)
  - Handles worker discovery, health checks, and request routing
- **Workers**: Your custom agent implementations
  - Expose `/v1/responses` endpoint (for responses execution)
  - Expose `/health` endpoint (for health monitoring)
  - Expose `/agents` endpoint (for capability discovery)
  - Scale independently from gateway

**Request Flow Example:**
1. Client creates background response → Gateway Node
2. Gateway activates ResponseGrain → Stores state in Azure Storage
3. Grain queries worker discovery → Finds worker supporting requested agent
4. Gateway forwards request to worker via HTTP
5. Worker streams events back → Grain persists each event to storage
6. Gateway restarts (maintenance) → Orleans cluster redistributes grains
7. Client polls response status → Different gateway node
8. Node activates same ResponseGrain (from Azure Storage)
9. Returns current status without re-executing on worker

## Table of Contents

- [Overview](#overview)
- [Key Features](#key-features)
- [Architecture](#architecture)
- [Supported Protocols](#supported-protocols)
- [Core Components](#core-components)
- [Execution Modes](#execution-modes)
- [Worker Management](#worker-management)
- [Stateful Request Processing](#stateful-request-processing)
- [Durable Execution](#durable-execution)
- [Stream Caching & Resumption](#stream-caching--resumption)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [API Endpoints](#api-endpoints)
- [Production Deployment](#production-deployment)

## Overview

The **Agent Gateway** is designed to solve the operational challenges of running AI agents at scale. Rather than implementing complex agentic protocols in every application, the gateway provides a standardized, scalable ingress layer that:

1. **Exposes standardized protocols** - Implements OpenAI Responses, Conversations, A2A, and other AI/agent protocols
2. **Manages protocol complexity** - Handles streaming, pagination, state management, and error handling
3. **Enables stateful routing** - Routes related requests to the same worker instance for consistency
4. **Provides durable execution** - Survives gateway restarts via Orleans-backed persistence
5. **Caches streaming responses** - Enables cursor-based stream resumption and replay
6. **Scales out horizontally** - Orleans clustering supports multi-node deployments
7. **Supports flexible execution** - Local execution or distributed worker forwarding

## Key Features

### Protocol Implementation
- **Complete OpenAI Responses API** - Streaming and non-streaming response generation, background execution, status polling, cursor-based stream resumption
- **Complete OpenAI Conversations API** - Stateful conversation management with item CRUD operations and pagination
- **Agent-to-Agent (A2A) Protocol** - Reverse proxy forwarding to worker applications with agent discovery
- **DevUI Integration** - Built-in agent/workflow discovery and management UI

### Operational Excellence
- **Durable Background Execution** - Orleans reminders ensure background requests complete even after gateway restart
- **Stateful Request Routing** - Consistent routing of related requests (e.g., long-running operation status checks)
- **Stream Caching** - All streaming events are persisted, enabling clients to resume from any sequence number
- **Worker Health Monitoring** - Automatic health checks with failure thresholds and automatic deregistration
- **Worker Discovery Caching** - Intelligent caching of worker capabilities to minimize discovery overhead
- **Idempotent Operations** - Safe retries for message appending to conversations

### Scalability & Reliability
- **Orleans Virtual Actors** - Grain-based state management for conversations and responses
- **Azure Storage Integration** - Blob storage for state, Table storage for reminders
- **Distributed Clustering** - Multi-node gateway deployments with automatic failover
- **Horizontal Scaling** - Add gateway nodes and worker instances independently

## Supported Protocols

### 1. OpenAI Responses API
Complete implementation of OpenAI's Responses API for generating model responses:
- **Request Types**: Streaming, non-streaming, background execution
- **Stream Resumption**: Cursor-based (`starting_after`) to resume from any sequence number
- **Status Tracking**: Real-time status (`queued`, `in_progress`, `completed`, `failed`, `cancelled`)
- **Input Management**: List and paginate input items
- **Background Processing**: Create long-running responses that survive gateway restarts

See [Responses API Specification](./Responses/api_spec.md) for details.

### 2. OpenAI Conversations API
Full-featured conversation state management:
- **CRUD Operations**: Create, read, update, delete conversations
- **Item Management**: Add, retrieve, delete individual messages/items
- **Pagination**: Cursor-based pagination with ascending/descending order
- **Idempotent Appending**: Safe concurrent message appending with conflict detection
- **Metadata Support**: Custom key-value pairs on conversations

See [Conversations API Specification](./Conversations/api_spec.md) for details.

### 3. Agent-to-Agent (A2A) Protocol
HTTP reverse proxy for A2A protocol endpoints:
- **Agent Discovery**: `/a2a/{agent}/v1/card`
- **Message Handling**: `/a2a/{agent}/v1/message:send`, `/v1/message:stream`
- **Task Management**: `/a2a/{agent}/v1/tasks/{id}`, cancel, subscribe
- **Push Notifications**: Configure notification endpoints
- **Stateful Routing**: Routes requests to workers supporting specific agents

### 4. DevUI & Entity Discovery
Web-based management interface:
- **Agent Discovery**: Automatic aggregation of agents from all workers
- **Entity Management**: List, inspect agents and workflows
- **Interactive Testing**: Built-in chat interface for agent testing

## Core Components

### ResponseGrain
The heart of the Responses API implementation. Each response has a dedicated grain instance.

**Responsibilities:**
- **Request Execution**: Delegates to `IResponseExecutor` (local or worker)
- **Stream Collection**: Captures all streaming events in order with sequence numbers
- **State Persistence**: Stores request, response metadata, and complete event stream
- **Background Execution**: Uses Orleans reminders for resilient background processing
- **Stream Resumption**: Serves cached events from any sequence number
- **Conversation Integration**: Idempotently appends messages to conversations on completion

**Execution Flow:**
1. Client creates response → `ResponseGrain` activated
2. Request validated → Initial response state stored
3. Orleans reminder registered for background resilience
4. Execution task started → Streams events from `IResponseExecutor`
5. Each event → Added to `StreamingUpdates`, state persisted, watchers notified
6. On completion → Messages appended to conversation (if configured)
7. Reminder unregistered

### ConversationGrain
Manages stateful conversations with durable message history.

**Responsibilities:**
- **Message Storage**: Maintains ordered list of conversation items (messages)
- **CRUD Operations**: Create, retrieve, update, delete conversations
- **Item Management**: Add, retrieve, delete individual items
- **Pagination**: Efficient cursor-based pagination in both directions
- **Idempotent Appending**: Supports safe concurrent message appending via `afterItemId` parameter
- **Metadata Management**: Custom key-value pairs for application data

**Idempotent Appending:**
The `AppendItemsAsync` method ensures safe message appending in concurrent scenarios:
```csharp
Task<int> AppendItemsAsync(IReadOnlyList<ItemResource> items, string? afterItemId)
```
- `afterItemId`: ID of the last message that must exist before appending
- Returns count of messages appended (0 if duplicate/retry, -1 if conflict)
- Prevents duplicate message insertion on retries
- Detects concurrent modifications

### ResponsesService
Facade over `ResponseGrain` operations, providing a clean API for HTTP endpoints.

**Operations:**
- `CreateResponseAsync` - Non-streaming response creation
- `CreateResponseStreamingAsync` - Streaming response creation
- `GetResponseAsync` - Retrieve response by ID
- `GetResponseStreamingAsync` - Resume streaming from sequence number
- `ListResponseInputItemsAsync` - Paginate input items

### WorkerRegistry
Tracks registered worker instances and their health status.

**Features:**
- **Registration Management**: Upsert/remove worker endpoints
- **Health Tracking**: Consecutive failure counts with configurable thresholds
- **Automatic Cleanup**: Deregisters workers exceeding failure threshold
- **Active Worker Query**: Provides list of healthy workers for routing

### WorkerDiscoveryCache
Caches the results of worker agent discovery to minimize HTTP overhead.

**Features:**
- **TTL-Based Caching**: Configurable cache duration (default 5 minutes)
- **Automatic Invalidation**: On worker removal or failure
- **Lazy Loading**: Discovers agents on-demand
- **Agent Lookup**: Fast dictionary-based agent capability queries

**Workflow:**
1. Request for agent `X` arrives
2. Check cache for worker capabilities
3. Cache miss → HTTP GET to worker's discovery endpoint
4. Parse response → Extract agent cards
5. Store in cache as `Dictionary<string, AgentDiscoveryCard>`
6. Return matching worker

### WorkerHealthCheckService
Background service for proactive worker health monitoring.

**Features:**
- **Periodic Probing**: HTTP health checks every 15 seconds (configurable)
- **Failure Tracking**: Increments failure count on unsuccessful probes
- **Automatic Deregistration**: Removes workers after 3 consecutive failures (configurable)
- **Cache Cleanup**: Removes stale discovery cache entries

**Health Check Flow:**
```
┌──────────────────┐
│  Timer Tick      │
│  (15s interval)  │
└────────┬─────────┘
         │
         ▼
┌─────────────────────────┐
│ For each worker:        │
│  GET {HealthUri}        │
└────────┬────────────────┘
         │
    ┌────┴─────┐
    │          │
    ▼          ▼
┌────────┐  ┌────────┐
│Success │  │Failure │
└───┬────┘  └───┬────┘
    │           │
    ▼           ▼
┌────────┐  ┌──────────────────┐
│Reset   │  │Increment count   │
│failure │  │If >= threshold:  │
│count   │  │  - Deregister    │
│        │  │  - Invalidate    │
│        │  │    cache         │
└────────┘  └──────────────────┘
```

## Execution Modes

The gateway supports two execution modes for Responses API requests:

### 1. Local Execution (LocalChatClientResponseExecutor)
Executes agent logic directly within the gateway process using `ChatClientAgent`.

**Use Case:** Simple deployments, development, testing

**Configuration:**
```csharp
builder.Services.AddSingleton<IResponseExecutor, LocalChatClientResponseExecutor>();
builder.AddChatClient("chat-model");  // OpenAI, Azure OpenAI, Ollama, etc.
```

**Flow:**
1. `ResponseGrain` calls `LocalChatClientResponseExecutor`
2. Executor creates `ChatClientAgent` with configured `IChatClient`
3. Loads conversation history from `ConversationGrain` (if applicable)
4. Calls `agent.RunStreamingAsync(messages, ...)`
5. Converts agent streaming updates to `StreamingResponseEvent` format
6. Yields events back to grain for persistence

**Advantages:**
- Simple setup
- No additional infrastructure
- Direct access to conversation state

**Limitations:**
- Limited to single IChatClient implementation
- Scaling requires gateway scaling
- No custom agent logic

### 2. Worker Forwarding (WorkerResponseExecutor)
Forwards requests to remote worker applications via HTTP.

**Use Case:** Production deployments, custom agent implementations, multi-agent systems

**Configuration:**
```csharp
builder.Services.AddSingleton<IResponseExecutor, WorkerResponseExecutor>();
```

**Flow:**
1. `ResponseGrain` calls `WorkerResponseExecutor`
2. Executor extracts agent name from request (`agent.name` or `model`)
3. Queries `WorkerDiscoveryCache` for capable worker
4. HTTP POST to `{WorkerEndpoint}/v1/responses` with `CreateResponse` body
5. Sets `stream: true` to receive SSE stream
6. Parses SSE events (`data: {json}`) into `StreamingResponseEvent` objects
7. Yields events back to grain for persistence

**Advantages:**
- Custom agent implementations in any language
- Independent worker scaling
- Multiple agent types per deployment
- Worker-level isolation and versioning

**Requirements:**
Workers must:
- Implement `POST /v1/responses` accepting `CreateResponse` JSON
- Return `text/event-stream` with SSE format: `data: {StreamingResponseEvent}\n\n`
- Implement `GET /health` for health checks
- Implement `GET /agents` returning `List<AgentDiscoveryCard>`

## Worker Management

### Registration
Workers register with the gateway via HTTP POST:

```bash
POST /workers/registrations
Content-Type: application/json

{
  "endpoint": "http://worker-1:8080",
  "hostId": "instance-xyz",
  "healthPath": "/health",
  "discoveryPath": "/agents"
}
```

**Response:**
```json
{
  "registrationId": "http://worker-1:8080"
}
```

The `endpoint` serves as a stable identifier. Repeated registrations update the worker entry and act as heartbeats.

### Deregistration
```bash
DELETE /workers/registrations?endpoint=http://worker-1:8080
```

### Health Monitoring
The `WorkerHealthCheckService` automatically:
1. Probes `GET {endpoint}{healthPath}` every 15 seconds
2. Expects HTTP 2xx response
3. Increments failure count on non-2xx or exception
4. Deregisters worker after 3 consecutive failures
5. Invalidates discovery cache on failure/deregistration

### Discovery
Workers expose agent capabilities at their discovery endpoint:

```bash
GET http://worker-1:8080/agents
```

**Expected Response:**
```json
[
  {
    "name": "customer-support",
    "description": "Handles customer inquiries"
  },
  {
    "name": "code-reviewer",
    "description": "Reviews code for best practices"
  }
]
```

Gateway aggregates agents from all workers and exposes via:
```bash
GET /agents
```

## Stateful Request Processing

The gateway ensures **stateful routing** for scenarios where request consistency matters.

### Problem
In scale-out deployments with multiple worker instances:
- Long-running background responses require status polling
- Scheduling the same request on multiple workers → duplicate work
- Workers may maintain agent-specific state (e.g., loaded models)

### Solution
The gateway uses `WorkerDiscoveryCache` and Orleans grain activation to provide:

1. **Agent-to-Worker Mapping**: Discovery cache maps agent names to capable workers
2. **Consistent Grain Activation**: Each `ResponseGrain` (by ID) activates on same silo node
3. **Worker Selection**: Same response ID → Same grain activation → Consistent worker selection

**Example Flow:**
```
Client: POST /v1/responses {agent: "legal-reviewer", background: true}
  → ResponseGrain "resp_abc123" activated on Gateway Node 2
  → WorkerDiscoveryCache: Find worker supporting "legal-reviewer"
  → Worker-A selected
  → HTTP POST to Worker-A
  → Response: {id: "resp_abc123", status: "queued"}

[30 seconds later]
Client: GET /v1/responses/resp_abc123
  → ResponseGrain "resp_abc123" activated on Gateway Node 2 (same node due to Orleans consistent hashing)
  → Grain returns persisted state: {id: "resp_abc123", status: "in_progress"}

[2 minutes later]
Client: GET /v1/responses/resp_abc123?stream=true
  → ResponseGrain "resp_abc123" activated on Gateway Node 2
  → Streams cached events from grain state
  → No duplicate worker invocation
```

### Scaling Considerations
- **Gateway Nodes**: Orleans clustering ensures grain affinity across nodes
- **Worker Instances**: Discovery cache selects first capable worker (could add load balancing)
- **State Persistence**: Azure Blob Storage shared across all gateway nodes

## Durable Execution

The gateway provides **durable execution** for background responses via Orleans reminders.

### Background Execution
Per the OpenAI Responses API specification:
> "To start response generation in the background, make an API request with background set to true"

**Request:**
```bash
POST /v1/responses
Content-Type: application/json

{
  "model": "gpt-4o",
  "input": "Write a comprehensive market analysis report",
  "background": true
}
```

**Response (immediate):**
```json
{
  "id": "resp_xyz789",
  "status": "queued",
  "created_at": 1704067200,
  ...
}
```

### Durability Mechanism

1. **Request Arrival**: `ResponseGrain` stores request in persistent state
2. **Reminder Registration**: Orleans reminder `BackgroundExecution` created (1-minute period)
3. **Execution Start**: Background task initiated (`RunAsync`)
4. **Gateway Restart**: If gateway crashes/restarts during execution:
   - Orleans reactivates `ResponseGrain` from persisted state
   - Reminder fires → Checks if execution is incomplete
   - Restarts `RunAsync` task from the beginning
5. **Completion**: Reminder unregistered, final state persisted

**Idempotency:**
The `RunAsync` method is idempotent:
```csharp
if (!response.IsTerminal) {
    await ProcessRequestAsync(cancellationToken);
}
await FinalizeResponseAsync(cancellationToken);
```

**Key Properties:**
- **State Persistence**: `ResponseState` includes request, response, and all streaming events
- **Reminder Durability**: Orleans reminders survive process restarts (backed by Azure Table Storage)
- **Activation Recovery**: Grain reactivates with full state on any node in the cluster
- **Progress Preservation**: All emitted streaming events are already persisted

### Failure Handling
If execution fails:
1. Exception caught in `RunAsync`
2. Response status set to `failed`
3. Error details captured in `response.error`
4. `response.failed` event emitted and persisted
5. Reminder unregistered
6. Clients can retrieve failure details via `GET /v1/responses/{id}`

## Stream Caching & Resumption

The gateway **caches all streaming events** to enable cursor-based stream resumption.

### Problem
In standard streaming APIs:
- Network interruptions → Lost events
- Clients must restart from beginning
- High token/cost overhead for long responses

### Solution
Every `StreamingResponseEvent` is persisted with a sequence number:

```csharp
class ResponseState {
    List<StreamingResponseEvent> StreamingUpdates;  // Ordered list
}

// Each event has:
interface StreamingResponseEvent {
    int SequenceNumber;  // 1, 2, 3, ...
    string Type;         // "response.output_text.delta", etc.
}
```

**Resume Request:**
```bash
GET /v1/responses/resp_abc123?stream=true&starting_after=150
```

**Behavior:**
1. `ResponseGrain` loads from persistent state
2. Streams events from `StreamingUpdates` list starting at index 150
3. If response is still in progress, continues yielding new events as they arrive
4. If response is complete, streams remaining cached events and closes

**Example:**
```
Original Stream (1000 events):
[1] response.created
[2] response.in_progress
[3] response.output_item.added
[4] response.output_text.delta ("The")
[5] response.output_text.delta (" quick")
...
[500] response.output_text.delta (" brown")
[Network Interruption]

Resume Stream (starting_after=500):
[501] response.output_text.delta (" fox")
[502] response.output_text.delta (" jumps")
...
[1000] response.completed
```

### Benefits
- **Fault Tolerance**: Network issues don't lose data
- **Cost Optimization**: No duplicate token generation
- **Client Flexibility**: Resume from any point
- **Debugging**: Full event history for analysis

### Storage Implications
- Each streaming event is serialized to Azure Blob Storage as part of `ResponseState`
- Long responses with many tool calls can generate 1000+ events
- Blob storage is cheap (~$0.02/GB/month)
- Consider TTL/cleanup policies for old responses

## Getting Started

### Prerequisites
- .NET 8.0 SDK or later
- Azure Storage Account (for production) or Azurite (for development)
- Docker (optional, for Azurite)

### Development Setup

1. **Start Azurite (local Azure Storage emulator):**
```bash
docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite
```

2. **Configure Connection Strings:**
In `appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "state": "UseDevelopmentStorage=true",
    "reminders": "UseDevelopmentStorage=true"
  }
}
```

3. **Configure Chat Model (for local execution):**
Set environment variable:
```bash
# OpenAI
export ConnectionStrings__chat-model="Endpoint=https://api.openai.com;Key=sk-..."

# Azure OpenAI
export ConnectionStrings__chat-model="Endpoint=https://my-resource.openai.azure.com;Key=...;Deployment=gpt-4o"

# Ollama
export ConnectionStrings__chat-model="Endpoint=http://localhost:11434;Model=llama3"
```

4. **Run the Gateway:**
```bash
cd dotnet/samples/AgentWebChat/AgentGateway
dotnet run
```

5. **Access DevUI:**
Open browser to `https://localhost:5001/devui`

### Testing with cURL

**Create a non-streaming response:**
```bash
curl -X POST https://localhost:5001/v1/responses \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gpt-4o",
    "input": "Hello, how are you?"
  }'
```

**Create a streaming response:**
```bash
curl -N -X POST https://localhost:5001/v1/responses \
  -H "Content-Type: application/json" \
  -H "Accept: text/event-stream" \
  -d '{
    "model": "gpt-4o",
    "input": "Tell me a story",
    "stream": true
  }'
```

**Create a background response:**
```bash
curl -X POST https://localhost:5001/v1/responses \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gpt-4o",
    "input": "Write a detailed analysis",
    "background": true
  }'
```

**Poll response status:**
```bash
curl https://localhost:5001/v1/responses/resp_abc123
```

**Resume streaming from sequence 100:**
```bash
curl -N "https://localhost:5001/v1/responses/resp_abc123?stream=true&starting_after=100" \
  -H "Accept: text/event-stream"
```

## Configuration

### Agent Gateway Options

The gateway can be configured via `appsettings.json` or environment variables.

#### Default Worker

Configure a default worker that is always assumed to be available and supports any workload:

```json
{
  "AgentGateway": {
    "DefaultWorker": {
      "Endpoint": "https://localhost:5001",
      "HostId": "default",
      "HealthPath": "/health",
      "DiscoveryPath": "/discovery"
    }
  }
}
```

Or via environment variables:
```bash
export AgentGateway__DefaultWorker__Endpoint="https://localhost:5001"
export AgentGateway__DefaultWorker__HostId="default"
export AgentGateway__DefaultWorker__HealthPath="/health"
export AgentGateway__DefaultWorker__DiscoveryPath="/discovery"
```

When a default worker is configured:
- It is assumed to always be available
- It is assumed to support any agent/workload
- The gateway will prefer non-default workers when selecting a worker to handle a request
- If no non-default worker supports the requested agent, the default worker will be used as a fallback

#### Runtime Worker Registration

Control whether workers can register/deregister at runtime via the worker management API:

```json
{
  "AgentGateway": {
    "EnableRuntimeRegistration": false
  }
}
```

Or via environment variable:
```bash
export AgentGateway__EnableRuntimeRegistration=false
```

When set to `false`:
- The `/workers/registrations` endpoints will not be mapped
- Workers cannot register or deregister at runtime
- Useful for scenarios where only the default worker should be used

**Combined Example:**
```json
{
  "AgentGateway": {
    "DefaultWorker": {
      "Endpoint": "https://my-worker:8080"
    },
    "EnableRuntimeRegistration": false
  }
}
```

This configuration creates a static deployment with only the default worker and no runtime registration capability.

### Execution Mode
Choose between local and worker execution in `Program.cs`:

```csharp
// Option 1: Local execution
builder.Services.AddSingleton<IResponseExecutor, LocalChatClientResponseExecutor>();

// Option 2: Worker forwarding
builder.Services.AddSingleton<IResponseExecutor, WorkerResponseExecutor>();
```

### Orleans Storage
Configure Azure Storage for production:

```json
{
  "ConnectionStrings": {
    "state": "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=...;EndpointSuffix=core.windows.net",
    "reminders": "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=...;EndpointSuffix=core.windows.net"
  }
}
```

### Health Check Settings
In `WorkerHealthCheckService`:
```csharp
private readonly TimeSpan _interval = TimeSpan.FromSeconds(15);  // Probe frequency
private readonly int _failureThreshold = 3;                      // Failures before deregister
```

### Discovery Cache TTL
In `WorkerDiscoveryCache`:
```csharp
this._cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(5);
```

### Orleans Clustering
For multi-node deployments, configure clustering in `Program.cs`:
```csharp
builder.Host.UseOrleans(siloBuilder =>
{
    siloBuilder.UseAzureStorageClustering(options =>
    {
        options.ConfigureTableServiceClient(connectionString);
    });
});
```

## API Endpoints

### Responses API
| Method | Path | Description |
|--------|------|-------------|
| POST | `/v1/responses` | Create a response (streaming or non-streaming) |
| GET | `/v1/responses/{id}` | Retrieve response by ID |
| GET | `/v1/responses/{id}?stream=true` | Stream response (supports `starting_after`) |
| GET | `/v1/responses/{id}/input_items` | List input items with pagination |

### Conversations API
| Method | Path | Description |
|--------|------|-------------|
| POST | `/v1/conversations` | Create a conversation |
| GET | `/v1/conversations/{id}` | Retrieve conversation |
| POST | `/v1/conversations/{id}` | Update conversation metadata |
| DELETE | `/v1/conversations/{id}` | Delete conversation |
| GET | `/v1/conversations/{id}/items` | List items with pagination |
| POST | `/v1/conversations/{id}/items` | Add items to conversation |
| GET | `/v1/conversations/{id}/items/{itemId}` | Retrieve specific item |
| DELETE | `/v1/conversations/{id}/items/{itemId}` | Delete item |

### A2A Forwarding
| Method | Path | Description |
|--------|------|-------------|
| GET/POST | `/a2a/{agent}` | Forward to worker agent endpoint |
| GET | `/a2a/{agent}/v1/card` | Agent discovery card |
| GET/POST | `/a2a/{agent}/v1/tasks/{id}` | Task management |
| POST | `/a2a/{agent}/v1/message:send` | Send message |
| POST | `/a2a/{agent}/v1/message:stream` | Stream message |

### Worker Management
| Method | Path | Description |
|--------|------|-------------|
| POST | `/workers/registrations` | Register/heartbeat worker |
| DELETE | `/workers/registrations` | Deregister worker |

### Discovery & DevUI
| Method | Path | Description |
|--------|------|-------------|
| GET | `/agents` | List all discovered agents |
| GET | `/v1/entities` | List entities (DevUI format) |
| GET | `/v1/entities/{id}/info` | Entity details |
| GET | `/devui` | DevUI web interface |

## Production Deployment

### Multi-Node Gateway Setup

1. **Shared Storage:** All gateway nodes must share Azure Storage accounts
2. **Clustering:** Orleans handles grain distribution and failover
3. **Load Balancer:** Place HTTP load balancer in front of gateway nodes
4. **Health Endpoint:** Configure LB to use `/health` endpoint

**Architecture:**
```
                    ┌──────────────┐
                    │ Load Balancer│
                    └──────┬───────┘
                           │
         ┌─────────────────┼─────────────────┐
         │                 │                 │
         ▼                 ▼                 ▼
    ┌─────────┐       ┌─────────┐       ┌─────────┐
    │Gateway 1│       │Gateway 2│       │Gateway 3│
    └────┬────┘       └────┬────┘       └────┬────┘
         │                 │                 │
         └─────────────────┼─────────────────┘
                           │
                    ┌──────┴───────┐
                    │ Azure Storage│
                    │ - Blobs      │
                    │ - Tables     │
                    └──────────────┘
```

### Worker Deployment

Workers can be deployed as:
- **Containers**: Kubernetes/Docker with horizontal scaling
- **VMs**: Traditional VM deployments
- **Serverless**: Azure Container Apps, AWS Fargate (with health endpoint for long-running)
- **Process Isolation**: Multiple workers per host with different ports

**Worker Requirements:**
- Implement health endpoint returning 200 OK
- Implement discovery endpoint returning agent list
- Implement `/v1/responses` endpoint (for WorkerResponseExecutor mode)
- Register with gateway on startup
- Send periodic heartbeats (or rely on health checks)

### Monitoring & Observability

**Metrics to Track:**
- Response creation rate
- Response completion time (by status)
- Worker health check success/failure rate
- Discovery cache hit rate
- Orleans grain activation count
- Storage operation latency

**Logging:**
- Structured logging with request IDs
- Worker health probe results
- Discovery cache operations
- Grain activation/deactivation events

**Tracing:**
The gateway includes OpenTelemetry instrumentation hooks for distributed tracing across gateway and workers.

### Security Considerations

**Current Implementation:**
- No authentication/authorization
- Suitable for internal/private networks

**Production Recommendations:**
- Add API key or JWT authentication middleware
- Use Azure AD/Entra ID for worker authentication
- Enable HTTPS for all endpoints
- Implement rate limiting per client/tenant
- Add request validation and sanitization
- Use managed identities for Azure Storage access

### Scaling Guidelines

**Gateway Scaling:**
- Start with 2-3 nodes for availability
- Add nodes based on request throughput
- Orleans handles automatic rebalancing

**Worker Scaling:**
- Scale based on response execution time
- Monitor queue depth (background responses)
- Consider worker specialization (agent types)

**Storage Scaling:**
- Azure Blob Storage scales automatically
- Monitor IOPS for Table Storage (reminders)
- Consider partitioning strategies for high-volume

### Cost Optimization

**Storage:**
- Implement response TTL/cleanup policies
- Use lifecycle management for old blobs
- Archive completed responses to cool/archive tiers

**Compute:**
- Use spot instances for workers (if stateless)
- Auto-scale based on queue depth
- Consider reserved instances for baseline capacity

**Networking:**
- Co-locate gateway and workers in same region
- Use private endpoints for Azure Storage
- Minimize cross-region traffic

## Contributing

This is a sample application demonstrating the Agent Gateway pattern. For production use, consider:
- Adding comprehensive unit and integration tests
- Implementing authentication and authorization
- Adding metrics and distributed tracing
- Implementing retry policies with exponential backoff
- Adding circuit breakers for worker communication
- Implementing request validation and rate limiting

## License

Copyright (c) Microsoft. All rights reserved.

Licensed under the MIT License. See LICENSE file in the project root for details.
