# Builds the ASP.NET compile service sample.
#
# The SDK image must be able to restore every target framework the library
# declares, so it is the 10.0 SDK even though the sample itself targets 8.0;
# the 8.0 SDK cannot restore net10.0 and the build fails at restore.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/ src/
COPY samples/InstructionTemplateSpecification.Compiler.Service/ samples/InstructionTemplateSpecification.Compiler.Service/
RUN dotnet publish samples/InstructionTemplateSpecification.Compiler.Service/InstructionTemplateSpecification.Compiler.Service.csproj \
    --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "InstructionTemplateSpecification.Compiler.Service.dll"]
