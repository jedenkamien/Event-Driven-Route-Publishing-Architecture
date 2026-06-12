# Detailed Analysis: Event-Driven Route Publishing Architecture

> **Audience**: Deep technical divers, future maintainers, and architecture reviewers.  
> **Purpose**: Exhaustive reference for every design decision, trade-off, and implementation detail.  
> **Companion files**: [README.md](README.md) (executive summary) · [PATTERN_OVERVIEW.md](PATTERN_OVERVIEW.md) (10-min read) · [code-samples/](code-samples/) (annotated source)

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Complete Component Walkthrough](#2-complete-component-walkthrough)
   - 2.1 [Facade Layer — RouteStrategyChangedPublishService](#21-facade-layer--routestrategychangedpublishservice)
   - 2.2 [Strategy Factory — RoutingStrategyPicker](#22-strategy-factory--routingstrategypicker)
   - 2.3 [Template Method Helper — PublishingSupporter](#23-template-method-helper--publishingsupporter)
   - 2.4 [Concrete Strategies — Transportation Mode Publishers](#24-concrete-strategies--transportation-mode-publishers)
   - 2.5 [Message Hierarchy](#25-message-hierarchy)
   - 2.6 [Dependency Injection Wiring](#26-dependency-injection-wiring)
3. [Design Decisions & Trade-offs](#3-design-decisions--trade-offs)
4. [Error Handling Strategy](#4-error-handling-strategy)
5. [Extensibility Analysis](#5-extensibility-analysis)
6. [Testing Strategy](#6-testing-strategy)
7. [Performance Considerations](#7-performance-considerations)
8. [Alternatives Considered](#8-alternatives-considered)
9. [Lessons Learned](#9-lessons-learned)

---

## 1. Architecture Overview

### Component Map

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                           ENTRY POINTS                                       │
│  Event Bus / Message Consumer → RouteStrategyChangedPublishService           │
└─────────────────────────────────────┬────────────────────────────────────────┘
                                      │  calls one of:
                                      │  ProcessRouteStrategyCreatedEvent()
                                      │  ProcessRouteStrategyUpdatedEvent()
                                      │  ProcessRouteStrategyDeletedEvent()
                                      ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  FACADE: RouteStrategyChangedPublishService                                  │
│  • Unified interface — hides selection/publishing complexity                 │
│  • Consistent try/catch + logging for all three operations                   │
│  • Delegates to RoutingStrategyPicker                                        │
└─────────────────────────────────────┬────────────────────────────────────────┘
                                      │  Pick(routeStrategy)
                                      ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  STRATEGY FACTORY: RoutingStrategyPicker                                     │
│  • ConcurrentDictionary<string, IPublishingStrategy> registry                │
│  • Lazy init: populated on first call, reused thereafter                     │
│  • Returns null for explicitly unsupported modes (Null Object)               │
└─────────────────────────────────────┬────────────────────────────────────────┘
                                      │  returns IPublishingStrategy
                                      ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  STRATEGY INTERFACE: IPublishingStrategy                                     │
│  • PublishCreated / PublishUpdated / PublishDeleted                          │
└──────┬───────────────────┬───────────────────┬───────────────────────────────┘
       │                   │                   │
       ▼                   ▼                   ▼
┌────────────┐  ┌──────────────────┐  ┌─────────────────────┐   ┌ ─ ─ ─ ─ ─ ─
│ Walking    │  │ Driving          │  │ Bicycle             │   │ ... N more  │
│ Route      │  │ Route            │  │ Route               │     (in prod:   
│ Publisher  │  │ Publisher        │  │ Publisher           │   │ 16 total)   │
└─────┬──────┘  └────────┬─────────┘  └──────────┬──────────┘    ─ ─ ─ ─ ─ ─ ┘
      │                  │                        │
      └──────────────────┴────────────────────────┘
                                      │  Publish<T>(delegate, ...)
                                      ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  TEMPLATE METHOD: PublishingSupporter                                        │
│  Step 1: CreateMessageWithBasicInformation()   ← always the same            │
│  Step 2: fillSpecificDetailsForMessageAction() ← injected by each publisher  │
│  Step 3: _publisher.PublishAsync()             ← always the same            │
│  Step 4: Logging + error handling              ← always the same            │
└─────────────────────────────────────┬────────────────────────────────────────┘
                                      │
                                      ▼
┌──────────────────────────────────────────────────────────────────────────────┐
│  INFRASTRUCTURE: IMessagePublisher                                           │
│  • Azure Service Bus (production) / In-memory mock (tests)                  │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Responsibility Matrix

| Component | Knows WHAT to publish | Knows HOW to publish | Knows WHEN to publish | Knows WHICH publisher |
|---|:---:|:---:|:---:|:---:|
| `RouteStrategyChangedPublishService` | ✗ | ✗ | ✅ | ✗ |
| `RoutingStrategyPicker` | ✗ | ✗ | ✗ | ✅ |
| `PublishingSupporter` | ✗ | ✅ | ✗ | ✗ |
| `WalkingRouteChangedPublisher` | ✅ | ✗ | ✗ | ✗ |

No single class holds more than one dimension of responsibility. This is the core enabler of the Open-Closed Principle in this architecture.

---

## 2. Complete Component Walkthrough

### 2.1 Facade Layer — `RouteStrategyChangedPublishService`

> **See**: [code-samples/facade-coordinator-example.cs](code-samples/facade-coordinator-example.cs)

#### What it does

The service is the public contract for everything related to route strategy publishing. Any consumer (event processor, background job, API controller) calls exactly three methods:

```csharp
Task ProcessRouteStrategyCreatedEvent(RouteStrategy, RouteMetadata, CancellationToken);
Task ProcessRouteStrategyUpdatedEvent(RouteStrategy, RouteMetadata, CancellationToken);
Task ProcessRouteStrategyDeletedEvent(RouteStrategy, RouteMetadata, CancellationToken);
```

Each method follows the same structural pattern: pick a strategy, guard against null, delegate, catch exceptions.

#### The static error-message factory

```csharp
private static string ExceptionMessage(string operationType, RouteStrategy routeStrategy,
    RouteMetadata routeMetadata, Exception ex) =>
    $"Error while publishing route strategy {operationType} message. " +
    $"Route Metadata ID: {routeMetadata.NavigationProfileId}, " +
    $"Transportation mode: {routeStrategy.TransportationMode}, " +
    $"Inner exception: {ex.Message}, Stack trace: {ex.StackTrace}.";
```

This is a private `static` factory method — an elegant alternative to defining a `const` format string or scattering inline interpolation throughout. It ensures every log entry for every operation has an identical, searchable structure, making log aggregation (e.g., in Application Insights or Splunk) trivially easy.

#### Why a static method rather than a base class or helper?

Three methods share the format string. A base class would introduce coupling for a class with no other shared state. A helper class would be over-engineering for one three-argument string. A `static` local method is the minimal, cohesive choice.

#### Null Object guard

```csharp
if (publishStrategy is null)
{
    return;
}
```

The guard is **intentional and documented**. `null` entries are explicitly placed in the factory registry for transportation modes that are known but not yet implemented (or intentionally silent). Without this guard, those modes would throw `NullReferenceException` — a confusing failure mode when the intent is "do nothing".

---

### 2.2 Strategy Factory — `RoutingStrategyPicker`

> **See**: [code-samples/strategy-factory-example.cs](code-samples/strategy-factory-example.cs)

#### Registry initialization

```csharp
private void InitializeStrategies()
{
    _strategies.TryAdd(nameof(NavigationProfile.WalkingRoute),
        _serviceProvider.GetRequiredService<IWalkingRoutePublisher>());

    _strategies.TryAdd(nameof(NavigationProfile.DrivingRoute),
        _serviceProvider.GetRequiredService<IDrivingRoutePublisher>());

    _strategies.TryAdd(nameof(NavigationProfile.BicycleRoute),
        _serviceProvider.GetRequiredService<IBicycleRoutePublisher>());

    _strategies.TryAdd(nameof(NavigationProfile.PublicTransitRoute),
        _serviceProvider.GetRequiredService<IPublicTransitRoutePublisher>());

    // Null Object pattern: explicitly unsupported modes
    _strategies.TryAdd(nameof(NavigationProfile.PedestrianAccessibility), null!);
    _strategies.TryAdd(nameof(NavigationProfile.TrafficConditions), null!);
}
```

In production this method registers **16 transportation modes**. The four core modes shown here represent the production pattern faithfully; the remaining 12 follow identical structure.

#### Key design choices in the factory

**`nameof()` instead of string literals**  
`nameof(NavigationProfile.WalkingRoute)` produces `"WalkingRoute"` at compile time. If the property is renamed, the compiler catches the mismatch immediately. Raw string keys (`"WalkingRoute"`) would silently break at runtime.

**`ConcurrentDictionary` instead of `Dictionary`**  
The registry is effectively read-mostly after first write. `ConcurrentDictionary.TryAdd` is safe under concurrent calls from multiple threads during the lazy init window. A plain `Dictionary` with a `lock` would also work, but `ConcurrentDictionary` provides the same safety without an explicit lock.

**Lazy initialization**  
The registry is built on first `Pick()` call rather than in the constructor. This defers resolution of all publisher instances from the DI container until they are actually needed, keeping startup time minimal. In a serverless or Azure Function host, startup latency matters.

**`GetRequiredService` vs. `GetService`**  
`GetRequiredService<T>()` throws an `InvalidOperationException` at startup if the service is not registered. This converts a missing registration (a configuration bug) into an immediate, loud failure rather than a silent null that surfaces at runtime under production load.

#### What `Pick()` returns for unknown modes

```csharp
_strategies.TryGetValue(routeStrategy?.TransportationMode, out var publisher);
if (publisher == null)
{
    throw new ArgumentOutOfRangeException(
        $"Unknown transportation mode: {routeStrategy?.TransportationMode}.");
}
return publisher;
```

There is a subtle but important distinction between:
- A key that is in the dictionary with a `null` value → Null Object (intentional no-op)
- A key that is **not** in the dictionary → `ArgumentOutOfRangeException` (programming error)

`TryGetValue` sets `publisher` to `null` for both cases, so the `null` check after the call cannot distinguish them. In the version shown in code samples, the `ArgumentOutOfRangeException` is thrown on any `null` result. An alternative (and the version used in the Null Object pattern scenario) would be to check `_strategies.ContainsKey(mode)` before throwing, keeping the null return path open only for explicitly registered null entries. The right choice depends on whether you want the facade to silently skip or loudly surface unknown modes — both are valid; pick consistently.

---

### 2.3 Template Method Helper — `PublishingSupporter`

> **See**: [code-samples/template-method-example.cs](code-samples/template-method-example.cs)

#### The invariant workflow

```csharp
public async Task Publish<T>(
    Func<RouteStrategyChangedBaseMessage, RouteStrategy, T> fillSpecificDetailsForMessageAction,
    RouteStrategy routeStrategy,
    RouteMetadata routeMetadata,
    CancellationToken cancellationToken)
    where T : RouteStrategyChangedBaseMessage, new()
{
    T publishMsg = new();

    try
    {
        // Step 1: Base message (common fields: ID, timestamp, profile ID, ...)
        var msgWithBasicInformation = _mapper.Map<T>(
            CreateMessageWithBasicInformation(routeStrategy, routeMetadata));

        // Step 2: Mode-specific transformation (injected as delegate)
        publishMsg = fillSpecificDetailsForMessageAction.Invoke(
            msgWithBasicInformation, routeStrategy);

        // Step 3: Publish to message bus
        await _publisher.PublishAsync(publishMsg, routeMetadata.NavigationProfileId, cancellationToken);

        // Step 4: Log success
        _logger.LogInformation(/* ... */);
    }
    catch (Exception ex)
    {
        // Step 5: Log failure
        _logger.LogError(ex, /* ... */);
        throw;
    }
}
```

The `where T : RouteStrategyChangedBaseMessage, new()` constraint serves two purposes:
1. Guarantees that `T` exposes the base message fields (type-safe polymorphism)
2. Allows `new()` instantiation for the pre-catch default `publishMsg` assignment

#### Why a delegate instead of an abstract method?

The classic GoF Template Method uses an abstract base class with overridable hook methods. The functional approach used here passes the variable step as a `Func<>` parameter. Advantages:

| Concern | Abstract class | Delegate (this design) |
|---|---|---|
| Inheritance coupling | Must inherit from base | No inheritance required |
| Testability | Harder to mock | Pass any lambda in tests |
| Composition | One base per class | Multiple behaviors composable |
| Readability at call site | Method override, indirection | Inline lambda, clear intent |

The trade-off is that the delegate signature is more verbose in the method declaration, but call sites read very naturally:

```csharp
await _supporter.Publish(
    CreateDetailedMessage<WalkingRouteCreatedMessage>,
    routeStrategy, routeMetadata, cancellationToken);
```

#### AutoMapper usage for base message projection

`_mapper.Map<T>(baseMessage)` projects the `RouteStrategyChangedBaseMessage` result of `CreateMessageWithBasicInformation` into the specific message type `T` (e.g., `WalkingRouteCreatedMessage`). This relies on AutoMapper's ability to map from a base type to a derived type, copying all shared fields. The mode-specific delegate then fills in the remaining derived-type properties.

This is an elegant use of AutoMapper — it eliminates the need to manually copy all base fields in every `CreateDetailedMessage` implementation. The mapper profile configuration (not shown here) registers these projections.

---

### 2.4 Concrete Strategies — Transportation Mode Publishers

> **See**: [code-samples/concrete-strategy-example.cs](code-samples/concrete-strategy-example.cs)

All four core publishers follow an identical structure. The table below highlights what varies between them:

| Publisher | Message base type | Mode-specific fields | Key domain object |
|---|---|---|---|
| `WalkingRouteChangedPublisher` | `WalkingRouteChangedMessage` | `WalkingPreferences`, accessibility flags, max distance | `WalkingPreferenceDto` |
| `DrivingRouteChangedPublisher` | `DrivingRouteChangedMessage` | `DrivingPreferences`, highway/toll flags, traffic mode | `DrivingPreferenceDto` |
| `BicycleRouteChangedPublisher` | `BicycleRouteChangedMessage` | `BicyclePreferences`, elevation limits, bike lane pref. | `BicyclePreferenceDto` |
| `PublicTransitRouteChangedPublisher` | `PublicTransitRouteChangedMessage` | `TransitPreferences`, max transfers, accessibility | `TransitPreferenceDto` |

#### The three-method pattern (repeated in every publisher)

```csharp
public async Task PublishCreated(RouteStrategy routeStrategy,
    RouteMetadata routeMetadata, CancellationToken cancellationToken)
{
    await _supporter.Publish(
        CreateDetailedMessage<WalkingRouteCreatedMessage>,
        routeStrategy, routeMetadata, cancellationToken);
}

public async Task PublishUpdated(...) // identical, different message type
public async Task PublishDeleted(...)  // uses CreateDeletedMessage (no preferences needed)
```

`PublishDeleted` intentionally uses a separate `CreateDeletedMessage` helper because deletion events only need base metadata — there is no preference payload. Reusing `CreateDetailedMessage` for deletions would require passing an empty/null preferences object, which is semantically misleading.

#### The `CreateDetailedMessage<T>` method

```csharp
private T CreateDetailedMessage<T>(
    RouteStrategyChangedBaseMessage baseMessage,
    RouteStrategy routeStrategy)
    where T : WalkingRouteChangedMessage
{
    var walkingStrategy = routeStrategy as WalkingRouteStrategy
        ?? throw new InvalidOperationException("Expected WalkingRouteStrategy");

    var msg = _mapper.Map<T>(baseMessage);
    msg.Hash = walkingStrategy.ComputeHash();
    msg.WalkingPreferences = _mapper.Map<IEnumerable<WalkingPreferenceDto>>(
        walkingStrategy.Preferences);

    return msg;
}
```

This is the **only unique code** in each publisher class. Everything else is structural delegation. The explicit cast with `??` operator ensures a fast, clear failure if the wrong strategy type reaches this publisher — an impossible scenario in the current DI setup, but valuable as a defensive invariant.

#### Interface segregation in publisher interfaces

Each publisher implements a specific interface:

```
IPublishingStrategy               ← base: PublishCreated/Updated/Deleted
    ↑
IWalkingRoutePublisher : IPublishingStrategy   ← marker for DI registration
IDrivingRoutePublisher : IPublishingStrategy
IBicycleRoutePublisher : IPublishingStrategy
IPublicTransitRoutePublisher : IPublishingStrategy
```

The mode-specific interface (`IWalkingRoutePublisher`) adds no new methods — it is a marker interface. Its purpose is to give the DI container a distinct registration key per mode. Without it, registering four types against the same `IPublishingStrategy` interface would cause the last registration to silently win. The marker interface avoids this with zero overhead.

---

### 2.5 Message Hierarchy

> **See**: [code-samples/message-hierarchy-example.cs](code-samples/message-hierarchy-example.cs)

#### Three-level inheritance

```
RouteStrategyChangedBaseMessage          ← Level 1: common to ALL modes
    ├── WalkingRouteChangedMessage        ← Level 2: common within walking
    │       ├── WalkingRouteCreatedMessage
    │       ├── WalkingRouteUpdatedMessage
    │       └── WalkingRouteDeletedMessage
    ├── DrivingRouteChangedMessage        ← Level 2: common within driving
    │       └── ...
    ├── BicycleRouteChangedMessage
    └── PublicTransitRouteChangedMessage
```

**Level 1** carries the infrastructure contract: `Id`, `MessageType`, `PublishedTimestamp`, `NavigationProfileId`, `TransportationMode`, `SchemaVersion`, and contextual fields (`RegionCode`, `LanguageCode`, `DevicePlatform`, `AppVersion`). Message routers, audit logs, and monitoring dashboards consume only Level 1 fields, so they work equally for all 16+ modes without modification.

**Level 2** carries mode-specific shared data. For walking routes, `WalkingRouteChangedMessage` holds `WalkingPreferences` and `Hash`. Both created and updated messages need these; the deleted message inherits the level 2 class but leaves the preference payload empty.

**Level 3** (concrete message types) typically add no new properties. They exist as distinct types so consumers can subscribe to specific operation types:

```csharp
// Consumer subscribing only to walking route creations
services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<WalkingRouteCreatedMessage>());
```

#### `Hash` field purpose

Each `WalkingRouteChangedMessage` (and equivalents for other modes) carries a `Hash` field. This is a deterministic hash of the preference payload, enabling consumers to perform change detection without comparing individual fields. If the hash is identical to the stored value, the consumer can skip processing — an important optimization for high-frequency updates.

#### JSON property naming

All fields use `[JsonPropertyName("camelCase")]` attributes. This ensures the over-the-wire JSON format is stable regardless of C# naming conventions applied to properties in the future. It also decouples the C# property name from the contract name, allowing safe refactoring on either side independently.

---

### 2.6 Dependency Injection Wiring

> **See**: [code-samples/dependency-injection-example.cs](code-samples/dependency-injection-example.cs)

#### Lifetime choices explained

| Component | Lifetime | Rationale |
|---|---|---|
| `PublishingSupporter` | `Transient` | Stateless; cheaply created; avoids shared-state bugs |
| `WalkingRouteChangedPublisher` (et al.) | `Transient` | Stateless; one per operation is intentional |
| `RoutingStrategyPicker` | `Singleton` | Holds the `ConcurrentDictionary` cache; must persist across requests |
| `RouteStrategyChangedPublishService` | `Transient` | Stateless coordinator; no reason to cache |

The `RoutingStrategyPicker` being a singleton is critical: the lazy initialization that populates the registry is only correct if the instance survives across requests. If it were `Transient`, the registry would be rebuilt from scratch on every call.

#### Conditional Service Bus registration

```csharp
var appSettings = new ServiceBusConfig();
configuration.Bind(appSettings);
if (!appSettings.EnableServiceBusRegistration)
{
    services.AddTransient<IMessagePublisher, NoOpMessagePublisher>();
}
else
{
    services.AddServiceBus(configuration);
}
```

This allows the full messaging stack to run in development/test environments without an actual Service Bus connection. Developers substitute a no-op or in-memory publisher controlled by a single configuration flag. No code changes required.

---

## 3. Design Decisions & Trade-offs

### Decision 1: Strategy Pattern over Switch Statement

| | Switch Statement | Strategy Pattern |
|---|---|---|
| Adding a new mode | Modify existing method | Add new class + two registrations |
| Test isolation | All modes in one method | Each mode independently testable |
| Compile-time safety | None (string comparison) | `nameof()` + interface |
| Code duplication | High (common publishing logic repeated) | None (PublishingSupporter centralizes it) |
| Team parallelism | Merge conflicts inevitable | Independent files per mode |

**Decision**: Strategy Pattern. The switch-statement cost compounds with each added mode.

### Decision 2: Functional Template Method over Abstract Base Class

| | Abstract Base Class | Functional Delegate |
|---|---|---|
| Inheritance chain | Required | None |
| Testability | Mock requires subclass | Pass lambda directly |
| Composability | One behavior per class | Multiple behaviors composable |
| Call-site readability | Indirection to override | Inline method reference |

**Decision**: Functional delegate. The inheritance chain would add a layer with no semantic value.

### Decision 3: Lazy Strategy Registry over Eager

| | Eager (constructor) | Lazy (first call) |
|---|---|---|
| Startup time | All publishers resolved upfront | Minimal; deferred |
| Error surfacing | DI errors at startup | DI errors at first use |
| Simplicity | Slightly simpler | Requires empty-check guard |

**Decision**: Lazy. In a serverless environment, cold-start latency is a real cost. DI misconfiguration errors in production are caught at deployment verification (integration tests), not silently deferred.

### Decision 4: Marker Interfaces per Transportation Mode

| | Single `IPublishingStrategy` | Marker interfaces |
|---|---|---|
| DI registration | Last registration wins (silent bug) | Each mode has unique key |
| Discoverability | All implementations mixed | Clear per-mode interface |
| Extensibility overhead | Low (no interface to create) | One extra interface per mode |

**Decision**: Marker interfaces. The DI silent-override bug is too dangerous in a registry with 16+ modes.

### Decision 5: ConcurrentDictionary over lock + Dictionary

| | `lock` + `Dictionary` | `ConcurrentDictionary` |
|---|---|---|
| Thread safety | Explicit, verbose | Built-in |
| Performance (read-heavy) | `lock` on every read | Lock-free reads |
| Performance (first write) | Single lock | Comparable |

**Decision**: `ConcurrentDictionary`. The registry is written once and read thousands of times. Lock-free reads are materially faster at scale.

---

## 4. Error Handling Strategy

### Layered Error Propagation

```
PublishingSupporter.Publish()
    ├── catches Exception
    ├── logs _logger.LogError(ex, message)
    └── rethrows (throw;)
            ↓
RouteStrategyChangedPublishService.ProcessRouteStrategy*Event()
    ├── catches Exception
    ├── logs _logger.LogError(ExceptionMessage(...))
    └── rethrows (throw;)
            ↓
Caller (event processor / message handler)
    └── handles final retry / dead-letter logic
```

### Why rethrow at every layer?

Each layer logs with its own contextual data, then rethrows. This produces layered log entries:

- `PublishingSupporter` log: "Failed to publish WalkingRouteCreatedMessage [raw exception message]"
- `RouteStrategyChangedPublishService` log: "Error publishing 'created' for profile P1, mode Walking"

A log aggregation query on `NavigationProfileId` finds both entries, giving the full context. If only one layer logged, you would lose either the low-level exception detail or the high-level operation context.

### Static error-message factory rationale

```csharp
private static string ExceptionMessage(string operationType, RouteStrategy routeStrategy,
    RouteMetadata routeMetadata, Exception ex) =>
    $"Error while publishing route strategy {operationType} message. " +
    $"Route Metadata ID: {routeMetadata.NavigationProfileId}, " +
    $"Transportation mode: {routeStrategy.TransportationMode}, " +
    $"Inner exception: {ex.Message}, Stack trace: {ex.StackTrace}.";
```

Placing this as a `static` method:

1. **Testable**: Can assert the exact format in unit tests without running actual publishing
2. **Consistent**: All three operations (`created`, `updated`, `deleted`) share format — log parsers/alerts work for all
3. **No state dependency**: `static` makes it clear this is a pure transformation function

### Exceptions are not swallowed

No `catch` block swallows exceptions. Every failure is:

1. Logged with full context
2. Rethrown to the caller

This is intentional. The retry and dead-letter policies live in the message handler infrastructure (e.g., Azure Service Bus retry policy or a Polly pipeline), not in the publishing code. Swallowing exceptions would silently drop messages.

---

## 5. Extensibility Analysis

### The Extension Checklist for a New Transportation Mode

To add, for example, `TaxiRouteChangedPublisher`:

1. **Create message classes** (in `Contract.Messaging`)
   ```csharp
   public abstract class TaxiRouteChangedMessage : RouteStrategyChangedBaseMessage
   {
       public string Hash { get; set; }
       public IEnumerable<TaxiPreferenceDto>? TaxiPreferences { get; set; }
   }
   public class TaxiRouteCreatedMessage : TaxiRouteChangedMessage { }
   public class TaxiRouteUpdatedMessage : TaxiRouteChangedMessage { }
   public class TaxiRouteDeletedMessage : TaxiRouteChangedMessage { }
   ```

2. **Create the marker interface** (in `Abstractions/Publishers`)
   ```csharp
   public interface ITaxiRoutePublisher : IPublishingStrategy { }
   ```

3. **Create the publisher class** (in `Publishers`)
   ```csharp
   public class TaxiRouteChangedPublisher : ITaxiRoutePublisher
   {
       // PublishCreated, PublishUpdated, PublishDeleted — delegation pattern
       // CreateDetailedMessage<T> — taxi-specific transformation
       // CreateDeletedMessage — base metadata only
   }
   ```

4. **Register in DI** (`ServiceRegistration.cs`) — one line:
   ```csharp
   services.AddTransient<ITaxiRoutePublisher, TaxiRouteChangedPublisher>();
   ```

5. **Register in factory** (`RoutingStrategyPicker.cs`) — one line:
   ```csharp
   _strategies.TryAdd(nameof(NavigationProfile.TaxiRoute),
       _serviceProvider.GetRequiredService<ITaxiRoutePublisher>());
   ```

6. **Add AutoMapper profiles** for `TaxiPreferenceDto` mapping (if preferences are complex)

7. **Write tests** for the new publisher (see [Testing Strategy](#6-testing-strategy))

**Zero changes to**:
- `RouteStrategyChangedPublishService`
- `PublishingSupporter`
- Any existing publisher
- `IPublishingStrategy`
- Infrastructure layer

### Extension Points Summary

| Extension point | Effort | Risk to existing code |
|---|---|---|
| New transportation mode | ~30 min (new class + 2 lines) | Zero |
| New operation type (e.g., `PublishArchived`) | Medium (add to interface + all publishers) | Low (interface change is additive) |
| New base message field | Low (add to `RouteStrategyChangedBaseMessage`) | Low (additive, backwards-compatible) |
| New publishing infrastructure | Low (replace `IMessagePublisher` impl) | Zero (interface-only dependency) |
| Replace AutoMapper with manual mapping | Medium (rewrite projection in `PublishingSupporter`) | Zero to existing classes |

### Phased Rollout Support

The Null Object pattern enables **feature-flagged rollout** of new modes:

```csharp
// Phase 1: Register as null — mode exists in domain, but no publishing yet
_strategies.TryAdd(nameof(NavigationProfile.TaxiRoute), null!);

// Phase 2: Replace null with real publisher — zero downtime, no code change elsewhere
_strategies.TryAdd(nameof(NavigationProfile.TaxiRoute),
    _serviceProvider.GetRequiredService<ITaxiRoutePublisher>());
```

Because the facade silently returns on `null`, phase 1 produces no messages and no errors. The swap in phase 2 is surgical.

---

## 6. Testing Strategy

### Unit Test Structure per Publisher

Each concrete publisher follows the same test structure:

```csharp
public class WalkingRouteChangedPublisherTests
{
    private readonly Mock<IPublishingSupporter> _supporter = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly WalkingRouteChangedPublisher _sut;

    public WalkingRouteChangedPublisherTests()
    {
        _sut = new WalkingRouteChangedPublisher(_supporter.Object, _mapper.Object);
    }

    [Fact]
    public async Task PublishCreated_CallsSupporterWithCorrectDelegate()
    {
        // Arrange
        var strategy = WalkingRouteStrategyFactory.Create();
        var metadata = RouteMetadataFactory.Create();

        // Act
        await _sut.PublishCreated(strategy, metadata, CancellationToken.None);

        // Assert
        _supporter.Verify(s => s.Publish(
            It.IsAny<Func<RouteStrategyChangedBaseMessage, RouteStrategy, WalkingRouteCreatedMessage>>(),
            strategy, metadata, CancellationToken.None), Times.Once);
    }
}
```

Since `PublishingSupporter` is mocked, the publisher test is purely about:
- Was `Publish` called?
- With the correct message type?
- With the correct input parameters?

The transformation logic in `CreateDetailedMessage` is tested separately with dedicated tests that invoke it directly (via `protected internal` or `internal` visibility + `InternalsVisibleTo`).

### Unit Test Structure for PublishingSupporter

```csharp
public class PublishingSupporterTests
{
    private readonly Mock<IMessagePublisher> _publisher = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly Mock<ILogger<PublishingSupporter>> _logger = new();
    private readonly PublishingSupporter _sut;

    [Fact]
    public async Task Publish_WhenDelegateSucceeds_CallsPublisherOnce()
    {
        // Arrange
        Func<RouteStrategyChangedBaseMessage, RouteStrategy, WalkingRouteCreatedMessage>
            fillDetails = (baseMsg, _) => new WalkingRouteCreatedMessage { /* ... */ };

        // Act
        await _sut.Publish(fillDetails, strategy, metadata, CancellationToken.None);

        // Assert
        _publisher.Verify(p => p.PublishAsync(
            It.IsAny<WalkingRouteCreatedMessage>(),
            metadata.NavigationProfileId,
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Publish_WhenPublisherThrows_LogsAndRethrows()
    {
        // Arrange
        _publisher.Setup(p => p.PublishAsync(/*...*/).Throws(new Exception("bus error")));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() =>
            _sut.Publish(fillDetails, strategy, metadata, CancellationToken.None));

        _logger.Verify(l => l.LogError(/*...*/), Times.Once);
    }
}
```

### Unit Test Structure for RoutingStrategyPicker

```csharp
[Fact]
public void Pick_WalkingRoute_ReturnsWalkingPublisher()
{
    var services = new ServiceCollection();
    services.AddTransient<IWalkingRoutePublisher, WalkingRouteChangedPublisher>();
    /* register all required publishers */
    var picker = new RoutingStrategyPicker(services.BuildServiceProvider());

    var strategy = new RouteStrategy { TransportationMode = nameof(NavigationProfile.WalkingRoute) };
    var result = picker.Pick(strategy);

    Assert.IsAssignableFrom<IWalkingRoutePublisher>(result);
}

[Fact]
public void Pick_NullObjectMode_ReturnsNull()
{
    /* ... register PedestrianAccessibility as null ... */
    var result = picker.Pick(new RouteStrategy
        { TransportationMode = nameof(NavigationProfile.PedestrianAccessibility) });

    Assert.Null(result);
}
```

### Integration Test Strategy

Integration tests focus on the full pipeline from facade to message bus:

```csharp
[Fact]
public async Task ProcessRouteStrategyCreatedEvent_WalkingMode_PublishesExpectedMessage()
{
    // Arrange: real publishers, real supporter, in-memory message bus
    var capturedMessages = new List<object>();
    var inMemoryPublisher = new CapturingMessagePublisher(capturedMessages);

    // Use real DI container with in-memory publisher
    var services = BuildTestServiceCollection(inMemoryPublisher);
    var sut = services.GetRequiredService<IRouteStrategyChangedPublishService>();

    // Act
    await sut.ProcessRouteStrategyCreatedEvent(
        WalkingRouteStrategyFactory.Create(),
        RouteMetadataFactory.Create(),
        CancellationToken.None);

    // Assert
    var message = Assert.Single(capturedMessages);
    var walkingMsg = Assert.IsType<WalkingRouteCreatedMessage>(message);
    Assert.Equal(nameof(WalkingRouteCreatedMessage), walkingMsg.MessageType);
    Assert.NotNull(walkingMsg.WalkingPreferences);
}
```

### Test Coverage Priorities

| Component | Priority | Why |
|---|---|---|
| `CreateDetailedMessage<T>` (per publisher) | **High** | This is the unique business logic |
| `PublishingSupporter.Publish` error path | **High** | Exception handling correctness |
| `RoutingStrategyPicker.Pick` (all modes) | **High** | Registry completeness |
| `RouteStrategyChangedPublishService` (delegation) | **Medium** | Mostly structural |
| Message class serialization | **Medium** | JSON contract stability |
| DI wiring | **Low** | Verified by integration tests |

---

## 7. Performance Considerations

### Strategy Registry — O(1) Lookup

The `ConcurrentDictionary` provides O(1) average-case lookup after initial population. For a registry of 16 transportation modes, even an O(n) scan would be negligible, but O(1) dictionary lookup removes any concern at scale.

### Lazy Initialization — Cold Start

The first call to `RoutingStrategyPicker.Pick()` triggers `InitializeStrategies()`, which calls `GetRequiredService<T>()` for every registered publisher. In a serverless host (Azure Functions), this happens on cold start. For 16 publishers, the cost is the DI resolution overhead × 16, which is typically < 1ms.

Subsequent calls (warm path) bypass initialization entirely and execute a single dictionary lookup.

### Memory Footprint

Publishers are registered as `Transient` in DI but stored in the `ConcurrentDictionary` as long-lived instances. This is effectively a `Singleton`-by-reference model for the publisher instances held by the picker. Publishers are stateless, so there are no thread-safety concerns, but this should be documented as an intentional choice to avoid future "why is this Transient but acting like Singleton?" confusion.

### Async All the Way Down

All `Publish` paths are `async Task` from facade to infrastructure. There is no `.Result` or `.Wait()` anywhere in the call chain. This avoids thread-pool starvation under concurrent load, which is the most common async performance anti-pattern in .NET.

### AutoMapper Mapping Profiles

AutoMapper mappings are compiled to expression trees at startup, making subsequent `Map<T>()` calls nearly as fast as hand-written code. The one-time startup cost is acceptable for a long-running service. For serverless scenarios with aggressive scale-to-zero, the startup profile compilation adds a few milliseconds to cold starts but is still negligible in practice.

---

## 8. Alternatives Considered

### Alternative A: MediatR Pipeline per Transportation Mode

Each transportation mode could publish a `WalkingRouteCreatedRequest` and have a dedicated MediatR handler:

```csharp
await _mediator.Send(new WalkingRouteCreatedRequest(routeStrategy, metadata));
```

**Rejected because**: MediatR introduces a service-locator pattern where the relationship between request and handler is invisible at the call site. It also complicates error propagation and adds a dependency for no structural gain over direct injection. The Strategy pattern makes the selection logic explicit and testable.

### Alternative B: Generic `PublishingService<TStrategy, TMessage>`

A single generic service could handle all modes:

```csharp
public class PublishingService<TStrategy, TMessage>
    where TStrategy : IPublishingStrategy
    where TMessage : RouteStrategyChangedBaseMessage
{ ... }
```

**Rejected because**: Generic type parameters would require the caller to know the strategy and message types at compile time, defeating the runtime-dispatch purpose of the Strategy pattern. The factory abstraction exists precisely to avoid this coupling.

### Alternative C: Event Sourcing / Domain Events

Route strategy changes could be modeled as domain events dispatched to an in-process event bus, decoupled from the publishing infrastructure.

**Considered but deferred**: The current system is primarily a translation layer (domain objects → external messages). Adding a full event sourcing layer would be over-engineering for the present scope. The architecture is compatible with this evolution — the facade would become an event subscriber.

### Alternative D: Reflection-based Strategy Discovery

Strategies could be auto-discovered at startup via reflection:

```csharp
var publishers = Assembly.GetExecutingAssembly().GetTypes()
    .Where(t => typeof(IPublishingStrategy).IsAssignableFrom(t))
    .Select(t => (IPublishingStrategy)ActivatorUtilities.CreateInstance(services, t));
```

**Rejected because**: Reflection-based discovery is opaque and harder to debug. The explicit registration in `InitializeStrategies()` doubles as documentation — a developer can read it and immediately understand every supported transportation mode. Reflection hides this.

---

## 9. Lessons Learned

### What worked extremely well

**1. The delegate-based Template Method**  
Passing `CreateDetailedMessage<T>` as a method group to `_supporter.Publish(...)` is the single most impactful design choice. It made `PublishingSupporter` a true "publish for free" infrastructure that concrete publishers can use without any base class or interface beyond `IPublishingSupporter`. New publishers are almost entirely copy-paste of the delegation scaffolding + one unique method.

**2. `nameof()` for registry keys**  
Switching from string literals to `nameof(NavigationProfile.WalkingRoute)` eliminated an entire category of bugs. The registry keys and the domain model stay in sync at compile time.

**3. Null Object entries in the registry**  
Rather than having the factory throw on unsupported modes and forcing callers to handle exceptions, registering `null` as an explicit value for known-but-unsupported modes allowed the facade's null guard to handle them gracefully. This made phased rollout trivial.

**4. Static error-message factory**  
The `static string ExceptionMessage(...)` method seems like a small thing, but consistent log structure across all operations was invaluable when debugging production incidents. All three operations produce the same log shape, and the `TransportationMode` field is always present for filtering.

### What could be improved

**1. `InitializeStrategies()` thread safety window**  
There is a short race condition window: if `_strategies.IsEmpty` is checked concurrently before the first initialization completes, `InitializeStrategies()` could be called multiple times. `ConcurrentDictionary.TryAdd` is idempotent, so no data corruption occurs, but redundant DI resolutions could happen. A `Lazy<bool>` initialization guard would eliminate even this theoretical concern.

**2. Publisher instances are effectively Singleton**  
Publishers are `Transient` in DI but stored in the `ConcurrentDictionary` indefinitely. The documentation should make this lifetime reality explicit to avoid future developers "fixing" the lifetime to `Singleton` or, worse, relying on per-request state in publishers.

**3. AutoMapper for base message projection**  
The `_mapper.Map<T>(baseMessage)` call that projects the base message into the derived type works correctly but feels like an unusual AutoMapper usage (base-to-derived projection). An explicit copy constructor or `with` expression on `record` types would be more transparent. This is a refactoring candidate if/when the project migrates to C# records.

---

*This document is the Tier 3 reference for the event-driven route publishing architecture. For a 10-minute introduction, see [PATTERN_OVERVIEW.md](PATTERN_OVERVIEW.md). For an executive summary, see [README.md](README.md).*
