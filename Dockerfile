FROM mcr.microsoft.com/dotnet/sdk:latest AS build
WORKDIR /src

COPY KaiAssistant.API/KaiAssistant.API.csproj ./KaiAssistant/KaiAssistant.API/
COPY KaiAssistant.Application/KaiAssistant.Application.csproj ./KaiAssistant/KaiAssistant.Application/
COPY KaiAssistant.Domain/KaiAssistant.Domain.csproj ./KaiAssistant/KaiAssistant.Domain/
COPY KaiAssistant.Infrastructure/KaiAssistant.Infrastructure.csproj ./KaiAssistant/KaiAssistant.Infrastructure/

RUN dotnet restore ./KaiAssistant/KaiAssistant.API/KaiAssistant.API.csproj

COPY . .

RUN dotnet publish ./KaiAssistant.API/KaiAssistant.API.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:latest
WORKDIR /app

# Copy published app files
COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "KaiAssistant.API.dll"]
