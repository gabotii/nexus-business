# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached unless project files change).
COPY Directory.Build.props Directory.Packages.props Nexus.slnx ./
COPY src/Nexus.Domain/Nexus.Domain.csproj src/Nexus.Domain/
COPY src/Nexus.Application/Nexus.Application.csproj src/Nexus.Application/
COPY src/Nexus.Infrastructure/Nexus.Infrastructure.csproj src/Nexus.Infrastructure/
COPY src/Nexus.Api/Nexus.Api.csproj src/Nexus.Api/
RUN dotnet restore src/Nexus.Api/Nexus.Api.csproj

COPY src/ src/
RUN dotnet publish src/Nexus.Api/Nexus.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- runtime ----
# The ASP.NET Core image ships ICU and tzdata (IANA time zones per company). Runs as the non-root "app" user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

# Data Protection keys (session tickets, e-mail tokens) and uploaded files (company logos) live on volumes mounted
# here; a named volume inherits this ownership on first use, so the non-root user can write to it.
RUN mkdir -p /keys /data/files && chown app:app /keys /data/files

USER app
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=6 \
  CMD ["dotnet", "Nexus.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "Nexus.Api.dll"]
