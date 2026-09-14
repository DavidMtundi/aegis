FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files for dependency caching
COPY src/Aegis.Shared/Aegis.Shared.csproj src/Aegis.Shared/
COPY src/Aegis.Modules.Aml/Aegis.Modules.Aml.csproj src/Aegis.Modules.Aml/
COPY src/Aegis.Modules.Kyc/Aegis.Modules.Kyc.csproj src/Aegis.Modules.Kyc/
COPY src/Aegis.Modules.Kyb/Aegis.Modules.Kyb.csproj src/Aegis.Modules.Kyb/
COPY src/Aegis.Modules.Screening/Aegis.Modules.Screening.csproj src/Aegis.Modules.Screening/
COPY src/Aegis.Modules.Risk/Aegis.Modules.Risk.csproj src/Aegis.Modules.Risk/
COPY src/Aegis.Modules.Transactions/Aegis.Modules.Transactions.csproj src/Aegis.Modules.Transactions/
COPY src/Aegis.Modules.Features/Aegis.Modules.Features.csproj src/Aegis.Modules.Features/
COPY src/Aegis.Modules.Alerts/Aegis.Modules.Alerts.csproj src/Aegis.Modules.Alerts/
COPY src/Aegis.Modules.Cases/Aegis.Modules.Cases.csproj src/Aegis.Modules.Cases/
COPY src/Aegis.Modules.Network/Aegis.Modules.Network.csproj src/Aegis.Modules.Network/
COPY src/Aegis.Modules.Reporting/Aegis.Modules.Reporting.csproj src/Aegis.Modules.Reporting/
COPY src/Aegis.Modules.Audit/Aegis.Modules.Audit.csproj src/Aegis.Modules.Audit/
COPY src/Aegis.Infrastructure/Aegis.Infrastructure.csproj src/Aegis.Infrastructure/
COPY src/Aegis.Api/Aegis.Api.csproj src/Aegis.Api/

RUN dotnet restore src/Aegis.Api/Aegis.Api.csproj

# Copy all sources and build
COPY src/ src/
RUN dotnet publish src/Aegis.Api/Aegis.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "Aegis.Api.dll"]
