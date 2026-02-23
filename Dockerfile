FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY ILMFoodNBrew.slnx ./
COPY ILMFoodNBrew.Api/ILMFoodNBrew.Api.csproj ILMFoodNBrew.Api/
COPY ILMFoodNBrew.Scraper/ILMFoodNBrew.Scraper.csproj ILMFoodNBrew.Scraper/
COPY ILMFoodNBrew.Shared/ILMFoodNBrew.Shared.csproj ILMFoodNBrew.Shared/
RUN dotnet restore ILMFoodNBrew.Api/ILMFoodNBrew.Api.csproj

COPY . .
RUN dotnet publish ILMFoodNBrew.Api/ILMFoodNBrew.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app
COPY --from=build /app/publish .

RUN mkdir -p /app/wwwroot/data

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENV PORT=8080

ENTRYPOINT ["dotnet", "ILMFoodNBrew.Api.dll"]
