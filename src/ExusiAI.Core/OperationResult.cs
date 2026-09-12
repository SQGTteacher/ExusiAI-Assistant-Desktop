namespace ExusiAI.Core;

public sealed record OperationError(string Code, string Message, Exception? Exception = null);

public sealed record OperationResult(bool Succeeded, OperationError? Error = null)
{
    public static OperationResult Success { get; } = new(true);
    public static OperationResult Failure(string code, string message, Exception? exception = null) =>
        new(false, new OperationError(code, message, exception));
}
