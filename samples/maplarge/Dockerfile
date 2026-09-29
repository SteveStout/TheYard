# The Shed in a container, the way it runs beside TheYard on the same App Service plan.
# Two stages: the SDK publishes, the runtime image serves. The publish carries the
# documents, the sample home and the source files the live samples read (ADR-012),
# because the container is the content root at runtime.

# #region build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY TestProject.csproj ./
RUN dotnet restore TestProject.csproj
COPY . ./
RUN dotnet publish TestProject.csproj -c Release --no-restore -o /app/publish
# #endregion build

# #region runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --chown=app:app --from=build /app/publish/ ./
# The commit the image was built from, for the footer; the deploy sets it.
ARG SHED_COMMIT=unknown
ENV SHED_COMMIT=${SHED_COMMIT}
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "TestProject.dll"]
# #endregion runtime
