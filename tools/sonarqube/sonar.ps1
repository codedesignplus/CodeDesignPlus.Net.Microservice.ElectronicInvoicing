#!/usr/bin/env pwsh
#echo "Install dotnet-sonarscanner ----------------------------------------------------------------------------------------------------------------------"
#dotnet tool install --global dotnet-sonarscanner 

Write-Host "Start Sonarscanner -------------------------------------------------------------------------------------------------------------------------------"

$org = "codedesignplus"
$key = "CodeDesignPlus.Net.Microservice.ElectronicInvoicing"
$csproj = "CodeDesignPlus.Net.Microservice.ElectronicInvoicing.sln"
$report = "tests/**/coverage.opencover.xml"
$server = "http://localhost:9000"
$token = $env:SONAR_TOKEN
if (-not $token) { throw "Set SONAR_TOKEN with the token of the local SonarQube" }

cd ../..
dotnet test $csproj /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
dotnet sonarscanner begin /o:$org /k:$key /d:sonar.host.url=$server /d:sonar.coverage.exclusions="**Tests*.cs,**/tests/load/*.js" /d:sonar.cs.opencover.reportsPaths=$report /d:sonar.login=$token
dotnet build
dotnet sonarscanner end /d:sonar.login=$token