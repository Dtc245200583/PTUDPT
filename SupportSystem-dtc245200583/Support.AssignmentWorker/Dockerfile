FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Support.AssignmentWorker/Support.AssignmentWorker.csproj Support.AssignmentWorker/
COPY Support.Contracts/Support.Contracts.csproj Support.Contracts/
RUN dotnet restore Support.AssignmentWorker/Support.AssignmentWorker.csproj
COPY . .
RUN dotnet publish Support.AssignmentWorker/Support.AssignmentWorker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Support.AssignmentWorker.dll"]
