using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
// Counts compiler async state machines and runtime-async methods (MethodImplAttributes 0x2000) without loading.
foreach (var path in args)
{
    using var pe = new PEReader(File.OpenRead(path));
    var md = pe.GetMetadataReader();
    int machines = 0, asyncMethods = 0;
    foreach (var h in md.TypeDefinitions)
    {
        var t = md.GetTypeDefinition(h);
        foreach (var ih in t.GetInterfaceImplementations())
        {
            var iface = md.GetInterfaceImplementation(ih).Interface;
            string name = iface.Kind == HandleKind.TypeReference ? md.GetString(md.GetTypeReference((TypeReferenceHandle)iface).Name) : "";
            if (name == "IAsyncStateMachine") machines++;
        }
        foreach (var mh in t.GetMethods())
            if (((int)md.GetMethodDefinition(mh).ImplAttributes & 0x2000) != 0) asyncMethods++;
    }
    Console.WriteLine($"{new FileInfo(path).Length,8} bytes  state machines {machines,3}  runtime-async methods {asyncMethods,3}  {path}");
}
