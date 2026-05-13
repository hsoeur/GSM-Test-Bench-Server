# ASP.NET Core .NET8 multi-stage build
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution/project files
COPY Api.TestGsm/Api.TestGsm.csproj Api.TestGsm/
RUN dotnet restore Api.TestGsm/Api.TestGsm.csproj

# Copy source
COPY Api.TestGsm/ Api.TestGsm/

# Publish
RUN dotnet publish Api.TestGsm/Api.TestGsm.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Listen on port5001 in container
ENV ASPNETCORE_URLS=http://0.0.0.0:5001

# Copy published app
COPY --from=build /app/publish .

# Expose port
EXPOSE5001

# Start
ENTRYPOINT ["dotnet", "Api.TestGsm.dll"]
