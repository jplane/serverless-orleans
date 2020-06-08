FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build
WORKDIR /src
COPY ./app/message_actor_interfaces ./message_actor_interfaces
COPY ./app/message_actors ./message_actors
RUN dotnet publish "./message_actors/Message.Actors.csproj" -c Release -o /publish

FROM serverlessorleans/backend-base:v1
COPY --from=build /publish ./app
