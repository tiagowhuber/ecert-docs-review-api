FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY EcertDocsReview.slnx .
COPY src/Ecert.DocsReview.Api/Ecert.DocsReview.Api.csproj src/Ecert.DocsReview.Api/
RUN dotnet restore src/Ecert.DocsReview.Api/Ecert.DocsReview.Api.csproj

COPY src/Ecert.DocsReview.Api/ src/Ecert.DocsReview.Api/
RUN dotnet publish src/Ecert.DocsReview.Api/Ecert.DocsReview.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Ecert.DocsReview.Api.dll"]
