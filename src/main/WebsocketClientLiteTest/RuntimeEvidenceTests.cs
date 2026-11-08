using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using WebsocketClientLite;
using Xunit;
using Xunit.Abstractions;

namespace WebsocketClientLiteTest;

// Emit from inside the test process: installed SDKs do not identify its runtime.
public class RuntimeEvidenceTests
{
    private readonly ITestOutputHelper _output;

    public RuntimeEvidenceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void RecordHostRuntime()
    {
        var library = typeof(ClientWebSocketRx).Assembly;
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        _output.WriteLine("PDD_HOST_RUNTIME=" + RuntimeInformation.FrameworkDescription);
        _output.WriteLine("PDD_HOST_IDENTITY=" + JsonSerializer.Serialize(new
        {
            runtime = RuntimeInformation.FrameworkDescription,
            runtime_version = Environment.Version.ToString(),
            test_framework = typeof(RuntimeEvidenceTests).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName,
            os,
            os_description = RuntimeInformation.OSDescription,
            os_version = Environment.OSVersion.VersionString,
            os_release = OperatingSystem.IsLinux() ? File.ReadAllText("/etc/os-release") : "",
            os_architecture = RuntimeInformation.OSArchitecture.ToString(),
            process_architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            library_framework = library.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName,
            library_name = library.FullName,
            library_path = library.Location,
            library_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(library.Location))).ToLowerInvariant(),
            library_mvid = library.ManifestModule.ModuleVersionId.ToString()
        }));
    }
}
