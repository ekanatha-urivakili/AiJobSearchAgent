FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY AiJobSearchAgent.slnx ./
COPY src/AiJobSearchAgent.Core/AiJobSearchAgent.Core.csproj src/AiJobSearchAgent.Core/
COPY src/AiJobSearchAgent.Worker/AiJobSearchAgent.Worker.csproj src/AiJobSearchAgent.Worker/
RUN dotnet restore src/AiJobSearchAgent.Worker/AiJobSearchAgent.Worker.csproj
COPY . .
RUN dotnet publish src/AiJobSearchAgent.Worker/AiJobSearchAgent.Worker.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AiJobSearchAgent.Worker.dll"]
