param([string]$betaver)

if ([string]::IsNullOrEmpty($betaver)) {
	$version = [Reflection.AssemblyName]::GetAssemblyName((resolve-path '..\interface\IWebsocketLite.PCL\bin\Release\IWebsocketClientLite.PCL.dll')).Version.ToString(3)
	}
else {
	$version = [Reflection.AssemblyName]::GetAssemblyName((resolve-path '..\interface\IWebsocketLite.PCL\bin\Release\IWebsocketClientLite.PCL.dll')).Version.ToString(3) + "-" + $betaver
}

.\build.ps1 $version

Nuget.exe push .\Nuget\WebsocketClientLite.PCL.$version.nupkg -Source https://www.nuget.org