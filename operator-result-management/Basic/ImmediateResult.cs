namespace operator_result_management.Basic
{
    public record ImmediateResult<T> : IOperationResult<T>
    {
        private readonly T _value;

        public ImmediateResult(T value)
        {
            _value = value;
        }

        public T GetResult() => _value;
    }
}