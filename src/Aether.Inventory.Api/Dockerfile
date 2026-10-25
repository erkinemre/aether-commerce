FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Aether.Contracts/Aether.Contracts.csproj src/Aether.Contracts/
COPY src/Aether.Inventory.Api/Aether.Inventory.Api.csproj src/Aether.Inventory.Api/
RUN dotnet restore src/Aether.Inventory.Api/Aether.Inventory.Api.csproj
COPY src/Shared/ src/Shared/
COPY src/Aether.Contracts/ src/Aether.Contracts/
COPY src/Aether.Inventory.Api/ src/Aether.Inventory.Api/
RUN dotnet publish src/Aether.Inventory.Api/Aether.Inventory.Api.csproj -c Release -o /app/publish --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Aether.Inventory.Api.dll"]
