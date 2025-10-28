# KaiAssistant API

A clean ASP.NET Core Web API following Domain-Driven Design (DDD) principles with AI assistant capabilities powered by Google Gemini.

## Architecture

This project follows **Domain-Driven Design (DDD)** with a clean architecture:

- **KaiAssistant.Domain**: Core business entities and domain models
- **KaiAssistant.Application**: Business logic, commands, queries, and service interfaces
- **KaiAssistant.Infrastructure**: External concerns (DI, persistence, third-party integrations)
- **KaiAssistant.API**: HTTP endpoints and middleware

## Prerequisites

- .NET 9.0 SDK
- Google Gemini API key
- SMTP server credentials (for email functionality)

## Setup

1. **Clone the repository**

   ```bash
   git clone https://github.com/Kheang1409/ContactFormApi.git
   cd KaiAssistant
   ```

2. **Configure environment variables**

   Copy `.sample.env` to `.env`:

   ```bash
   cp .sample.env .env
   ```

   Edit `.env` with your actual credentials:

   ```env
   SMTP_SERVER=smtp.gmail.com
   SMTP_PORT=465
   SMTP_SENDER_EMAIL=your-email@gmail.com
   SMTP_RECIEVER_EMAIL=recipient@example.com
   SMTP_SENDER_PASSWORD=your-app-password

   GEMINI_API_KEY=your-gemini-api-key
   GEMINI_MODEL_NAME=gemini-2.0-flash:generateContent
   GEMINI_ENDPOINT=https://generativelanguage.googleapis.com/v1beta/models/
   ```

   **Important**: Never commit `.env` file. It's already in `.gitignore`.

3. **Build and run**
   ```bash
   dotnet restore
   dotnet build
   dotnet run --project KaiAssistant.API
   ```

## Running with Docker

1. **Build the image**

   ```bash
   docker build -t kaiassistant-api .
   ```

2. **Run the container**
   ```bash
   docker run -p 5000:5000 \
     -e SMTP_SERVER=smtp.gmail.com \
     -e SMTP_PORT=465 \
     -e SMTP_SENDER_EMAIL=your-email@gmail.com \
     -e SMTP_RECIEVER_EMAIL=recipient@example.com \
     -e SMTP_SENDER_PASSWORD=your-app-password \
     -e GEMINI_API_KEY=your-gemini-api-key \
     -e GEMINI_MODEL_NAME=gemini-2.0-flash:generateContent \
     -e GEMINI_ENDPOINT=https://generativelanguage.googleapis.com/v1beta/models/ \
     kaiassistant-api
   ```

## API Endpoints

- `GET /api/healths` - Health check endpoint
- `POST /api/assistant/ask` - Ask questions to Kai's AI assistant
- `POST /api/contact` - Send contact form messages

## Configuration

The application reads configuration from:

1. Environment variables (highest priority)
2. `appsettings.json` (fallback, contains placeholder values)

Environment variables override appsettings values for security.

## Security Notes

- ✅ Secrets are read from environment variables
- ✅ `.env` file is gitignored
- ✅ `.sample.env` shows required variables with masked values
- ✅ No secrets are hardcoded in source code
- ✅ Docker image doesn't include `.env` files

## Docker Compose (local)

A `docker-compose.yml` is provided at the repository root to run the API service locally.

Quick start:

1. Copy `.env` at repository root and fill secrets (SMTP, Gemini API key, etc.).

2. Build and run the service:

   docker compose up --build

By default the API is published on the port defined by `API_HTTP_PORT` in `.env` (default `5000`).

The `docs` folder from `KaiAssistant.API` is mounted read-only into the container as `/app/docs` so resume data is available to the service.

## License

MIT License
