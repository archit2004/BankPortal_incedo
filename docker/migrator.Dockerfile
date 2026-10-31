FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet tool install --global dotnet-ef --version 9.0.*

COPY src/BankPortal.Domain/BankPortal.Domain.csproj src/BankPortal.Domain/
COPY src/BankPortal.Application/BankPortal.Application.csproj src/BankPortal.Application/
COPY src/BankPortal.Infrastructure/BankPortal.Infrastructure.csproj src/BankPortal.Infrastructure/
COPY src/BankPortal.Api/BankPortal.Api.csproj src/BankPortal.Api/
RUN dotnet restore src/BankPortal.Api/BankPortal.Api.csproj

COPY src/ src/

# Build-time placeholders only. EF loads the API's configuration to discover the DbContext, and the API
# validates its settings on startup. These values exist only in this build stage and are not secrets;
# the real connection string is passed at run time with --connection.
ENV Jwt__Key=build-time-placeholder-not-a-secret-0123456789abcdef \
    Jwt__Issuer=BankPortal \
    Jwt__Audience=BankPortal.Web \
    ConnectionStrings__Default="Server=placeholder;Database=BankPortal;User Id=sa;Password=placeholder;TrustServerCertificate=True"

RUN RID=$(if [ "$(uname -m)" = "aarch64" ]; then echo linux-arm64; else echo linux-x64; fi) && \
    dotnet ef migrations bundle \
      --project src/BankPortal.Infrastructure \
      --startup-project src/BankPortal.Api \
      --self-contained -r $RID --force -o /app/efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:9.0
# ICU is required by SqlClient (it resolves the en-us culture), so invariant globalization mode cannot be used
RUN apt-get update && apt-get install -y --no-install-recommends libicu72 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/efbundle .
ENTRYPOINT ["./efbundle"]