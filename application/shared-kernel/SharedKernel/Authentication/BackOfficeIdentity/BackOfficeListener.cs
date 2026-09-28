using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SharedKernel.Configuration;

namespace SharedKernel.Authentication.BackOfficeIdentity;

// Tells whether a request arrived where the platform's authentication sets the principal headers: the back-office
// container app in Azure, and the back-office listener locally, where the mock login sets them. Anywhere else, such as
// the account-api container app or the main listener, any caller can send those headers, so they must never become a
// back-office identity there.
public sealed class BackOfficeListener
{
    public const string IsBackOfficeContainerKey = "BackOffice:IsBackOfficeContainer";

    public const string LocalPortKey = "BACK_OFFICE_KESTREL_PORT";

    private readonly bool _isBackOfficeContainer;
    private readonly bool _isRunningInAzure;
    private readonly int? _localPort;

    private BackOfficeListener(bool isRunningInAzure, bool isBackOfficeContainer, int? localPort)
    {
        _isRunningInAzure = isRunningInAzure;
        _isBackOfficeContainer = isBackOfficeContainer;
        _localPort = localPort;
    }

    public static BackOfficeListener FromConfiguration(IConfiguration configuration)
    {
        if (SharedInfrastructureConfiguration.IsRunningInAzure)
        {
            return InAzureContainer(configuration.GetValue(IsBackOfficeContainerKey, false));
        }

        return OnLocalPort(int.TryParse(configuration[LocalPortKey], out var port) && port > 0 ? port : null);
    }

    public static BackOfficeListener InAzureContainer(bool isBackOfficeContainer)
    {
        return new BackOfficeListener(true, isBackOfficeContainer, null);
    }

    public static BackOfficeListener OnLocalPort(int? localPort)
    {
        return new BackOfficeListener(false, false, localPort);
    }

    public bool Received(HttpContext context)
    {
        if (_isRunningInAzure) return _isBackOfficeContainer;
        return _localPort is not null && context.Connection.LocalPort == _localPort;
    }
}
