namespace operator_result_management.Basic;

/// <summary>
/// A typed, schema-owned key identifying one instruction parameter.
/// </summary>
public sealed class InstructionCreationParameterKey<TResult, TContext> where TContext : ExecutionContextBase
{
    internal FactoryParameterDefinition Definition { get; }

    internal InstructionCreationParameterKey(FactoryParameterDefinition definition) => Definition = definition;
}
