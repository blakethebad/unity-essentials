using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace NvimUnity.Editor
{
    // Unity never imports a package's Samples~ folder, so the CompilationPipeline knows nothing
    // about sample code and it would land in no csproj (Roslyn then reports every Unity type as
    // missing). This synthesizes one assembly per sample asmdef, mirroring what Unity compiles
    // once the sample is imported, so package authors get real diagnostics while editing samples.
    internal static class HiddenSampleAssemblies
    {
        private const string SamplesFolder = "Samples~";
        private const string FallbackTemplate = "Assembly-CSharp";

        [Serializable]
        private class AsmdefData
        {
            public string name;
            public string rootNamespace;
            public string[] references;
            public string[] includePlatforms;
        }

        private class SampleSource
        {
            public string Name;
            public string RootNamespace;
            public string[] References;
            public bool EditorOnly;
            public readonly List<string> Files = new List<string>();
        }

        // isIncluded gets a package's "Packages/<name>" path, so the generation flags decide which
        // packages contribute samples exactly as they do for regular package code.
        public static List<Assembly> Collect(Func<string, bool> isIncluded, ICollection<string> takenNames)
        {
            var unityAssemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                .GroupBy(a => a.name).ToDictionary(g => g.Key, g => g.First());

            var sources = new List<SampleSource>();
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
            {
                if (string.IsNullOrEmpty(package.resolvedPath) || !isIncluded(package.assetPath))
                    continue;
                var samplesRoot = Path.Combine(package.resolvedPath, SamplesFolder);
                if (Directory.Exists(samplesRoot))
                    sources.AddRange(ScanSamples(package, samplesRoot));
            }

            var built = new Dictionary<string, Assembly>();
            foreach (var source in sources)
            {
                if (!takenNames.Contains(source.Name))
                    Build(source, unityAssemblies, sources, built, new HashSet<string>());
            }

            return built.Values.Where(a => !takenNames.Contains(a.name)).ToList();
        }

        private static IEnumerable<SampleSource> ScanSamples(PackageInfo package, string samplesRoot)
        {
            var byDirectory = new Dictionary<string, SampleSource>(StringComparer.OrdinalIgnoreCase);
            foreach (var asmdefPath in Directory.GetFiles(samplesRoot, "*.asmdef", SearchOption.AllDirectories))
            {
                AsmdefData data;
                try
                {
                    data = JsonUtility.FromJson<AsmdefData>(File.ReadAllText(asmdefPath));
                }
                catch (Exception)
                {
                    continue;
                }
                if (data == null || string.IsNullOrEmpty(data.name))
                    continue;

                byDirectory[Path.GetDirectoryName(asmdefPath)] = new SampleSource
                {
                    Name = data.name,
                    RootNamespace = data.rootNamespace ?? string.Empty,
                    References = data.references ?? Array.Empty<string>(),
                    EditorOnly = data.includePlatforms != null && data.includePlatforms.Length == 1
                        && data.includePlatforms[0] == "Editor",
                };
            }

            // asmdef-less samples compile into Assembly-CSharp once imported; group them per sample folder.
            var looseByFolder = new Dictionary<string, SampleSource>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(samplesRoot, "*.cs", SearchOption.AllDirectories))
            {
                var owner = FindOwner(Path.GetDirectoryName(file), samplesRoot, byDirectory);
                if (owner == null)
                {
                    var folder = TopFolder(file, samplesRoot);
                    if (!looseByFolder.TryGetValue(folder, out owner))
                    {
                        owner = new SampleSource
                        {
                            Name = $"{package.name}.Samples.{folder}",
                            RootNamespace = string.Empty,
                            References = null,
                            EditorOnly = false,
                        };
                        looseByFolder[folder] = owner;
                    }
                }
                owner.Files.Add(file);
            }

            return byDirectory.Values.Concat(looseByFolder.Values).Where(s => s.Files.Count > 0);
        }

        private static SampleSource FindOwner(string directory, string samplesRoot, Dictionary<string, SampleSource> byDirectory)
        {
            var root = Path.GetFullPath(samplesRoot).TrimEnd(Path.DirectorySeparatorChar);
            var current = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            while (current.Length >= root.Length)
            {
                if (byDirectory.TryGetValue(current, out var owner))
                    return owner;
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || parent == current)
                    break;
                current = parent;
            }
            return null;
        }

        private static string TopFolder(string file, string samplesRoot)
        {
            var relative = file.Substring(samplesRoot.Length).TrimStart('\\', '/');
            var separator = relative.IndexOfAny(new[] { '\\', '/' });
            return separator < 0 ? "Root" : relative.Substring(0, separator).Replace(' ', '_');
        }

        // Recursive so a sample can reference another sample; `visiting` breaks reference cycles.
        private static Assembly Build(SampleSource source, Dictionary<string, Assembly> unityAssemblies,
            List<SampleSource> sources, Dictionary<string, Assembly> built, HashSet<string> visiting)
        {
            if (built.TryGetValue(source.Name, out var done))
                return done;
            if (!visiting.Add(source.Name))
                return null;

            unityAssemblies.TryGetValue(FallbackTemplate, out var fallback);
            var references = new List<Assembly>();
            if (source.References == null)
            {
                if (fallback != null)
                    references.AddRange(fallback.assemblyReferences);
            }
            else
            {
                foreach (var reference in source.References)
                {
                    var name = ResolveReferenceName(reference);
                    if (string.IsNullOrEmpty(name))
                        continue;
                    if (unityAssemblies.TryGetValue(name, out var unityAssembly))
                        references.Add(unityAssembly);
                    else
                    {
                        var sample = sources.FirstOrDefault(s => s.Name == name);
                        var sampleAssembly = sample != null ? Build(sample, unityAssemblies, sources, built, visiting) : null;
                        if (sampleAssembly != null)
                            references.Add(sampleAssembly);
                    }
                }
            }

            // Engine and precompiled references are the same base set Unity hands every assembly.
            var template = references.FirstOrDefault(r => unityAssemblies.ContainsKey(r.name)) ?? fallback
                ?? unityAssemblies.Values.FirstOrDefault();
            if (template == null)
                return null;

            var assembly = new Assembly(
                source.Name,
                $@"Temp\Bin\Debug\{source.Name}\",
                source.Files.ToArray(),
                template.defines,
                references.ToArray(),
                template.compiledAssemblyReferences,
                source.EditorOnly ? AssemblyFlags.EditorAssembly : AssemblyFlags.None,
                template.compilerOptions,
                source.RootNamespace);
            built[source.Name] = assembly;
            return assembly;
        }

        private static string ResolveReferenceName(string reference)
        {
            const string guidPrefix = "GUID:";
            if (!reference.StartsWith(guidPrefix, StringComparison.OrdinalIgnoreCase))
                return reference;

            var path = AssetDatabase.GUIDToAssetPath(reference.Substring(guidPrefix.Length));
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            try
            {
                return JsonUtility.FromJson<AsmdefData>(File.ReadAllText(path))?.name;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
