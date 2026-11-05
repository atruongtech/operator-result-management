namespace operator_result_management
{
    public record ExecutionContext : IExecutionContext
    {
        private Dictionary<IInstruction, IExecutionContextContainer> _operations;

        public IReadOnlyDictionary<IInstruction, IExecutionContextContainer> Operations => _operations;   

        public ExecutionContext()
        {
            _operations = [];
        }

        public bool AddOperation(IInstruction instruction, IExecutionContextContainer contextContainer)
        {
            return _operations.TryAdd(instruction, contextContainer);
        }
    }
}