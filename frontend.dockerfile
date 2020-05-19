FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
WORKDIR /src
COPY ./frontend ./frontend
COPY ./interfaces ./interfaces
RUN dotnet publish "./frontend/frontend.csproj" -c Release -o /publish

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1
WORKDIR /app
COPY --from=build /publish .
ENTRYPOINT ["dotnet", "frontend.dll"]
