param([string]$version)

if ([string]::IsNullOrEmpty($version)) {$version = "0.0.1"}

$msbuild = join-path -path "C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise\MSBuild\15.0\Bin" -childpath "msbuild.exe"
&$msbuild ..\..\interface\IWebsocketLite.PCL\IWebsocketClientLite.Netstandard.csproj /t:Build /p:Configuration="Release"
&$msbuild ..\main\WebsocketClientLite.Netstandard20\WebsocketClientLite.Netstandard20.csproj /t:Build /p:Configuration="Release"

Remove-Item .\NuGet -Force -Recurse
New-Item -ItemType Directory -Force -Path .\NuGet

c:\tools\nuget\NuGet.exe pack WebsocketClientLite.PCL.nuspec -Verbosity detailed -Symbols -OutputDir "NuGet" -Version $version