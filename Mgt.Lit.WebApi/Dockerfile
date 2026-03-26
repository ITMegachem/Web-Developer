FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .

WORKDIR /src/Mgt.Lit.WebApi
RUN dotnet restore
RUN dotnet publish -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app/out .

EXPOSE 8080
ENTRYPOINT ["dotnet", "Mgt.Lit.WebApi.dll"]