FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files first to leverage Docker layer cache for restore
COPY KaiAssistant.sln ./
COPY KaiAssistant.API/*.csproj ./KaiAssistant.API/
COPY KaiAssistant.Application/*.csproj ./KaiAssistant.Application/
COPY KaiAssistant.Domain/*.csproj ./KaiAssistant.Domain/
COPY KaiAssistant.Infrastructure/*.csproj ./KaiAssistant.Infrastructure/

# Use BuildKit cache for NuGet packages to speed up restores between builds
RUN --mount=type=cache,target=/root/.nuget/packages \
	dotnet restore ./KaiAssistant.sln

# Copy the rest of the source code
COPY . .

# Publish the app (no restore because we already restored)
RUN dotnet publish ./KaiAssistant.API/KaiAssistant.API.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy published app files from build stage
COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "KaiAssistant.API.dll"]
