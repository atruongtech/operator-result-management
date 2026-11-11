using operator_result_management.Basic;

namespace operator_result_management;

/// <summary>
/// Defines a reference-identified step. Id is stable metadata, not a registry key.
/// Configuration may be mutable; operation creation must capture each run's values.
/// Coordinate edits with graph traversal and registration.
/// </summary>
public abstract class InstructionBase
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Direct upstream instructions, in input order. The default is a read-only copy
    /// of constructor inputs. Overrides may expose editable dependencies, but must
    /// remain stable during graph traversal and registration.
    /// </summary>
    public virtual IReadOnlyList<InstructionBase> Dependencies { get; }

    protected InstructionBase(params InstructionBase[] dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        foreach (var dependency in dependencies)
            ArgumentNullException.ThrowIfNull(dependency);
        Dependencies = Array.AsReadOnly(dependencies.ToArray());
    }
}

/// <summary>
/// Owns per-context registration and resolves declared dependencies before calling
/// the operation creation hook. Registration does not execute work.
/// </summary>
public abstract class InstructionBase<TContext> : InstructionBase
    where TContext : ExecutionContextBase
{
    protected InstructionBase(params InstructionBase[] dependencies) : base(dependencies)
    {
    }

    /// <summary>
    /// Returns the shared operation for this instruction and context. Existing
    /// lookups never recreate operations or reread mutable instruction state.
    /// </summary>
    public OperationInstanceBase GetOperation(TContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.GetOrAddOperation(this, () =>
        {
            var dependencies = Dependencies
                ?? throw new InvalidOperationException("Instruction dependencies must not be null.");
            var operations = new Dictionary<InstructionBase, OperationInstanceBase>(ReferenceEqualityComparer.Instance);
            foreach (var dependency in dependencies)
            {
                if (dependency is null)
                    throw new InvalidOperationException("Instruction dependencies must not contain null.");
                if (dependency is not InstructionBase<TContext> input)
                    throw new InvalidOperationException("Instruction dependency does not support this instruction's execution context.");
                if (!operations.ContainsKey(input))
                    operations.Add(input, input.GetOperation(context));
            }

            return CreateOperation(context, new InstructionInputs<TContext>(operations));
        });
    }

    /// <summary>
    /// Creates run-specific work using already resolved direct inputs. Capture
    /// configuration here, copying mutable values where needed. Do not start or
    /// wait for execution, or defer reading instruction state to the work hook.
    /// </summary>
    protected abstract OperationInstanceBase CreateOperation(TContext context, InstructionInputs<TContext> inputs);
}

/// <summary>
/// A result-producing instruction with library-owned typed and untyped registration.
/// </summary>
public abstract class InstructionBase<TResult, TContext> : InstructionBase<TContext>
    where TContext : ExecutionContextBase
{
    protected InstructionBase(params InstructionBase[] dependencies) : base(dependencies)
    {
    }

    public OperationInstanceBase<TResult> GetTypedOperation(TContext context) =>
        (OperationInstanceBase<TResult>)GetOperation(context);

    protected abstract override OperationInstanceBase<TResult> CreateOperation(
        TContext context, InstructionInputs<TContext> inputs);
}
