using JetBrains.Annotations;

namespace Account.Features.Authentication.Domain;

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceType
{
    Unknown,
    Desktop,
    Mobile,
    Tablet
}

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LoginMethod
{
    OneTimePassword,
    Google,
    Entra,
    MitId
}
