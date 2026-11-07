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
| `ProcessRunRequest<TContext>` | A fixed selection of instruction references to register, either the whole process or a subset. | A deep snapshot of definitions or a guarantee of identical results when replayed. |

Most types use the `operator_result_management` namespace. `OperationInstanceBase<TResult>` and `ImmediateResult<TResult>` use `operator_result_management.Basic`.

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

`Execute()` starts an operation. With `OperationInstanceBase<TResult>`, execution happens once per instance, and every call returns the same task. A failure or cancellation is not retried.

`Promise()` observes the eventual result **without starting execution**. Dependent operations await upstream promises instead of running separate copies of upstream work.

The base class publishes a result only after `ExecuteCoreAsync` succeeds. Execution failures and cancellation propagate through both `Execute()` and `Promise()`. Returning a null result is an execution failure.

All consumers receive the same published result. Prefer immutable result values; coordinate access if sharing mutable values. `ImmediateResult<T>` wraps a value without deep-copying it.

## Define and run a process

Compose instructions with `Add` and `Append`, create a fresh execution context, and register the process before executing it. The repository's [configurable number pipeline](configurable-number-pipeline-poc/Program.cs) serves as the usage example.

`Register(process)` registers every instruction currently in the process. Registering an individual instruction afterward returns its existing operation; it does not create or execute another one. Use typed registration to obtain an operation whose result you can observe through `Promise()`.

`ExecuteAsync()` freezes registration, starts all distinct registered operations, and awaits them all. Independent branches can progress concurrently; downstream operations wait for the upstream results they need. New registrations are rejected once context execution starts, but existing lookups remain valid. Repeated context execution returns the same task, including after failure or cancellation.

The caller owns and disposes the `CancellationTokenSource`. Operations capture the context's token and must observe it during asynchronous waits and long-running work. Cancellation is cooperative; it does not forcibly interrupt arbitrary work.

Calling an individual operation's `Execute()` starts only that operation, not the entire registered graph. Prefer context execution when dependencies must also start. Use a fresh context for a new run or a retry.

### Run only selected steps

Create a `ProcessRunRequest<TContext>` with the process and the desired instruction selection, then register it in a fresh context. A selection registers its selected instructions and their transitive upstream dependencies, not downstream consumers or unrelated process members. Selected instructions need not be graph sinks. An empty selection registers no work.

Use a fresh context for a partial run: context execution also starts anything else already registered there. A request fixes selected references, not their configuration or dependencies. Replaying it in a fresh context uses their state at registration time.

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

## Repository example

The [configurable number pipeline](configurable-number-pipeline-poc/Program.cs) demonstrates a derived execution context supplying user-interface services, typed process composition, and cancellation.

In VS Code, select **Configurable number pipeline** in the Run and Debug dropdown, then use **Run > Run Without Debugging** (Ctrl+F5). The launch configuration builds the solution first and runs the app in the integrated terminal for interactive input. **Ctrl+Shift+B** builds the solution without running the app.
