FROM mcr.microsoft.com/dotnet/sdk:8.0.419-bookworm-slim@sha256:dd09bcce84d9130e7f3e85c83a8ce9709e1d95e45c48c722a0d7923b38d8024c AS build
WORKDIR /src
COPY DatabaseGenerator/DatabaseGenerator.csproj DatabaseGenerator/
RUN dotnet restore DatabaseGenerator/DatabaseGenerator.csproj
COPY DatabaseGenerator/ DatabaseGenerator/
RUN dotnet publish DatabaseGenerator/DatabaseGenerator.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:GitRevision=container

FROM mcr.microsoft.com/dotnet/runtime:8.0.25-bookworm-slim@sha256:e66b853161cdde692f10863a7f5166048e16f7d6022d1eba03cc62367a540a1a
WORKDIR /app
COPY --from=build /app/publish/ ./
ENTRYPOINT ["dotnet", "/app/DatabaseGenerator.dll"]
