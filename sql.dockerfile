FROM mcr.microsoft.com/mssql/server:2019-latest

USER root

RUN apt-get update && apt-get install unzip -y

RUN wget -progress=bar:force -q -O sqlpackage.zip https://go.microsoft.com/fwlink/?linkid=2128144 \
    && unzip -qq sqlpackage.zip -d /opt/sqlpackage \
    && chmod u+x /opt/sqlpackage/sqlpackage

RUN mkdir /orleans-data

COPY ./db/orleans.bacpac /orleans-data/orleans.bacpac

ENV ACCEPT_EULA="Y"
ENV SA_PASSWORD="p@ssw0rd"

# Launch SQL Server, confirm startup is complete, deploy the DACPAC, then terminate SQL Server.
# See https://stackoverflow.com/a/51589787/488695
RUN ( /opt/mssql/bin/sqlservr & ) | grep -q "Service Broker manager has started" \
    && /opt/sqlpackage/sqlpackage /a:Import /tsn:. /tdn:orleans /tu:sa /tp:p@ssw0rd /sf:/orleans-data/orleans.bacpac \
    && pkill sqlservr
