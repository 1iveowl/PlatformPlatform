# The .NET 11 runtime image matching the SDK in blazor/global.json: DOTNET_VERSION and ASPNET_VERSION in this tag are
# 11.0.0-rc.1.26425.128, the runtime the Blazor build root targets. The 11.0 images are built on Ubuntu 26.04 (resolute),
# where 10.0 uses Noble, and the -extra variant carries the ICU data the two supported cultures need.
FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.1-resolute-chiseled-extra

WORKDIR /app
COPY ./Blazor.Host/publish .

USER app

ENTRYPOINT ["dotnet", "Blazor.Host.dll"]
