namespace operator_result_management.Basic;

/// <summary>
/// A typed, schema-owned key identifying a trailing group of instruction parameters.
/// </summary>
public sealed class InstructionCreationParameterGroupKey<TResult, TContext> where TContext : ExecutionContextBase
{
    internal FactoryParameterDefinition Definition { get; }

    internal InstructionCreationParameterGroupKey(FactoryParameterDefinition definition) => Definition = definition;
}
