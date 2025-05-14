# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project structure (one level above API folder to include .csproj and all dependencies)
COPY ../ ./

# Fixed WORKDIR to point directly to API folder (no /src prefix)
WORKDIR /src/InventoryService.API
RUN dotnet restore "BookStore.InventoryService.API.csproj"
RUN dotnet publish "BookStore.InventoryService.API.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 80
ENTRYPOINT ["dotnet", "BookStore.InventoryService.API.dll"]
