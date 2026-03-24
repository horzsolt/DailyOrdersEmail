# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.
# Base runtime image (Windows)
FROM mcr.microsoft.com/dotnet/aspnet:9.0-windowsservercore-ltsc2025 AS base
WORKDIR /app

# Build stage (Windows SDK!)
FROM mcr.microsoft.com/dotnet/sdk:9.0-windowsservercore-ltsc2025 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["OrderEmail.csproj", "."]
RUN dotnet restore "./OrderEmail.csproj"

COPY . .
RUN dotnet build "./OrderEmail.csproj" -c %BUILD_CONFIGURATION% -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./OrderEmail.csproj" -c %BUILD_CONFIGURATION% -o /app/publish /p:UseAppHost=false

# Final stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "DailyOrdersEmail.dll"]
ENV TZ=Europe/Budapest