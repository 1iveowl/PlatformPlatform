// What this runtime knows about its own version against the server's, for the one decision that depends on it: whether a
// mutation may still be sent. The version comes from every bootstrap the session accepts, so it is read on the
// authenticated channel and refreshed whenever the session is.
//
// A client also becomes stale without its version changing, because a deployment replaces the fingerprinted asset set:
// the assets this document still asks for are then no longer served. StaleAssetProbe reports such a 404 here, and from
// then on this runtime is treated the same as one outside the version window.
//
// Both signals are read again before a mutation is forwarded, by StaleClientRecheck, so a tab that has not navigated since
// a deployment still learns before it writes.

using Account.Features.Authentication.Queries;

namespace Blazor.Client.Bootstrap;

public sealed class ClientVersionState(string clientVersion)
{
    public string ClientVersion => clientVersion;

    public string? ServerVersion { get; private set; }

    public ClientVersionSupport Support { get; private set; } = ClientVersionSupport.Unknown;

    // The fingerprinted asset this document asked for that the server no longer serves, if one was reported
    public string? MissingAsset { get; private set; }

    // True when this runtime must not send a mutation and the user is asked to reload instead
    public bool IsStale => Support == ClientVersionSupport.Unsupported || MissingAsset is not null;

    // Called with every bootstrap the session accepts, and with null when the session ends
    public void Apply(BootstrapResponse? bootstrap)
    {
        ServerVersion = ClientVersionWindow.ReadServerVersion(bootstrap);
        Support = bootstrap is null ? ClientVersionSupport.Unknown : ClientVersionWindow.Evaluate(clientVersion, ServerVersion);
    }

    // The first missing asset is kept: a deployment does not put the old asset set back, so nothing clears this
    public void ReportMissingAsset(string url)
    {
        if (MissingAsset is not null) return;
        if (!FingerprintedAsset.IsFingerprinted(url)) return;

        MissingAsset = url;
    }
}
