# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Alcohol Label/Alcohol Label.csproj", "Alcohol Label/"]
RUN dotnet restore "Alcohol Label/Alcohol Label.csproj"

COPY . .
WORKDIR "/src/Alcohol Label"
RUN dotnet publish "Alcohol Label.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["sh", "-c", "dotnet 'Alcohol Label.dll' --urls http://0.0.0.0:${PORT:-8080}"]
