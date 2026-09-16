using Blazor.Host;
using SharedKernel.Configuration;

HostApplication.Build(args, SecurityDependencyConfiguration.GetTokenSigningService()).Run();
