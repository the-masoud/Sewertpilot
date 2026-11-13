using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace SewerPilot.OpenFlows.Compatibility
{
    public static class OpenFlowsMetadataInspector
    {
        public static OpenFlowsAssemblyMetadata Inspect(string path)
        {
            RuntimePaths.RejectLinks(path);
            var domain = AppDomain.CreateDomain("SewerPilot.Metadata", null,
                new AppDomainSetup { ApplicationBase = Path.GetDirectoryName(typeof(OpenFlowsMetadataInspector).Assembly.Location) });
            try
            {
                var worker = (MetadataWorker)domain.CreateInstanceAndUnwrap(typeof(MetadataWorker).Assembly.FullName, typeof(MetadataWorker).FullName);
                return worker.Inspect(Path.GetFullPath(path));
            }
            finally { AppDomain.Unload(domain); }
        }
    }

    // Reflection-only inspection cannot execute assembly constructors or API methods.
    public sealed class MetadataWorker : MarshalByRefObject
    {
        public override object InitializeLifetimeService() => null;
        public OpenFlowsAssemblyMetadata Inspect(string path)
        {
            var identity = AssemblyName.GetAssemblyName(path);
            var version = FileVersionInfo.GetVersionInfo(path);
            var assembly = Assembly.ReflectionOnlyLoadFrom(path);
            string target = null;
            foreach (var attribute in CustomAttributeData.GetCustomAttributes(assembly))
                if (attribute.AttributeType.FullName == "System.Runtime.Versioning.TargetFrameworkAttribute")
                    target = attribute.ConstructorArguments[0].Value as string;
            PortableExecutableKinds kinds;
            ImageFileMachine machine;
            assembly.ManifestModule.GetPEKind(out kinds, out machine);
            uint flags = ReadCorFlags(path);
            string architecture = machine == ImageFileMachine.AMD64 ? "x64" :
                machine == ImageFileMachine.I386 && (flags & 1) != 0 && (flags & 2) == 0 && (flags & 0x20000) == 0 ? "AnyCPU" :
                machine == ImageFileMachine.I386 ? "x86" : machine.ToString();
            return new OpenFlowsAssemblyMetadata(path, identity.Name, RuntimePaths.HashFile(path), identity.Version.ToString(),
                version.FileVersion, version.ProductVersion, assembly.ImageRuntimeVersion, target, architecture, flags, version.ProductName);
        }

        private static uint ReadCorFlags(string path)
        {
            using (var stream = File.OpenRead(path)) using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt16() != 0x5A4D) throw new BadImageFormatException("Missing DOS signature.");
                stream.Position = 0x3C; uint pe = reader.ReadUInt32(); stream.Position = pe;
                if (reader.ReadUInt32() != 0x4550) throw new BadImageFormatException("Missing PE signature.");
                reader.ReadUInt16(); ushort sections = reader.ReadUInt16(); stream.Position += 12;
                ushort optionalSize = reader.ReadUInt16(); reader.ReadUInt16(); long optional = stream.Position;
                ushort magic = reader.ReadUInt16();
                if (magic != 0x10B && magic != 0x20B) throw new BadImageFormatException("Unsupported PE header.");
                stream.Position = optional + (magic == 0x20B ? 112 : 96) + 14 * 8;
                uint cliRva = reader.ReadUInt32();
                for (int index = 0; index < sections; index++)
                {
                    stream.Position = optional + optionalSize + index * 40 + 8;
                    uint virtualSize = reader.ReadUInt32(), address = reader.ReadUInt32(), rawSize = reader.ReadUInt32(), raw = reader.ReadUInt32();
                    if (cliRva >= address && cliRva - address < Math.Max(virtualSize, rawSize))
                    { stream.Position = raw + (cliRva - address) + 16; return reader.ReadUInt32(); }
                }
                throw new BadImageFormatException("Missing CLI header.");
            }
        }
    }
}
