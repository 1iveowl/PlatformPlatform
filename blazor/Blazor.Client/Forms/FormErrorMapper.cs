// Places an account API failure on an EditForm, in static server-rendered POST handlers and in interactive components alike.
// Field keys arrive camelCase and match the model's properties case-insensitively; a key that matches no property, and
// any form-wide message, is stored against the model itself, so ValidationSummary shows it and FormMessages exposes it.
// The messages live in this mapper's own ValidationMessageStore: the next submit clears them without touching the
// DataAnnotationsValidator's store, and every message the API returned is kept.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Account.Client;
using Microsoft.AspNetCore.Components.Forms;

namespace Blazor.Client.Forms;

public sealed class FormErrorMapper : IDisposable
{
    private readonly EditContext _editContext;
    private readonly List<string> _formMessages = [];
    private readonly ValidationMessageStore _messageStore;
    private readonly Dictionary<string, string> _propertyNames;

    public FormErrorMapper(EditContext editContext)
    {
        _editContext = editContext;
        _messageStore = new ValidationMessageStore(editContext);
        _propertyNames = GetPropertyNames(editContext.Model.GetType());
        _editContext.OnValidationRequested += OnValidationRequested;
    }

    // Messages that belong to no single field, in the order they were added
    public IReadOnlyList<string> FormMessages => _formMessages;

    // True when the last failure was a rejected antiforgery token or a client outside the supported version window, which
    // only a page reload recovers from
    public bool IsReloadRequired { get; private set; }

    public void Dispose()
    {
        _editContext.OnValidationRequested -= OnValidationRequested;
    }

    // Field errors only: each key goes to its property, or to the form when no property matches
    public void Apply(ApiCallProblem problem)
    {
        foreach (var (key, messages) in problem.Errors)
        {
            if (_propertyNames.TryGetValue(key, out var propertyName))
            {
                _messageStore.Add(_editContext.Field(propertyName), messages);
            }
            else
            {
                AddFormMessagesCore(messages);
            }
        }

        _editContext.NotifyValidationStateChanged();
    }

    public void AddFormMessage(string message)
    {
        AddFormMessagesCore([message]);
        _editContext.NotifyValidationStateChanged();
    }

    // The form-level presentation for a surface without toasts, such as a static server-rendered form: field errors are
    // mapped, a message becomes a form message, an antiforgery rejection and a client outside the supported version window
    // become a form message that requires a reload, and a suppressed failure shows nothing
    public ApiFailure ApplyFailure(ApiCallOutcome outcome, ApiCallProblem problem)
    {
        var failure = ApiFailureClassifier.Classify(outcome, problem);
        switch (failure.Kind)
        {
            case ApiFailureKind.FieldValidation:
                Apply(problem);
                break;
            case ApiFailureKind.AntiforgeryRecovery:
            case ApiFailureKind.Version:
                IsReloadRequired = true;
                AddFormMessage(failure.Message!);
                break;
            case ApiFailureKind.Message:
                AddFormMessage(failure.Message!);
                break;
            case ApiFailureKind.Suppressed:
                break;
        }

        return failure;
    }

    public ApiFailure ApplyFailure(ApiCallResult result)
    {
        return ApplyFailure(result.Outcome, result.Problem ?? throw new ArgumentException("A successful result has no failure to apply.", nameof(result)));
    }

    public ApiFailure ApplyFailure<TValue>(ApiCallResult<TValue> result)
    {
        return ApplyFailure(result.Outcome, result.Problem ?? throw new ArgumentException("A successful result has no failure to apply.", nameof(result)));
    }

    // Removes the server messages this mapper added; client-side validation messages stay
    public void Clear()
    {
        _messageStore.Clear();
        _formMessages.Clear();
        IsReloadRequired = false;
        _editContext.NotifyValidationStateChanged();
    }

    private void AddFormMessagesCore(IEnumerable<string> messages)
    {
        foreach (var message in messages)
        {
            _messageStore.Add(new FieldIdentifier(_editContext.Model, string.Empty), message);
            _formMessages.Add(message);
        }
    }

    private void OnValidationRequested(object? sender, ValidationRequestedEventArgs e)
    {
        Clear();
    }

    // The form model's public properties are bound by the form's inputs and read by DataAnnotationsValidator, which keeps them
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Form model properties are referenced by the form's input bindings.")]
    private static Dictionary<string, string> GetPropertyNames(Type modelType)
    {
        var propertyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.GetIndexParameters().Length == 0))
        {
            propertyNames.TryAdd(property.Name, property.Name);
        }

        return propertyNames;
    }
}
