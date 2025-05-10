# Use official .NET SDK image for build
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Use official .NET runtime image for runtime
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Expose port (optional but good practice)
EXPOSE 80

# Entry point
ENTRYPOINT ["dotnet", "ProductService.dll"]
