# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview-alpine AS build
WORKDIR /src

ARG PROJECT=src/MRP.Api/MRP.Api.csproj
ARG ASSEMBLY_NAME=MRP.Api

COPY MRP.sln Directory.Build.props ./
COPY src/MRP.Domain/MRP.Domain.csproj src/MRP.Domain/
COPY src/MRP.Application/MRP.Application.csproj src/MRP.Application/
COPY src/MRP.Infrastructure/MRP.Infrastructure.csproj src/MRP.Infrastructure/
COPY src/MRP.Agents/MRP.Agents.csproj src/MRP.Agents/
COPY src/MRP.Api/MRP.Api.csproj src/MRP.Api/
COPY src/MRP.Dashboard/MRP.Dashboard.csproj src/MRP.Dashboard/
COPY src/MRP.Shared/MRP.Shared.csproj src/MRP.Shared/

RUN dotnet restore MRP.sln

COPY . .
RUN dotnet publish ${PROJECT} -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview-alpine AS runtime
WORKDIR /app

ARG ASSEMBLY_NAME=MRP.Api

RUN addgroup -S mrp && adduser -S mrp -G mrp
USER mrp

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV APP_DLL=${ASSEMBLY_NAME}.dll
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:8080/health || exit 1

ENTRYPOINT dotnet ${APP_DLL}
