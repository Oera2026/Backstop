FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Backstop.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish Backstop.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 5106

# Listen on $PORT if the host sets it, otherwise 5106.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-5106} exec dotnet Backstop.dll"]
