FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY StockDecision.sln ./
COPY src/StockDecision.Api/StockDecision.Api.csproj src/StockDecision.Api/
COPY src/StockDecision.Application/StockDecision.Application.csproj src/StockDecision.Application/
COPY src/StockDecision.Domain/StockDecision.Domain.csproj src/StockDecision.Domain/
COPY src/StockDecision.Infrastructure/StockDecision.Infrastructure.csproj src/StockDecision.Infrastructure/
RUN dotnet restore src/StockDecision.Api/StockDecision.Api.csproj
COPY . .
RUN dotnet publish src/StockDecision.Api/StockDecision.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "StockDecision.Api.dll"]
