# The server of the company timesheet as a container, for a demo on the internet (see docs/HOSTING_DEMO.md).
# A company runs the server from its own build against PostgreSQL (docs/API_IT_GUIDE.md); this image is for trying the product.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/ src/
RUN dotnet publish src/Company.Api/Company.Api.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:8.0
# The printed sheet (PDF) needs the font libraries on Linux
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 libfreetype6 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .

# A demo keeps its data in a file that is lost when the container is replaced: that is intended
ENV Database__Provider=sqlite \
    ConnectionStrings__Default="Data Source=/tmp/company-demo.db" \
    Authentication__Mode=Password \
    Demo__Seed=true

# Hosts such as Render give the port in PORT
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet Company.Api.dll"]
