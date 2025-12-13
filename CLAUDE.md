# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is an unofficial .NET SDK for [Inngest](https://www.inngest.com/), a platform for building reliable, scalable event-driven architectures. The SDK enables .NET applications to define functions that respond to events and cron schedules, with built-in retry logic and step memoization.

## Solution Structure

- **Inngest/** - Core SDK library (NuGet package `Inngest.NET`)
- **InngestExample/** - ASP.NET Core sample application demonstrating SDK usage

## Build Commands

```bash
# Restore and build the solution
dotnet build InngestExample.sln

# Run the example application
dotnet run --project InngestExample

# Build release
dotnet build -c Release
```

## Running with Inngest Dev Server

The SDK requires the Inngest Dev Server for local development. The dev server runs at `http://localhost:8288`.

### Option A: Auto-Discovery (Recommended)

```bash
# Terminal 1: Start the .NET app first
dotnet run --project InngestExample

# Terminal 2: Start the Dev Server with app URL
npx inngest-cli@latest dev -u http://localhost:5000/api/inngest
```

### Option B: Manual Registration

```bash
# Terminal 1: Start the Dev Server without discovery
npx inngest-cli@latest dev --no-discovery

# Terminal 2: Start the .NET app (registers on startup)
dotnet run --project InngestExample
```

## Architecture

### Core Components

**InngestClient** (`Inngest/InngestClient.cs`) - Central class handling:
- Function registration and storage
- HTTP request handling (PUT for sync, POST for function calls, GET for introspection)
- Event sending to Inngest API
- Signature verification for production environments
- Registration payload generation for the Inngest server

**InngestContext** (`Inngest/InngestContext.cs`) - Execution context passed to function handlers:
- `Step<T>()` - Execute memoized steps with optional retry configuration
- `Sleep()` - Introduce delays in function execution
- `GetSecret()` - Access registered secrets
- `SendEvent()` - Emit new events from within functions

**FunctionDefinition** (`Inngest/FunctionDefinition.cs`) - Defines functions with:
- Event or cron triggers via `FunctionTrigger`
- Configuration options (concurrency, retry, rate limits)
- Step definitions for registration

### ASP.NET Core Integration

- `UseInngest(path)` - Middleware extension to mount the Inngest endpoint
- `AddInngest(eventKey, signingKey)` - DI registration extension

### Request Flow

1. **Sync (PUT)** - Inngest server calls to register/discover functions
2. **Call (POST)** - Inngest server invokes functions with event payloads
3. **Introspection (GET)** - Returns function count and configuration status

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `INNGEST_EVENT_KEY` | Event key for sending events |
| `INNGEST_SIGNING_KEY` | Signing key for request verification |
| `INNGEST_DEV` | Enable dev mode (set to `true` or dev server URL) |
| `INNGEST_DISABLE_CRON_IN_DEV` | Disable cron triggers in dev mode (set to `true` or `1`) |
| `INNGEST_SERVE_ORIGIN` | Base URL for Inngest to reach this service |
| `INNGEST_SERVE_PATH` | Path for the Inngest endpoint |
| `INNGEST_API_BASE_URL` | Override Inngest API URL |
| `INNGEST_EVENT_API_BASE_URL` | Override Inngest Event API URL |

## Key Patterns

### Function Registration with Steps

Steps must be registered using fluent API for Inngest to know about them during sync:

```csharp
inngestClient.CreateFunction("my-function", async (context) =>
{
    await context.Step("step-1", async () => { /* logic */ return result; });
    return finalResult;
})
.WithStep("step-1", "Description");  // Required for sync
```

### Step Memoization

The `Steps` dictionary in `InngestContext` contains results from previously executed steps. The SDK checks this before executing a step to avoid re-execution.

## Current Limitations

- Sleep step reporting to Inngest server is not fully implemented (throws `NotImplementedException`)
- Step-specific execution (`stepId != "step"`) returns 501 Not Implemented
- SDK version is `0.1.0` (early development)
