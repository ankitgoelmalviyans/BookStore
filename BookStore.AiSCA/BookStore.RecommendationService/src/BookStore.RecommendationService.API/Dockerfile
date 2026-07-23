# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Build context is the service's src/ directory, so this copies Application/, Core/,
# Infrastructure/ and the API project together — the API's project references resolve.
COPY . ./

WORKDIR /src/BookStore.RecommendationService.API
RUN dotnet restore "BookStore.RecommendationService.API.csproj"
RUN dotnet publish "BookStore.RecommendationService.API.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 80
ENTRYPOINT ["dotnet", "BookStore.RecommendationService.API.dll"]
