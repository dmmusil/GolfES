docker run --name sql_server -e 'ACCEPT_EULA=Y' -e 'SA_PASSWORD=P@ssw0rd' -p 1433:1433 -d mcr.microsoft.com/mssql/server:2019-latest
sleep 90
docker exec sql_server /opt/mssql-tools/bin/sqlcmd \
   -S localhost -U SA -P "P@ssw0rd" \
   -Q "if not exists (select * from sys.databases where name='golf') begin create database golf end"

dotnet run --project Golf.SSS.Schema/Golf.SSS.Schema.csproj