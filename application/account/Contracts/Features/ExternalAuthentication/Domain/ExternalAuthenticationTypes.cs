using JetBrains.Annotations;

namespace Account.Features.ExternalAuthentication.Domain;

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExternalProviderType
{
    Google,
    Entra,
    MitId
}

/// <summary>
///     How strongly an identity provider asserts that the person is who they claim to be, in ascending order so a
///     minimum level can be expressed as a comparison. The values mirror the three MitID levels of assurance; MitID
///     Erhverv is deliberately absent, because it returns a different identifier for the same person.
/// </summary>
[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IdentityAssuranceLevel
{
    Low,
    Substantial,
    High
}
