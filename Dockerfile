
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["slow-light-requests-gate/slow-light-requests-gate.csproj", "slow-light-requests-gate/"]
RUN dotnet restore "slow-light-requests-gate/slow-light-requests-gate.csproj"
COPY . .
WORKDIR "/src/slow-light-requests-gate"
RUN dotnet build "slow-light-requests-gate.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "slow-light-requests-gate.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "slow-light-requests-gate.dll"]
