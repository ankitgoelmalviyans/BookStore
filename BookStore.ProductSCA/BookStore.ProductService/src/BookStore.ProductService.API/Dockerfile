# -------------------------------
# Build stage
# -------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy entire src directory (all projects)
COPY ../ ./

# Restore & publish API project
WORKDIR /src/BookStore.ProductService.API
RUN dotnet restore "BookStore.ProductService.API.csproj"
RUN dotnet publish "BookStore.ProductService.API.csproj" -c Release -o /app/publish

# -------------------------------
# Runtime stage
# -------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 80
ENTRYPOINT ["dotnet", "BookStore.ProductService.API.dll"]
