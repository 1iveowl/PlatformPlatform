using JetBrains.Annotations;

namespace Account.Features.EmailAuthentication.Domain;

[PublicAPI]
[IdPrefix("emlog")]
[JsonConverter(typeof(StronglyTypedIdJsonConverter<string, EmailLoginId>))]
public sealed record EmailLoginId(string Value) : StronglyTypedUlid<EmailLoginId>(Value)
{
    public override string ToString()
    {
        return Value;
    }
}
