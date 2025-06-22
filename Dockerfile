# Build stage
FROM mcr.microsoft.com/dotnet/sdk:latest AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY KaiAssistant.API/KaiAssistant.API.csproj ./KaiAssistant/KaiAssistant.API/
COPY KaiAssistant.Application/KaiAssistant.Application.csproj ./KaiAssistant/KaiAssistant.Application/
COPY KaiAssistant.Domain/KaiAssistant.Domain.csproj ./KaiAssistant/KaiAssistant.Domain/
COPY KaiAssistant.Infrastructure/KaiAssistant.Infrastructure.csproj ./KaiAssistant/KaiAssistant.Infrastructure/

RUN dotnet restore ./KaiAssistant/KaiAssistant.API/KaiAssistant.API.csproj

# Copy all source code including the docs folder inside KaiAssistant.API
COPY . .

# Publish the app
RUN dotnet publish ./KaiAssistant.API/KaiAssistant.API.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:latest
WORKDIR /app

# Copy published app files
COPY --from=build /app/publish .

# Copy the docs folder with the resume file from the build context into the container
COPY KaiAssistant.API/docs ./docs

EXPOSE 5000

ENTRYPOINT ["dotnet", "KaiAssistant.API.dll"]
