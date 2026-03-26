# ---------- BUILD ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
WORKDIR /src/Mgt.Lit.WebFront

RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# ---------- RUNTIME ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 80

ENTRYPOINT ["dotnet", "Mgt.Lit.WebFront.dll"]