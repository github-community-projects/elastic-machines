# syntax=docker/dockerfile:1.7-labs

FROM mcr.microsoft.com/dotnet/sdk:10.0.300-alpine3.23 AS build
ARG VERSION=0.0.0.0
WORKDIR /source
COPY --parents *.slnx **/*.csproj Directory.Build.props ./
RUN dotnet restore
COPY ./ ./
RUN dotnet publish ElasticMachines.Monolith/ElasticMachines.Monolith.csproj --no-restore --configuration Release --output /app -property:Version="${VERSION}"

FROM mcr.microsoft.com/dotnet/aspnet:10.0.8-alpine3.23
ARG ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT}
ENV ASPNETCORE_HTTP_PORTS=80
ENV DOTNET_EnableDiagnostics=0
RUN apk add --no-cache tzdata
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "ElasticMachines.Monolith.dll"]
