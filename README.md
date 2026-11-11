# Operator Result Management

A .NET 10 library for reusable dependency graphs, per-run operations, and shared asynchronous results. The library manages registration and execute-once behavior; your application supplies the work, services, and UI. Persistence, retries, rollback, and execution history are application concerns.

The [configurable number pipeline proof of concept (POC)](configurable-number-pipeline-poc/Program.cs) demonstrates editable graphs. The [drink ordering POC](drink-ordering-poc/Program.cs) demonstrates stateful services and conditional subprocesses. Their console services and domain instructions are sample code, not library features.

## Getting started

Install the **.NET 10 SDK**. From the repository root:

## Configurable number pipeline proof of concept
```powershell
dotnet build .\operator-result-management.slnx
dotnet run --project .\configurable-number-pipeline-poc\configurable-number-pipeline-poc.csproj
```
This example project demonstrates a workflow in which a process can be configured
at runtime. Consuming applications might use the library to expose functionality in which users can define custom processes, execute them, and iterate the definition while inspecting results.

The POC's initial pipeline reads an integer, multiplies it, factors the product, and calculates the difference between the largest and smallest factors. Its menu lets users execute the process or add instructions compatible with selected upstream steps. Each run uses a fresh context.

The POC is interactive, not a batch CLI. Factoring requires a positive number, and the difference step requires a nonempty list. Multiplication does not check overflow; non-cancellation errors propagate out of the app. **Ctrl+C** requests cancellation during execution, but cannot interrupt a synchronous console read.

In VS Code, select **Configurable number pipeline** and press `Ctrl+F5`; `Ctrl+Shift+B` builds without running.

## Drink ordering proof of concept

```powershell
dotnet run --project .\drink-ordering-poc\drink-ordering-poc.csproj
```
This example project demonstrates using the library is to define fixed processes. The application establishes two distinct workflows with different steps and dependencies. The use of the library types can provide abstraction of dependency registration to allow consumers to focus on the step definitions.

| Workflow | Sizes | Initial state and replenishment |
|---|---|---|
| Orange juice | Small: 1 orange; medium: 2; large: 3 | Hopper starts full with 6 oranges. Insufficient stock triggers a refill to 6 before production. Pulp setting starts enabled and persists until changed by a customer's selection. |
| Coffee | Small: 4 oz; large: 8 oz | Carafe starts empty. Insufficient coffee triggers a cancellable, asynchronous 3-second brew that adds 12 oz, preserving leftovers. |

[OrangeJuiceProcess](drink-ordering-poc/OrangeJuiceProcess.cs) composes **take size order -> check hopper -> conditionally fill hopper -> choose pulp and set machine -> produce juice -> return juice**. [CoffeeProcess](drink-ordering-poc/CoffeeProcess.cs) composes **take size order -> check carafe -> conditionally brew -> pour serving -> ask for and add cream -> ask for and add sugar -> return coffee**. Both publish immutable typed drink results that include the chosen pulp setting or cream and sugar counts.

Replenishment uses the library's `ConditionalInstruction<DrinkOrderExecutionContext>` with a completion-only body instruction. Fresh child contexts share the parent's machines, console, and cancellation token. Pulp selection waits for the refill conditional, and production waits for pulp selection. Pouring waits for the brew conditional; cream waits for pouring, sugar waits for cream, and returning waits for sugar. The console displays checks, replenishment, remaining stock, the persistent pulp setting, and each drink's customizations.

In VS Code, select **Drink ordering** and press `Ctrl+F5`.

```powershell
dotnet test .\drink-ordering-poc.Tests\drink-ordering-poc.Tests.csproj
```

## Referencing the library

To integrate the library, add a project reference, replacing the application path:

```powershell
dotnet add C:\path\YourApp\YourApp.csproj reference .\operator-result-management\operator-result-management.csproj
```

Use `operator_result_management` for processes, instructions, contexts, and result contracts; use `operator_result_management.Basic` for operations, result wrappers, and factories. The examples assume the repository's implicit `System`, threading, and task usings.

## Execution model

Keep reusable **definitions** separate from each **run**:

| Type | Responsibility |
|---|---|
| `ProcessBase<TContext>` | Instruction membership, composed through `Add`. |
| `InstructionBase<TContext>` / `InstructionBase<TResult, TContext>` | Declare dependencies and create completion-only or typed-result operations. |
| `ExecutionContextBase` | One run's operation registry, cancellation token, and execution lifecycle. Derive to supply services. |
| `OperationInstanceBase` / `OperationInstanceBase<TResult>` | Captured run-specific work and shared completion/result tasks. |
| `IOperationResult<TResult>` / `ImmediateResult<TResult>` | Published value, read through `GetResult()`. |
| `ProcessRunRequest<TContext>` | A selection of instruction references to register. |

The lifecycle is **compose -> register -> execute -> observe**. Registration creates operations and discovers upstream dependencies; it does not execute work.

**`Promise()` and `Completion` observe work without starting it.** Start the registered graph with `context.ExecuteAsync()`. Consumers must await upstream results or completion themselves: dependency declarations and process list order do not enforce sequencing.

The same instruction object shares one operation within a context. Separate objects remain distinct regardless of equality or `Id`. Use a **fresh context for every run or retry**; repeated execution returns the original task.

Keep `TContext` consistent across instructions, processes, and factories. Generic bases are invariant, even when their context types have an inheritance relationship.

## Compose, run, and read a result

This single-run version of the [POC's composition](configurable-number-pipeline-poc/Program.cs) uses its context and instruction classes. It belongs in a project containing those sample classes, not an application referencing only the library.

```csharp
using configurable_number_pipeline_poc;
using operator_result_management;
using operator_result_management.Basic;

var process = new ProcessBase<NumberPipelineExecutionContext>();
var input = process.Add(new DirectInputInstruction());
var multiply = process.Add(new UserInputMultiplyInstruction(input));
var factor = process.Add(new FactorInstruction(multiply));
var difference = process.Add(new MinMaxDifferenceInstruction(factor));

using var cancellation = new CancellationTokenSource();
var context = new NumberPipelineExecutionContext(
    new UserInterfaceProvider(), cancellation.Token);

context.Register(process);
OperationInstanceBase<int> resultOperation = context.Register(difference);

await context.ExecuteAsync();
int value = (await resultOperation.Promise()).GetResult();
Console.WriteLine($"Final result: {value}");
```

`Register(process)` registers all current members. The subsequent `Register(difference)` retrieves the existing typed operation without duplicating work. The POC's full `Program.cs` also wires and removes a `Console.CancelKeyPress` handler.

`Add<TInstruction>` adds only the supplied instruction and returns that exact instance, preserving its static type. All direct and transitive dependencies must already be members of the same process by reference identity. Add inputs before their consumers; adding only a graph's sink does not implicitly include its upstream steps. Instructions registered in an execution context or added to another process do not satisfy this requirement.

Missing dependencies, cycles, and invalid dependency metadata throw `InvalidOperationException` before process membership changes. Repeated additions still validate the current dependency graph, but do not duplicate or reorder members. This applies to typed-result and completion-only instructions alike; callers can ignore the return value.

`Add` does not enforce execution ordering; operations must still await their dependencies. These checks apply to process composition, not execution-context registration. `context.Register(instruction)` and run selections still resolve upstream operations for the current run.

## Implement your own steps

Follow these POC patterns, replacing console services and arithmetic with your domain work:

1. **Supply services.** Derive from `ExecutionContextBase`, as [NumberPipelineExecutionContext](configurable-number-pipeline-poc/NumberPipelineExecutionContext.cs) does, or use the base directly. The caller owns service lifetimes and the cancellation source; services shared by concurrent operations need appropriate coordination.
2. **Declare dependencies.** Derive from `InstructionBase<TResult, TContext>` and pass direct inputs to its base constructor. In `CreateOperation(context, inputs)`, capture their operations through `inputs.GetOperation(input)`. Undeclared inputs are rejected.
3. **Capture run state.** Copy configuration and retain upstream operations during creation. Constructors and creation hooks must not perform work, start execution, or wait for results.
4. **Execute and publish.** Derive the operation from `OperationInstanceBase<TResult>`, call `base(context)`, and override `ExecuteResultAsync(CancellationToken)`. Await upstream `Promise().WaitAsync(cancellationToken)`, then return an `IOperationResult<TResult>`, usually `new ImmediateResult<TResult>(value)`.

See [DirectInputInstruction](configurable-number-pipeline-poc/DirectInputInstruction.cs) for no inputs, [UserInputMultiplyInstruction](configurable-number-pipeline-poc/UserInputMultiplyInstruction.cs) for one input, and [DistributeInstruction](configurable-number-pipeline-poc/DistributeInstruction.cs) for mixed-type inputs.

For **completion-only work**, use `InstructionBase<TContext>` and non-generic `OperationInstanceBase`; override `ExecuteCoreAsync(CancellationToken)`. Consumers await `operation.Completion.WaitAsync(cancellationToken)`. This also works when a typed operation's value is not needed.

Override creation/work hooks, not library-owned lifecycle methods. Never await your own operation's completion. Null tasks or result wrappers fail execution. Results are shared without copying, so prefer immutable values or coordinate mutable access.

## Run a subset

`context.Register(finalInstruction)` registers that instruction and its transitive upstream dependencies, not downstream consumers. For a selection from a process, use the earlier example's `process` and `factor`:

```csharp
var request = new ProcessRunRequest<NumberPipelineExecutionContext>(
    process, [factor]);
var partialContext = new NumberPipelineExecutionContext(new UserInterfaceProvider());
partialContext.Register(request);
var factorOperation = partialContext.Register(factor);

await partialContext.ExecuteAsync();
var factors = (await factorOperation.Promise()).GetResult();
```

This omits the difference step. Selections must contain process members; duplicates are removed, and an empty selection registers nothing. Use a fresh context because execution starts **all registered work**, not just the latest request. Requests capture selected references, not configuration or dependency state.

## Execution, cancellation, and failures

- Finish registration before `ExecuteAsync()`: new registrations are rejected afterward, though existing lookups remain valid. Calling an individual operation's `Execute()` does not start its dependencies.
- Work begins on the activating thread until its first incomplete await. The library does not offload CPU work or blocking I/O automatically; blocking on another operation can prevent it from starting.
- Failures propagate through promises, completion, and context execution. The context waits for all operations; it does not cancel independent branches or roll back effects.
- Cancellation is cooperative. Observe the hook's token during waits and computation. Canceling an observer's `WaitAsync` stops only that wait; cancel the context's source to request run cancellation.
- The base checks cancellation before work and before publishing success. It cannot interrupt synchronous work, undo side effects, or change an already published result.
- Registration can fail after upstream operations were registered; it is not transactional. Correct invalid definitions and prefer a fresh context. Retrying execution also requires a fresh context and may repeat side effects.

[FactorInstruction](configurable-number-pipeline-poc/FactorInstruction.cs) demonstrates computation cancellation; [Program.cs](configurable-number-pipeline-poc/Program.cs) demonstrates caller-side cancellation handling.

## Instruction factories

Factories are optional: use constructors for fixed graphs, or `InstructionFactory<TContext>` to discover which instructions accept a UI's selected inputs.

The POC's [DistributeInstructionFactory](configurable-number-pipeline-poc/DistributeInstructionFactory.cs) declares ordered inputs with different result types:

```csharp
var schema = InstructionFactory.For<NumberPipelineExecutionContext>();
var valueKey = schema.Input<int>("toDistribute");
var valuesKey = schema.Input<ImmutableList<int>>("distributeAmong");
var factory = schema.Creates(inputs =>
    new DistributeInstruction(inputs.Get(valueKey), inputs.Get(valuesKey)));
```

This also requires `using System.Collections.Immutable;` and the POC types. Call `factory.BindInputs(selectedInstructions)` before registering the run. A `ReadyResponse<TContext>` exposes bindings and a `Create` delegate; add its result to the process. A `FailedResponse<TContext>` exposes `Reason`. Binding validates count, types, and context without constructing or executing anything. See [InputScripts.SelectBinding](configurable-number-pipeline-poc/InputScripts.cs) for the selection UI.

Names must be nonblank and unique. `Input<TResult>` declares a fixed input; `Many<TResult>` declares one trailing variadic group, with a configurable minimum count. Keys belong to their schema, and `Creates` freezes it.

Each `Create()` invocation must return the exact inferred instruction type with **the bound dependency references in order, including duplicates**. Type/dependency mismatches and foreign keys throw; constructor exceptions propagate. Binding snapshots references, not definitions. See [factory tests](operator-result-management.Tests/InstructionFactoryTests.cs) for zero-input and variadic examples.

## Conditional subprocesses

`ConditionalInstruction<TContext>` takes a Boolean-producing instruction, a body process, a child-context factory, and optional `outerInputs`. It produces completion, not a value. The POC UI does not expose conditionals; see [ConditionalInstructionTests](operator-result-management.Tests/ConditionalInstructionTests.cs) for complete examples.

- Add the condition and any outer inputs (with their dependencies) to the parent process before adding the conditional. Do not add its body-local instructions to the parent.
- The factory must return a fresh, unused child context with the parent's exact cancellation token and required services. Do not register work in the factory.
- Declare cross-scope data through `outerInputs`. When composing the body, explicitly add any imported instructions and their dependencies before body steps that consume them. Body membership does not grant an import: the condition, outer inputs, and their dependencies share existing outer operations only when declared through the conditional, and execute even when the condition is false. Imported instructions do not become locally owned operations in the child context.
- Body-local instructions cannot also belong to parent or sibling scopes. Recursive containment and dependencies back into enclosing conditionals are rejected.
- Registration prepares and freezes the whole body, including nested bodies, even for a false condition. Invalid bodies still fail registration.

A false condition completes successfully without starting the body. A true condition waits for all body work; failure or cancellation propagates. Downstream operations can await the conditional's `Completion`, but body results and operations are not exported.

## Editing and traceability

Instructions may be mutable, but registered operations must use captured state. Re-registering an edited instruction in the same context returns its original operation. Coordinate edits with traversal and registration; registry locks do not protect arbitrary instruction setters.

`process.Instructions`, run requests, and `context.Operations` preserve membership or associations, **not deep copies of instruction state**. The operations map contains locally owned operations, excluding conditional imports. For historical records, retain immutable definitions or capture configuration and run metadata yourself.

Dependency edits do not update process membership automatically. Add any new upstream instructions in dependency order, then call `Add` again on the edited instruction to validate its current graph. Missing transitive dependencies are rejected even when the edited instruction is already a member. Validation does not remove old members or reorder existing ones. Registration still captures the current graph and resolves upstream operations; composition checks are not a freeze on mutable definitions. Rebuild the process when a new membership order is needed; there is no removal/replacement API.

`InstructionBase.Id` is stable metadata, not a registry key or enforced uniqueness constraint. Multiple instructions may map to one operation; reverse lookup must account for all matching references.

## Tests and older integrations

The [test project](operator-result-management.Tests) targets only the library: registration, lifecycle, graph editing, factories, and conditional scopes. Test-local instructions and operations exercise library contracts; the POC's console UI and arithmetic are outside the automated suite. Favor observable behavior over reflection-based API-shape checks, collection implementation types, or exact diagnostic wording.

```powershell
dotnet test .\operator-result-management.Tests\operator-result-management.Tests.csproj
```

Older interface-based integrations must use the current bases:

| Previous API | Replacement |
|---|---|
| `IProcess` / `IProcess<TContext>` | `ProcessBase` for display; `ProcessBase<TContext>` for composition. `Instructions` exposes `IReadOnlyList`. |
| `IInstruction` and generic forms | Corresponding `InstructionBase` forms; override `CreateOperation(context, inputs)`. Context contravariance is no longer available. |
| `IOperationInstance` and typed form | `OperationInstanceBase` and typed form in `.Basic`. Typed work overrides `ExecuteResultAsync`; completion-only work overrides `ExecuteCoreAsync`. |
| Custom execution-context interface implementations | Derive from `ExecutionContextBase`. |
| `IInstructionFactory<TContext>` | Concrete factories built through `InstructionFactory.For<TContext>()`. |

`IOperationResult<TResult>` remains unchanged. Registration, graph membership, and execution lifecycle remain library-owned.
