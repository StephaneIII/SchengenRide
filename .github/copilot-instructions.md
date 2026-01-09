# SamkørselApp - AI Coding Guidelines

## Project Overview
This is a Danish ridesharing/carpooling application ("SamkørselApp") built with ASP.NET Core 9.0. Currently in early development with minimal implementation - a foundation for building a transportation sharing platform.

## Architecture & Structure

### Project Setup
- **Main Project**: `MyWebApp.csproj` - ASP.NET Core 9.0 web application
- **Solution**: `SamkørselApp.sln` - contains single web app project
- **Entry Point**: `Program.cs` - minimal API setup with single "Hello World" endpoint
- **Configuration**: Standard ASP.NET Core config pattern with `appsettings.json`

### Key Conventions

#### Namespace Inconsistency
- Helper classes use `MyUrlListener.Helper` namespace (see `Helper/ConnectionStringGetter.cs`)
- This suggests potential legacy code or template remnants - maintain consistency when adding new code

#### Configuration Access Pattern
The project has a custom `ConnectionStringGetter` class that manually builds configuration:
```csharp
// Helper/ConnectionStringGetter.cs - Custom configuration access
new ConfigurationBuilder().AddJsonFile("appsettings.json").Build()
```
**Note**: This bypasses ASP.NET Core's built-in DI container - consider using standard `IConfiguration` injection instead.

## Development Environment

### Local Development
- **HTTP**: `http://localhost:5009`
- **HTTPS**: `https://localhost:7086` (primary), `http://localhost:5009` (fallback)
- **Environment**: Development mode enabled via `ASPNETCORE_ENVIRONMENT`

### Build & Run Commands
```bash
# Development
dotnet run --project MyWebApp.csproj
# or from solution
dotnet run
```

## Project Context & Domain
This is a **school project** ("Hovedopgave-kursus") focused on building a carpooling/ridesharing application for Danish users. The name "SamkørselApp" translates to "Rideshare App" in English.

### Expected Domain Areas (Not Yet Implemented)
When expanding this application, consider these typical ridesharing domains:
- User management and profiles
- Trip creation and matching
- Booking and payment systems
- Real-time tracking and communication
- Rating and review systems

## Technical Considerations

### .NET 9.0 Features
- Project uses latest .NET 9.0 framework
- Nullable reference types enabled
- Implicit usings enabled - be aware of auto-imported namespaces

### Current Limitations
- No database configuration despite having `ConnectionStringGetter`
- No actual connection string in `appsettings.json`
- Minimal API surface - single endpoint only
- No authentication, authorization, or business logic implemented

## File Organization
- `Helper/` - Utility classes (currently configuration helpers)
- `Properties/` - Launch settings and project metadata
- Standard ASP.NET Core project structure for remaining folders

When adding new features, follow standard ASP.NET Core patterns for controllers, models, services, and maintain clear separation of concerns.