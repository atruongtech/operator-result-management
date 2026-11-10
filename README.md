# Operator Result Management

A .NET 10 library for describing reusable processes, creating their per-run operations, and sharing asynchronous results between dependent steps.

The central distinction is **definition versus execution**:

- A **process** describes which steps belong to a reusable definition.
- An **instruction** describes one step and knows how to create its operation in a context.
- An **operation** is the executable instance of that step for a particular run.
- An **execution context** owns the registered operation graph for that run.

Instructions can be immutable or mutable. The context preserves instruction-to-operation associations by object identity; it does not deep-copy instruction definitions.

## What each abstraction represents

| Abstraction | Responsibility | What it does not represent |
|---|---|---|
| `IProcess<TContext>` / `ProcessBase<TContext>` | A reusable collection of instructions and their dependency graph. | An active run, a UI model, or a sequential execution schedule. |
| `IInstruction<TContext>` | A defined step, its configuration, and its direct upstream dependencies. Registers its operation and dependencies in the supplied context. | The execution of that step or its result. |
| `IInstruction<TResult, TContext>` | An instruction whose operation produces a typed result. | A result value stored on the definition. |
| `IExecutionContext` / `ExecutionContextBase` | One run's operation registry, shared cancellation token, and execution lifecycle. A derived context can supply run-specific services. | A historical snapshot of instruction configuration. |
| `IOperationInstance<TResult>` / `OperationInstanceBase<TResult>` | The run-specific work, captured configuration, upstream operation references, and eventual result. | A reusable process definition. |
| `IOperationResult<TResult>` / `ImmediateResult<TResult>` | The published value of a successful operation, accessed through `GetResult()`. | The instruction or the execution lifecycle. |
| `ConditionalInstruction<TContext>` | A ready-to-use condition and a separately registered, owned subprocess body. | A value selector or permission to expose body-local operations. |
| `IOperationInstance` / non-generic `OperationInstanceBase` | Execute-once work with an observation-only `Completion` task. | A typed result value. |
| `ProcessRunRequest<TContext>` | A fixed selection of instruction references to register, either the whole process or a subset. | A deep snapshot of definitions or a guarantee of identical results when replayed. |

Most types use the `operator_result_management` namespace. Both forms of `OperationInstanceBase` and `ImmediateResult<TResult>` use `operator_result_management.Basic`.

### Process: membership, not scheduling

`ProcessBase<TContext>.Add` discovers an instruction's transitive dependencies, validates context compatibility and cycles, and adds distinct instruction objects in dependency-first insertion order. Adding the same object again does not duplicate it.

`Append` creates a dependent step from an existing process instruction. The returned instruction must declare that input as a direct dependency.

The instruction collection is immutable, but its elements need not be. Adding steps replaces the collection; previously returned collections keep their membership. Collection order is not a promise of sequential execution. Data dependencies determine when a step can produce its result.

Selection, UI notifications, and editing workflows belong to the client.

### Instruction: identity and registration

An instruction declares every direct upstream instruction whose operation it consumes in `Dependencies`. During registration, it resolves those dependencies in the same context and calls `GetOrAddOperation(this, factory)` for its own operation.

Sharing uses **reference identity**, not `Id` or overridden equality:

- The same instruction object shares one operation within a context.
- Separate instruction objects remain separate steps, even with identical configuration or the same `Id`.
- A fresh context creates independent operations for a new run.

`IInstruction.Id` is stable metadata useful for client-side correlation. It is not a registry key or a library-enforced uniqueness constraint.

Registration constructs the graph; it must not start or wait for execution. Operation factories must capture the run's configuration and upstream operation references so later instruction edits cannot change the registered work.

### Operation: work and its eventual result

Every `IOperationInstance` exposes `Completion` to observe success, failure, or cancellation **without starting execution**, regardless of its result type. `Execute()` starts an operation and returns that exact task: `ReferenceEquals(operation.Completion, operation.Execute())` is always true. With either `OperationInstanceBase`, execution happens once per instance. A failure or cancellation is not retried.

`Promise()` observes the eventual result **without starting execution**. Dependent operations await upstream promises instead of running separate copies of upstream work.

Typed `OperationInstanceBase<TResult>` uses its result promise as the shared completion and execution task; `Promise()` retains typed value access. Dependencies that need only completion can await `Completion` through `IOperationInstance`, without casts.

The base class publishes a result only after `ExecuteCoreAsync` succeeds. Execution failures and cancellation propagate through both `Execute()` and `Promise()`. Returning a null result is an execution failure.

All consumers receive the same published result. Prefer immutable result values; coordinate access if sharing mutable values. `ImmediateResult<T>` wraps a value without deep-copying it.

**Breaking interface change:** `ICompletionOperationInstance` has been removed. Custom `IOperationInstance` implementations must expose an observation-only `Completion` task and return that same task from `Execute()`.

## Define and run a process

Compose instructions with `Add` and `Append`, create a fresh execution context, and register the process before executing it. The repository's [configurable number pipeline](configurable-number-pipeline-poc/Program.cs) serves as the usage example.

`Register(process)` registers every instruction currently in the process. Registering an individual instruction afterward returns its existing operation; it does not create or execute another one. Use typed registration to obtain an operation whose result you can observe through `Promise()`.

`ExecuteAsync()` freezes registration, starts all distinct registered operations, and awaits them all. Independent branches can progress concurrently; downstream operations wait for the upstream results they need. New registrations are rejected once context execution starts, but existing lookups remain valid. Repeated context execution returns the same task, including after failure or cancellation.

The caller owns and disposes the `CancellationTokenSource`. Operations capture the context's token and must observe it during asynchronous waits and long-running work. Cancellation is cooperative; it does not forcibly interrupt arbitrary work.

Calling an individual operation's `Execute()` starts only that operation, not the entire registered graph. Prefer context execution when dependencies must also start. Use a fresh context for a new run or a retry.

### Run only selected steps

Create a `ProcessRunRequest<TContext>` with the process and the desired instruction selection, then register it in a fresh context. A selection registers its selected instructions and their transitive upstream dependencies, not downstream consumers or unrelated process members. Selected instructions need not be graph sinks. An empty selection registers no work.

Use a fresh context for a partial run: context execution also starts anything else already registered there. A request fixes selected references, not their configuration or dependencies. Replaying it in a fresh context uses their state at registration time.

## Conditional subprocesses

`ConditionalInstruction<TContext>` defines a completion-only conditional instruction
with an owned `IProcess<TContext>` body. `TContext` must derive from `ExecutionContextBase`.
Use it directly by supplying a factory that creates a fresh body context with the parent's
cancellation token and the services required by the body:

```csharp
var conditional = new ConditionalInstruction<ExecutionContextBase>(
    condition,
    body,
    bodyContextFactory: parent => new ExecutionContextBase(parent.CancellationToken));
```

For a custom context, the factory can also pass run-specific services from the parent.
The class is unsealed; consumers can derive from it and override the protected virtual
`CreateBodyContext` hook when needed. Its default implementation calls the required
constructor-supplied factory.

Add the conditional to the parent process, not its body instructions. Its parent
dependencies are the Boolean condition and explicitly declared outer inputs.
Body membership is separate: `ProcessBase.Add` does not flatten it into the parent.

Parent registration constructs the entire body, including nested conditional bodies,
and freezes each body's registry before execution. Instruction implementations must
capture configuration and upstream operations as usual. Later edits to body membership,
configuration, or dependencies apply only to fresh runs. Registration errors are still
reported for a body whose condition would be false.

At execution, the conditional awaits its condition. A false result completes successfully
without starting the body. A true result executes the child context and completes only
after all its owned operations finish. Body failure or cancellation propagates through
the conditional to the parent run. Independent parent operations remain concurrent;
this is not a sequential statement scheduler.

### Inputs and scope boundaries

- Pass outer instructions through `outerInputs`. The condition and the transitive
  dependencies of all declared inputs are also available as imports. Body instructions
  use their normal `GetTypedOperation(context)` calls; imported lookups return the
  existing outer operation, not a second copy.
- Imports are not child-owned operations and are excluded from the child's `Operations`
  mapping and execution list. They execute in their owning outer scope even if the body
  is not taken. Put work inside the body if it should execute only when the condition is true.
- Body-local instructions cannot also be registered in a parent or sibling scope.
  Cross-scope references without an explicit outer-input binding are rejected during
  registration, regardless of registration order. Reuse an outer input to share work;
  use distinct instruction instances for separate body-local steps.
- Nested bodies obey the same rules. Recursive containment and dependencies crossing
  back into an enclosing conditional are rejected. Scope checks occur at registration;
  process dependency validation alone does not traverse body containment.

The body-context factory (or overridden `CreateBodyContext`) must return a fresh, unused
context exclusively owned by the body. The factory runs during registration, even when
the condition will be false, and is not called again for existing operation lookups.
Do not register work in that factory or expose the child context to consumers: the
class handles registration, imported lookups, freezing, and execution.

### Completion without a value

`conditional.GetOperation(context)` returns `IOperationInstance`, not a typed
result operation. Its `Completion` task observes success, failure, or cancellation without
starting execution. Start the owning parent context as usual:

```csharp
var operation = conditional.GetOperation(context);
await context.ExecuteAsync();
await operation.Completion;
```

A downstream instruction can declare the conditional as a dependency, capture its
operation during registration, and await `operation.Completion.WaitAsync(cancellationToken)`
inside its own execution. Declaring a dependency alone does not impose ordering.
No body results or executable operation references are exported.

The non-generic `operator_result_management.Basic.OperationInstanceBase` supports other
completion-only operations with the same execute-once and cancellation semantics.
Unstarted body operations retain pending promises, but remain private and are never
awaited by the parent run. A false conditional is successful, not failed or canceled.
The number-pipeline POC does not yet expose conditional creation.

## Mutable or immutable instructions

The client chooses the instruction editing model. **Run isolation is required in either model:** once an operation is registered, later definition edits must not change its configuration or upstream operation graph.

| Client choice | What an old context's instruction references expose |
|---|---|
| Mutate existing instruction objects | The current state of those same objects, including later edits. |
| Use immutable instructions and create replacements on edit | The original definitions registered in that context. Future runs can use the replacements. |

Immutable instructions provide historical fidelity only if their relevant configuration and dependencies are also immutable. Getter-only properties do not make referenced objects immutable.

Mutable instructions work well for editable definitions, provided operation construction captures values rather than reading the instruction later during execution. Copy mutable configuration objects when retaining their references would allow edits to leak into a run. The library does not perform that copying for you.

Registering the changed instruction again in the original context still returns its original operation. Edits take effect when operations are created in a fresh context, not by refreshing an existing registration.

Clients must coordinate edits with process dependency traversal and registration; registry locking does not synchronize arbitrary instruction setters. Dependencies must remain stable during those activities.

In-place dependency edits do not automatically update process membership, ordering, or validation. Calling `Add` again discovers and validates the current dependency graph, but it does not remove previously added instructions or reorder existing entries. For structural edits requiring a clean membership/order, build a new process definition from the desired graph. `ProcessBase` has no removal or replacement API.

## Traceability through the operations mapping

`context.Operations` is an instruction-to-operation identity map. Each access returns a read-only **snapshot of registry membership**, while keys and values remain the original objects. It is not a historical instruction-state snapshot.

The association means:

> This instruction object registered this operation in this context.

It does not necessarily mean:

> The instruction's currently displayed configuration produced this operation's result.

For reverse lookup, compare operation references and retain all matching instructions.

Multiple instructions may register the same operation; the context executes that distinct operation once. A single operation-to-instruction property would not describe every supported association.

The mapping retains instruction objects while the context or a returned mapping snapshot remains reachable. For durable historical records beyond those objects' lifetimes, clients can capture instruction metadata/configuration at registration or retain immutable definition revisions. An instruction ID alone neither snapshots configuration nor uniquely identifies an execution. Run IDs, operation IDs, definition revisions, and persistent provenance records are client concerns, not built-in features.

## Context-typed instruction factories

`IInstructionFactory<TContext>` binds candidate inputs and returns a
`BindingResponse<TContext>`. A successful `ReadyResponse<TContext>` exposes a
`Create` delegate returning `IInstruction<TContext>`, so callers can add the
constructed instruction directly to `IProcess<TContext>` without casting.
`FailedResponse<TContext>` reports why the inputs cannot be bound.

Inputs remain `IReadOnlyList<IInstruction>` because factories validate their
required input count, result types, and context compatibility at binding time.
`InstructionFactoryBinding` retains untyped instruction references for display.
The factory registry and selection helper must preserve `TContext` through to
creation; different result types can share the same context-typed registry.

Consumers must
specify the execution context type when implementing factories or handling responses.

## Repository example

The [configurable number pipeline](configurable-number-pipeline-poc/Program.cs) demonstrates a derived execution context supplying user-interface services, typed process composition, and cancellation.

In VS Code, select **Configurable number pipeline** in the Run and Debug dropdown, then use **Run > Run Without Debugging** (Ctrl+F5). The launch configuration builds the solution first and runs the app in the integrated terminal for interactive input. **Ctrl+Shift+B** builds the solution without running the app.
