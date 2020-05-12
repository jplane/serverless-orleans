FROM mcr.microsoft.com/mssql/server:2019-latest

USER root

RUN mkdir /orleans-data

COPY ./db/orleans.mdf /orleans-data
COPY ./db/orleans.ldf /orleans-data

ENV ACCEPT_EULA="Y"
ENV SA_PASSWORD="p@ssw0rd"

HEALTHCHECK --interval=10s  \
	CMD /opt/mssql-tools/bin/sqlcmd -S . -U sa -P p@ssw0rd \
		-Q "CREATE DATABASE [orleans] ON (FILENAME = '/orleans-data/orleans.mdf'),(FILENAME = '/orleans-data/orleans.ldf') FOR ATTACH"
