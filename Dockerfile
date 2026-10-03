# syntax=docker/dockerfile:1

# ---------- Etapa 1: build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY matriculas.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish matriculas.csproj -c Release -o /app --no-restore

# ---------- Etapa 2: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080

COPY --from=build /app .

EXPOSE 8080

ENTRYPOINT ["dotnet", "matriculas.dll"]