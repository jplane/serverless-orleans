FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
WORKDIR /src
COPY ./autoscaler ./autoscaler
RUN dotnet publish "./autoscaler/autoscaler.csproj" -c Release -o /publish

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1
WORKDIR /app
COPY --from=build /publish .
ENTRYPOINT ["dotnet", "autoscaler.dll"]
