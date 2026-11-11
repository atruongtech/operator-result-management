using operator_result_management.Basic;

namespace operator_result_management;

/// <summary>
/// Executes an owned subprocess only when its condition produces true.
/// The body is registered and frozen with the parent run, but is not a parent dependency.
/// Body instructions are private to their scope. Explicit outer inputs, the condition,
/// and their transitive dependencies are imported without duplicating their operations.
/// </summary>
public class ConditionalInstruction<TContext> : InstructionBase<TContext>
    where TContext : ExecutionContextBase
{
    private readonly IReadOnlyList<InstructionBase<TContext>> _dependencies;
    private readonly Func<TContext, TContext> _bodyContextFactory;

    public InstructionBase<bool, TContext> Condition { get; }
    public ProcessBase<TContext> Body { get; }
    public override IReadOnlyList<InstructionBase> Dependencies => _dependencies;

    public ConditionalInstruction(
        InstructionBase<bool, TContext> condition,
        ProcessBase<TContext> body,
        Func<TContext, TContext> bodyContextFactory,
        IEnumerable<InstructionBase<TContext>>? outerInputs = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(bodyContextFactory);
        Condition = condition;
        Body = body;
        _bodyContextFactory = bodyContextFactory;
        var dependencies = new List<InstructionBase<TContext>> { condition };
        if (outerInputs is not null)
        {
            foreach (var input in outerInputs)
            {
                ArgumentNullException.ThrowIfNull(input);
                if (!dependencies.Any(dependency => ReferenceEquals(dependency, input)))
                    dependencies.Add(input);
            }
        }
        _dependencies = dependencies.AsReadOnly();
    }

    /// <summary>
    /// Creates a fresh, unused context exclusively owned by this body. Preserve the
    /// parent's cancellation token and supply any services required by body instructions.
    /// Uses the constructor-supplied factory by default. Do not register or execute work
    /// here; registration is performed by this class.
    /// </summary>
    protected virtual TContext CreateBodyContext(TContext parent) => _bodyContextFactory(parent);

    /// <summary>
    /// Registers the condition, outer inputs, and a frozen private body without executing
    /// any work. The returned completion signal does not expose body results.
    /// </summary>
    protected override OperationInstanceBase CreateOperation(TContext context, InstructionInputs<TContext> inputs)
    {
        var imports = new Dictionary<InstructionBase, OperationInstanceBase>(ReferenceEqualityComparer.Instance);
        foreach (var dependency in _dependencies)
            foreach (var instruction in ProcessBase<TContext>.GetDependencyGraph(dependency))
                if (!imports.ContainsKey(instruction))
                    imports.Add(instruction, instruction.GetOperation(context));
        var condition = inputs.GetOperation(Condition);

        var bodyRequest = new ProcessRunRequest<TContext>(Body);
        foreach (var instruction in bodyRequest.SelectedInstructions)
            _ = ProcessBase<TContext>.GetDependencyGraph(instruction);

        var bodyContext = CreateBodyContext(context)
            ?? throw new InvalidOperationException("Body context factory returned null.");
        bodyContext.AttachScope(context, imports);
        try
        {
            bodyRequest.Register(bodyContext);
            bodyContext.FreezeRegistration();
            return new ConditionalOperation(context, condition, bodyContext);
        }
        catch
        {
            bodyContext.DiscardScope();
            throw;
        }
    }

    private sealed class ConditionalOperation(
        TContext context, OperationInstanceBase<bool> condition, TContext bodyContext)
        : OperationInstanceBase(context)
    {
        protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            var result = await condition.Promise().WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.GetResult())
                await bodyContext.ExecuteAsync().ConfigureAwait(false);
        }
    }
}
