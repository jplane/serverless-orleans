FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
WORKDIR /src
COPY ./backend ./backend
COPY ./actors ./actors
COPY ./interfaces ./interfaces
RUN dotnet publish "./backend/backend.csproj" -c Release -o /publish

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1
WORKDIR /app
COPY --from=build /publish .
ENTRYPOINT ["dotnet", "backend.dll"]
