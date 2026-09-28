FROM node:24-bookworm-slim AS frontend
WORKDIR /src/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY backend/PharmacyPos.Api/ backend/PharmacyPos.Api/
RUN dotnet publish backend/PharmacyPos.Api -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=backend /app ./
COPY --from=frontend /src/frontend/dist ./wwwroot
COPY LICENSE ./LICENSE
USER $APP_UID
ENTRYPOINT ["dotnet", "PharmacyPos.Api.dll"]
