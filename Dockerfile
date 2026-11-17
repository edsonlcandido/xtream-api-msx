FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["xtream-api-msx.csproj", "./"]
RUN dotnet restore "xtream-api-msx.csproj"

COPY . .
RUN dotnet publish "xtream-api-msx.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "xtream-api-msx.dll"]
