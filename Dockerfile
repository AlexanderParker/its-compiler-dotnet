FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY src/ src/
COPY samples/Its.Compiler.Service/ samples/Its.Compiler.Service/
RUN dotnet publish samples/Its.Compiler.Service/Its.Compiler.Service.csproj --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Its.Compiler.Service.dll"]
