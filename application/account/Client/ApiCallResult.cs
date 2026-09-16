using System.Diagnostics.CodeAnalysis;

namespace Account.Client;

public sealed class ApiCallResult
{
    private ApiCallResult(ApiCallOutcome outcome, ApiCallProblem? problem)
    {
        Outcome = outcome;
        Problem = problem;
    }

    public ApiCallOutcome Outcome { get; }

    [MemberNotNullWhen(false, nameof(Problem))]
    public bool IsSuccess => Outcome == ApiCallOutcome.Success;

    public ApiCallProblem? Problem { get; }

    public static ApiCallResult Success()
    {
        return new ApiCallResult(ApiCallOutcome.Success, null);
    }

    public static ApiCallResult Failed(ApiCallOutcome outcome, ApiCallProblem problem)
    {
        if (outcome == ApiCallOutcome.Success) throw new ArgumentException("A failed result cannot have the Success outcome.", nameof(outcome));

        return new ApiCallResult(outcome, problem);
    }
}

public sealed class ApiCallResult<TValue>
{
    private ApiCallResult(ApiCallOutcome outcome, ApiCallProblem? problem, TValue? value)
    {
        Outcome = outcome;
        Problem = problem;
        Value = value;
    }

    public ApiCallOutcome Outcome { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Problem))]
    public bool IsSuccess => Outcome == ApiCallOutcome.Success;

    public ApiCallProblem? Problem { get; }

    public TValue? Value { get; }

    public static ApiCallResult<TValue> Success(TValue value)
    {
        return new ApiCallResult<TValue>(ApiCallOutcome.Success, null, value);
    }

    public static ApiCallResult<TValue> Failed(ApiCallOutcome outcome, ApiCallProblem problem)
    {
        if (outcome == ApiCallOutcome.Success) throw new ArgumentException("A failed result cannot have the Success outcome.", nameof(outcome));

        return new ApiCallResult<TValue>(outcome, problem, default);
    }
}
