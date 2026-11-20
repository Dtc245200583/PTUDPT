FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Support.Api/Support.Api.csproj Support.Api/
COPY Support.Contracts/Support.Contracts.csproj Support.Contracts/
RUN dotnet restore Support.Api/Support.Api.csproj
COPY . .
RUN dotnet publish Support.Api/Support.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Support.Api.dll"]
