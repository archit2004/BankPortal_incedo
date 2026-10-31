FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first (cached until a csproj changes)
COPY src/BankPortal.Domain/BankPortal.Domain.csproj src/BankPortal.Domain/
COPY src/BankPortal.Application/BankPortal.Application.csproj src/BankPortal.Application/
COPY src/BankPortal.Infrastructure/BankPortal.Infrastructure.csproj src/BankPortal.Infrastructure/
COPY src/BankPortal.Api/BankPortal.Api.csproj src/BankPortal.Api/
RUN dotnet restore src/BankPortal.Api/BankPortal.Api.csproj

COPY src/ src/
RUN dotnet publish src/BankPortal.Api/BankPortal.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
# curl is only for the container health check
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "BankPortal.Api.dll"]