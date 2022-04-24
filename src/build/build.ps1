param([string]$version)

if ([string]::IsNullOrEmpty($version)) {$version = "0.0.1"}

$msbuild = join-path -path "C:\Program Files\Microsoft Visual Studio\2022\PReview\MSBuild\Current\Bin" -childpath "msbuild.exe"
&$msbuild ..\interface\IWebsocketClientLite\IWebsocketClientLite.csproj /t:Build /p:Configuration="Release"
&$msbuild ..\main\WebsocketClientLite\WebsocketClientLite.csproj /t:Build /p:Configuration="Release"

Remove-Item .\NuGet -Force -Recurse
New-Item -ItemType Directory -Force -Path .\NuGet

c:\tools\nuget\NuGet.exe pack WebsocketClientLite.PCL.nuspec -Verbosity detailed -Symbols -OutputDir "NuGet" -Version $version