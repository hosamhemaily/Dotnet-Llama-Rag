# Optional: containerized run of the API. Local development normally uses `dotnet run`
# directly (see README); this is provided for completeness per the project spec.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY DotnetLlamaRag.sln .
COPY Rag.Domain/Rag.Domain.csproj Rag.Domain/
COPY Rag.Application/Rag.Application.csproj Rag.Application/
COPY Rag.Infrastructure/Rag.Infrastructure.csproj Rag.Infrastructure/
COPY Rag.Api/Rag.Api.csproj Rag.Api/
COPY Rag.Tests/Rag.Tests.csproj Rag.Tests/
RUN dotnet restore Rag.Api/Rag.Api.csproj

COPY . .
RUN dotnet publish Rag.Api/Rag.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Rag.Api.dll"]
