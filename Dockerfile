# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files first to leverage Docker cache for restore
COPY KaiAssistant.sln ./
COPY KaiAssistant.API/*.csproj ./KaiAssistant.API/
COPY KaiAssistant.Application/*.csproj ./KaiAssistant.Application/
COPY KaiAssistant.Domain/*.csproj ./KaiAssistant.Domain/
COPY KaiAssistant.Infrastructure/*.csproj ./KaiAssistant.Infrastructure/

# Restore NuGet packages
RUN dotnet restore ./KaiAssistant.sln

# Copy the remaining source code
COPY . .

# Publish the app
RUN dotnet publish ./KaiAssistant.API/KaiAssistant.API.csproj -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy published app from build stage
COPY --from=build /app/publish .

EXPOSE 8080

# Start the app
ENTRYPOINT ["dotnet", "KaiAssistant.API.dll"]