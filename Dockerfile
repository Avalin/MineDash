# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["MineDash.csproj", "./"]
RUN dotnet restore "MineDash.csproj"

# Copy everything else and build
COPY . .
RUN dotnet build "MineDash.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "MineDash.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Create app_data directory for persistence
RUN mkdir -p /app/app_data

# Copy published app
COPY --from=publish /app/publish .

# Expose port (using 8214 - similar to Jellyfin's 8096 style)
EXPOSE 8214

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8214
ENV ASPNETCORE_ENVIRONMENT=Production

# Entry point
ENTRYPOINT ["dotnet", "MineDash.dll"]

