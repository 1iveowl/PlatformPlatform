// The accessible state of one form field, decided in one place for static server-rendered forms and interactive ones.
// A field is marked aria-invalid while the EditContext holds a message for it, which covers both sources: the data
// annotations validator writes its messages there, and FormErrorMapper writes the account API's field errors into its own
// store on the same context. Every field is described by the element that carries its messages and by the form's error
// alert, so a screen reader reads the refusal with the control rather than leaving it in a region the user may never
// reach. The description is attached whether or not the field is invalid, so the reference is stable across an enhanced
// form update and names an element that is always rendered.

using System.Linq.Expressions;
using Microsoft.AspNetCore.Components.Forms;

namespace Blazor.Client.Forms;

public sealed class FieldAria(EditContext editContext, string? formErrorId = null)
{
    // The id of the element holding one field's validation messages, derived from the field's own name so a control and
    // its description cannot drift apart. FieldValidation renders that element with the same derivation.
    public static string ValidationId(string fieldName)
    {
        return $"{fieldName}-validation";
    }

    // The value of an aria-describedby: the ids that exist, in the order they are given, separated by a single space
    public static string? DescribedBy(params string?[] ids)
    {
        var present = ids.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
        return present.Length == 0 ? null : string.Join(" ", present);
    }

    // The attributes one field's input splats: its description, and aria-invalid while the field has a message. The
    // attribute is left out rather than written false, because a field with no message is valid by omission and the
    // framework's input components remove a false value of their own accord.
    public IReadOnlyDictionary<string, object> For<TValue>(Expression<Func<TValue>> field, string fieldName)
    {
        var attributes = new Dictionary<string, object>();
        var describedBy = DescribedBy(ValidationId(fieldName), formErrorId);
        if (describedBy is not null) attributes["aria-describedby"] = describedBy;
        if (IsInvalid(field)) attributes["aria-invalid"] = "true";

        return attributes;
    }

    public bool IsInvalid<TValue>(Expression<Func<TValue>> field)
    {
        return editContext.GetValidationMessages(FieldIdentifier.Create(field)).Any();
    }
}
