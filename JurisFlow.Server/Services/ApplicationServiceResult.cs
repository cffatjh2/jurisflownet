namespace JurisFlow.Server.Services
{
    public sealed class ApplicationServiceResult<T>
    {
        private ApplicationServiceResult(bool succeeded, T? value, int statusCode, string? title, string? detail)
        {
            Succeeded = succeeded;
            Value = value;
            StatusCode = statusCode;
            Title = title;
            Detail = detail;
        }

        public bool Succeeded { get; }
        public T? Value { get; }
        public int StatusCode { get; }
        public string? Title { get; }
        public string? Detail { get; }

        public static ApplicationServiceResult<T> Success(T value)
        {
            return new ApplicationServiceResult<T>(true, value, StatusCodes.Status200OK, null, null);
        }

        public static ApplicationServiceResult<T> Failure(int statusCode, string title, string detail)
        {
            return new ApplicationServiceResult<T>(false, default, statusCode, title, detail);
        }
    }
}
