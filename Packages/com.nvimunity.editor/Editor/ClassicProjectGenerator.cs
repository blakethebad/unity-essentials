using System;
using UnityEngine;
using UnityEditor;
using System.Linq;
using UnityEditor.Compilation;
using System.Collections.Generic;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEditor.PackageManager;
using System.IO;
using System.Text;
using System.Security;
using System.Security.Cryptography;

namespace NvimUnity.Editor
{

    public interface IGenerator
    {
        void SyncProject();
        void SyncIfNeeded(IEnumerable<string> affectedFiles, IEnumerable<string> reimportedFiles, bool checkProjectFiles = false);
        bool HasValidExtension(string file);
        string GetSolutionPath();
        string ProjectDirectory { get; }
        string ProjectName { get; }
        ProjectGenerationFlag ProjectGenerationFlag { get; set; }
    }

    [Flags] //TODO: Remove this later i dont see any point in generating csproj for these
    public enum ProjectGenerationFlag
    {
        None = 0,
        Embedded = 1,
        Local = 2,
        Registry = 4,
        Git = 8,
        BuiltIn = 16,
        Unknown = 32,
        PlayerAssemblies = 64,
        LocalTarBall = 128,
    }

    public class AssemblyUsage
    {
        private readonly HashSet<string> _projectAssemblies = new();
        private readonly HashSet<string> _precompiledAssemblies = new();

        public void AddProjectAssembly(Assembly assembly)
        {
            _projectAssemblies.Add(assembly.name);
        }

        public void AddPrecompiledAssemblies(Assembly assembly)
        {
            _precompiledAssemblies.Add(assembly.name);
        }

        public bool IsProjectAssembly(Assembly assembly) => _projectAssemblies.Contains(assembly.name);
        public bool IsPrecompiledAssembly(Assembly assembly) => _precompiledAssemblies.Contains(assembly.name);
    }

    public class ProjectPart //TODO: Properties to fields
    {
        public string Name { get; }
        public string OutputPath { get; }
        public Assembly Assembly { get; }
        public List<string> AdditionalAssets { get; }
        public string[] SourceFiles { get; }
        public string RootNamespace { get; }
        public Assembly[] AssemblyReferences { get; }
        public string[] CompiledAssemblyReferences { get; }
        public string[] Defines { get; }
        public ScriptCompilerOptions CompilerOptions { get; }

        public static Dictionary<string, ResponseFileData> ResponseFileCache = new();

        public ProjectPart(string name, Assembly assembly, List<string> additionalAssets)
        {
            Name = name;
            Assembly = assembly;
            AdditionalAssets = additionalAssets;
            OutputPath = assembly != null ? assembly.outputPath : "Temp/Bin/Debug";
            SourceFiles = assembly != null ? assembly.sourceFiles : Array.Empty<string>();
    #if UNITY_2020_2_OR_NEWER
            RootNamespace = assembly != null ? assembly.rootNamespace : string.Empty;
    #else
            RootNamespace = UnityEditor.EditorSettings.projectGenerationRootNamespace;
    #endif
            AssemblyReferences = assembly != null ? assembly.assemblyReferences : Array.Empty<Assembly>();
            CompiledAssemblyReferences = assembly != null ? assembly.compiledAssemblyReferences : Array.Empty<string>();
            Defines = assembly != null ? assembly.defines : Array.Empty<string>();
            CompilerOptions = assembly != null ? assembly.compilerOptions : new ScriptCompilerOptions();
        }

        public List<ResponseFileData> GetResponseFileData(string directory)
        {
            if (Assembly == null)
                return new List<ResponseFileData>();

            var data = new List<ResponseFileData>();
            foreach (var response in Assembly.compilerOptions.ResponseFiles)
            {

                var apiCompatibilityLevel = Assembly.compilerOptions.ApiCompatibilityLevel;
                var key = response + ":" + (int)apiCompatibilityLevel;
                if (!ResponseFileCache.TryGetValue(key, out var cachedResponse))
                {
                    var systemDirectories = CompilationPipeline.GetSystemAssemblyDirectories(apiCompatibilityLevel);
                    cachedResponse = CompilationPipeline.ParseResponseFile(response, directory, systemDirectories);
                    ResponseFileCache.Add(key, cachedResponse);
                }

                data.Add(cachedResponse);
            }
            return data;
        }
    }

    public enum ScriptingLanguage //TODO: Dont very much like this
    {
        None,
        CSharp,
    }

    public class ClassicProjectGenerator : IGenerator
    {
        private static string[] _userExtensions;
        private static readonly Dictionary<string, ScriptingLanguage> _builtinExtensions = new()
        {
            { ".cs", ScriptingLanguage.CSharp },
            { ".uxml", ScriptingLanguage.None },
            { ".uss", ScriptingLanguage.None },
            { ".shader", ScriptingLanguage.None },
            { ".compute", ScriptingLanguage.None },
            { ".cginc", ScriptingLanguage.None },
            { ".hlsl", ScriptingLanguage.None },
            { ".glslinc", ScriptingLanguage.None },
            { ".template", ScriptingLanguage.None },
            { ".raytrace", ScriptingLanguage.None },
            { ".json", ScriptingLanguage.None},
            { ".rsp", ScriptingLanguage.None},
            { ".asmdef", ScriptingLanguage.None},
            { ".asmref", ScriptingLanguage.None},
            { ".xaml", ScriptingLanguage.None},
            { ".tt", ScriptingLanguage.None},
            { ".t4", ScriptingLanguage.None},
            { ".ttinclude", ScriptingLanguage.None}
        };

        //Properties (not fields) so they satisfy IGenerator.
        public string ProjectDirectory { get; }
        public string ProjectDirectoryWithSlash;
        public string ProjectName { get; }

        public ProjectGenerationFlag ProjectGenerationFlag { get; set; } = ProjectGenerationFlag.None;
        private Assembly[] _allEditorAssemblies;
        private Assembly[] _allPlayerAssemblies;
        private Assembly[] _allAssemblies;

        private readonly Dictionary<string, PackageInfo> _packageInfoCache = new Dictionary<string, PackageInfo>();
        private readonly Dictionary<string, PackageInfo> _responseFilesCache = new Dictionary<string, PackageInfo>();

        private readonly Dictionary<string, string> _projectGuids = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _normalisedPaths = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _assemblyNames = new Dictionary<string, string>();
        private static char[] _buffer = null;


        public ClassicProjectGenerator(string projectDirectory)
        {
            var normalizedDirectoryPath = projectDirectory.Replace(Path.DirectorySeparatorChar == '\\' ? '/' : '\\', Path.DirectorySeparatorChar);
            ProjectDirectory = Path.GetFullPath(normalizedDirectoryPath);
            ProjectDirectoryWithSlash = ProjectDirectory + Path.DirectorySeparatorChar;
            ProjectName = Path.GetFileName(ProjectDirectory);
        }

        public void SyncIfNeeded(IEnumerable<string> affectedFiles, IEnumerable<string> reimportedFiles, bool checkProjectFiles = false)
        {
            var extensions = EditorSettings.projectGenerationUserExtensions;
            var rootNamespace = EditorSettings.projectGenerationRootNamespace;

            _userExtensions = new string[extensions.Length];
            for (var i = 0; i < extensions.Length; i++)
            {
                _userExtensions[i] = $".{extensions[i]}";
            }

            //Other IDE packages also sync on manifest.json and internal package changes, so maybe we should too

            var hasValidAffectedFile = affectedFiles.Any(ShouldFileBePartOfSolution);
            var hasValidReimport = reimportedFiles.Any((s =>
            {
                var extension = Path.GetExtension(s);
                return extension == ".asmdef" || extension == ".asmref" || Path.GetFileName(s) == "csc.rsp";
            }));

            //Editor data, compilation define and last write time changes could be checked here too
            if (hasValidAffectedFile || hasValidReimport)
            {
                SyncProject();
            }

        }


        public void SyncProject()
        {
            var extensions = EditorSettings.projectGenerationUserExtensions;
            var rootNamespace = EditorSettings.projectGenerationRootNamespace;

            _userExtensions = new string[extensions.Length];
            for (var i = 0; i < extensions.Length; i++)
            {
                _userExtensions[i] = $".{extensions[i]}";
            }

            var postProcessorTypes = TypeCache.GetTypesDerivedFrom<AssetPostprocessor>().ToArray();
            GenerateSolutionAndProjects(postProcessorTypes);
        }

        public string GetSolutionPath()
        {
            return Path.Combine(ProjectDirectory, ProjectName + ".sln");
        }

        private void GenerateSolutionAndProjects(Type[] postProcessorTypes)
        {
            var allAssemblies = GetAllAssemblies();
            var assemblyUsage = new AssemblyUsage();
            foreach (var assembly in allAssemblies)
            {
                if (assembly.sourceFiles.Any(ShouldFileBePartOfSolution))
                    assemblyUsage.AddProjectAssembly(assembly);
                else
                    assemblyUsage.AddPrecompiledAssemblies(assembly);
            }

            var additionalAssets = GetAdditionalAssets();

            var projectParts = new List<ProjectPart>();
            var assemblyNamesWithSource = new HashSet<string>();
            foreach (var assembly in allAssemblies)
            {
                if (!assemblyUsage.IsProjectAssembly(assembly))
                    continue;

                if (assemblyNamesWithSource.Contains(assembly.name))
                    projectParts.Add(new ProjectPart(assembly.name, assembly, new List<string>()));
                else
                {
                    additionalAssets.TryGetValue(assembly.name, out var additinalAssetsForProject);
                    projectParts.Add(new ProjectPart(assembly.name, assembly, additinalAssetsForProject));
                    assemblyNamesWithSource.Add(assembly.name);
                }
            }

            //TODO: Not sure about the bottom part did not understand most of it
            var executingAssemblyName = typeof(ClassicProjectGenerator).Assembly.GetName().Name;
            var executingAssembly = GetAssemblyFromName(executingAssemblyName);
            string[] coreReferences = null;
            foreach (var pair in additionalAssets)
            {
                var assembly = pair.Key;
                var additionalAssetList = pair.Value;

                if (!assemblyNamesWithSource.Contains(assembly))
                {
                    if (coreReferences == null)
                    {
                        coreReferences = executingAssembly?.compiledAssemblyReferences.Where(a =>
                            a.EndsWith("UnityEditor.dll", StringComparison.Ordinal) ||
                            a.EndsWith("UnityEngine.dll", StringComparison.Ordinal) ||
                            a.EndsWith("UnityEngine.CoreModule.dll", StringComparison.Ordinal)).ToArray();
                    }

                    Assembly newAssembly = null;
                    if (executingAssembly != null)
                    {
                        newAssembly = new Assembly(assembly, executingAssembly.outputPath, Array.Empty<string>(),
                                new[] { "UNITY_EDITOR" },
                                Array.Empty<Assembly>(),
                                coreReferences, executingAssembly.flags);
                    }

                    projectParts.Add(new ProjectPart(assembly, newAssembly, additionalAssetList));
                }
            }

            var stringBuilder = new StringBuilder();
            var solutionPath = Path.Combine(ProjectDirectory, $"{ProjectName}.sln");
            var solutionContent = BuildSolutionFileContent(stringBuilder, projectParts, postProcessorTypes);

            //TODO: OnGeneratedSolution() DONOT FORGET
            if (HasFileChanged(solutionPath, solutionContent))
            {
                File.WriteAllText(solutionPath, solutionContent, Encoding.UTF8);
            }
            stringBuilder.Clear();


            foreach (var projectPart in projectParts)
            {
                var hasPlayerAssemblyFlag = !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.PlayerAssemblies);
                var projFileName = !hasPlayerAssemblyFlag ? projectPart.Name :
                    !projectPart.Defines.Contains("UNITY_EDITOR") ? projectPart.Name + ".Player" : projectPart.Name;

                var projFilePath = Path.Combine(ProjectDirectory, $"{projFileName}.csproj");
                var projFileContent = BuildProjectFileContent(stringBuilder, projectPart, assemblyUsage);

                if (Path.GetExtension(projFilePath) == ".csproj")
                {
                    //TODO: OnGeneratedCSProject DONOT FORGET
                }

                if (HasFileChanged(projFilePath, projFileContent))
                {
                    File.WriteAllText(projFilePath, projFileContent, Encoding.UTF8);
                    //TODO: Update last write here DONOT FORGET
                }

                stringBuilder.Clear();
            }
        }

        //TODO: Think about moving project file builder to its own file
        //virtual: the only method a generator variant overrides. RoslynProjectGenerator swaps
        //it for SDK-style output and inherits everything else.
        protected virtual string BuildProjectFileContent(StringBuilder stringBuilder, ProjectPart assembly, AssemblyUsage assemblyUsage)
        {
            var responseFileDatas = assembly.GetResponseFileData(ProjectDirectory);
            var responseFileArgs = GetOtherArgumentsFromResponseFilesData(responseFileDatas);

            var hasPlayerAssemblyFlag = !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.PlayerAssemblies);
            var projFileName = !hasPlayerAssemblyFlag ? assembly.Name :
                !assembly.Defines.Contains("UNITY_EDITOR") ? assembly.Name + ".Player" : assembly.Name;

            //Header
            stringBuilder
                   .AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>")
                   .AppendLine(
                     "<Project ToolsVersion=\"4.0\" DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">")
                   .AppendLine("  <PropertyGroup>")
            .Append("    <LangVersion>").Append(GetLangVersion(responseFileArgs["langversion"], assembly)).AppendLine("</LangVersion>")
            .AppendLine(
              "    <_TargetFrameworkDirectories>non_empty_path_generated_by_nvim_unity</_TargetFrameworkDirectories>")
            .AppendLine(
              "    <_FullFrameworkReferenceAssemblyPaths>non_empty_path_generated_by_nvim_unity</_FullFrameworkReferenceAssemblyPaths>")
            .AppendLine("    <DisableHandlePackageFileConflicts>true</DisableHandlePackageFileConflicts>");
            //TODO? The two dummy paths above only have to be non-empty, check if they can go

            var rulesetPaths = new HashSet<string>(responseFileArgs["ruleset"]);
    #if UNITY_2020_2_OR_NEWER
            if (!string.IsNullOrEmpty(assembly.CompilerOptions.RoslynAnalyzerRulesetPath))
                rulesetPaths.Add(assembly.CompilerOptions.RoslynAnalyzerRulesetPath);
    #endif
            var normalisedRulesetPaths = rulesetPaths.Select(GetNormalisedAssemblyPath);

            foreach (var path in normalisedRulesetPaths)
            {
                stringBuilder.Append("    <CodeAnalysisRuleSet>").Append(path).AppendLine("</CodeAnalysisRuleSet>");
            }

            stringBuilder
              .AppendLine("  </PropertyGroup>")
              .AppendLine("  <PropertyGroup>")
              .AppendLine("    <Configuration Condition=\" '$(Configuration)' == '' \">Debug</Configuration>")
              .AppendLine("    <Platform Condition=\" '$(Platform)' == '' \">AnyCPU</Platform>")
              .AppendLine("    <ProductVersion>10.0.20506</ProductVersion>")
              .AppendLine("    <SchemaVersion>2.0</SchemaVersion>")
              .Append("    <RootNamespace>").Append(assembly.RootNamespace).AppendLine("</RootNamespace>")
              .Append("    <ProjectGuid>{").Append(GetProjectGuid(projFileName)).AppendLine("}</ProjectGuid>")
              .AppendLine(
                "    <ProjectTypeGuids>{E097FAD1-6243-4DAD-9C02-E9B9EFC3FFC1};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids>")
              .AppendLine("    <OutputType>Library</OutputType>")
              .AppendLine("    <AppDesignerFolder>Properties</AppDesignerFolder>")
              .Append("    <AssemblyName>").Append(assembly.Name).AppendLine("</AssemblyName>")
              .AppendLine("    <TargetFrameworkVersion>v4.7.1</TargetFrameworkVersion>")
              .AppendLine("    <FileAlignment>512</FileAlignment>")
              .AppendLine("    <BaseDirectory>.</BaseDirectory>")
              .AppendLine("  </PropertyGroup>")
              .AppendLine("  <PropertyGroup Condition=\" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' \">")
              .AppendLine("    <DebugSymbols>true</DebugSymbols>")
              .AppendLine("    <DebugType>full</DebugType>")
              .AppendLine("    <Optimize>false</Optimize>")
              .Append("    <OutputPath>").Append(assembly.OutputPath).AppendLine("</OutputPath>");

            var defines = new HashSet<string>(assembly.Defines);
            foreach (var responseFileData in responseFileDatas)
                defines.UnionWith(responseFileData.Defines);

            stringBuilder
              .Append("    <DefineConstants>").CompatibleAppendJoin(';', defines).AppendLine("</DefineConstants>")
              .AppendLine("    <ErrorReport>prompt</ErrorReport>");

            var warningLevel = responseFileArgs["warn"].Concat(responseFileArgs["w"]).Distinct().FirstOrDefault();
            stringBuilder
              .Append("    <WarningLevel>").Append(!string.IsNullOrWhiteSpace(warningLevel) ? warningLevel : "4").AppendLine("</WarningLevel>")
              .Append("    <NoWarn>").CompatibleAppendJoin(',', GetNoWarn(responseFileArgs["nowarn"].ToList())).AppendLine("</NoWarn>")
              .Append("    <AllowUnsafeBlocks>").Append(assembly.CompilerOptions.AllowUnsafeCode | responseFileDatas.Any(x => x.Unsafe)).AppendLine("</AllowUnsafeBlocks>");

            AppendWarningAsError(stringBuilder, responseFileArgs["warnaserror"], responseFileArgs["warnaserror-"], responseFileArgs["warnaserror+"]);

            foreach (var docFile in responseFileArgs["doc"])
                stringBuilder.Append("    <DocumentationFile>").Append(docFile).AppendLine("</DocumentationFile>");

            var nullable = responseFileArgs["nullable"].FirstOrDefault();
            if (!string.IsNullOrEmpty(nullable))
                stringBuilder.Append("    <Nullable>").Append(nullable).AppendLine("</Nullable>");

            stringBuilder
              .AppendLine("  </PropertyGroup>")
              .AppendLine("  <PropertyGroup>")
              .AppendLine("    <NoConfig>true</NoConfig>")
              .AppendLine("    <NoStdLib>true</NoStdLib>")
              .AppendLine("    <AddAdditionalExplicitAssemblyReferences>false</AddAdditionalExplicitAssemblyReferences>")
              .AppendLine("    <ImplicitlyExpandNETStandardFacades>false</ImplicitlyExpandNETStandardFacades>")
              .AppendLine("    <ImplicitlyExpandDesignTimeFacades>false</ImplicitlyExpandDesignTimeFacades>")
              .AppendLine("  </PropertyGroup>");

            var analyzers = GetRoslynAnalyzers(assembly, responseFileArgs);
            if (analyzers.Length > 0)
            {
                stringBuilder.AppendLine("  <ItemGroup>");
                foreach (var analyzer in analyzers)
                    stringBuilder.AppendLine($"    <Analyzer Include=\"{analyzer.NormalizePath()}\" />");

                stringBuilder.AppendLine("  </ItemGroup>");
            }

            var additionalFiles = GetRoslynAdditionalFiles(assembly, responseFileArgs);
            if (additionalFiles.Length > 0)
            {

                stringBuilder.AppendLine("  <ItemGroup>");
                foreach (var additionalFile in additionalFiles)
                    stringBuilder.AppendLine($"    <AdditionalFiles Include=\"{additionalFile}\" />");
                stringBuilder.AppendLine("  </ItemGroup>");
            }

            var configFile = GetGlobalAnalyzerConfigFile(assembly);
            if (!string.IsNullOrEmpty(configFile))
            {
                stringBuilder
                  .AppendLine("  <ItemGroup>")
                  .Append("    <GlobalAnalyzerConfigFiles Include=\"").Append(configFile).AppendLine("\" />")
                  .AppendLine("  </ItemGroup>");
            }

            //Body
            stringBuilder.AppendLine("  <ItemGroup>");
            foreach (var file in assembly.SourceFiles)
            {
                var absPath = Path.GetFullPath(file.NormalizePath());
                var path = NvimEditorUtils.SkipPathPrefix(absPath, ProjectDirectory);
                var fullFile = SecurityElement.Escape(path);
                stringBuilder.Append("    <Compile Include=\"").Append(fullFile).AppendLine("\" />");
            }

            var additionals = (IEnumerable<string>)assembly.AdditionalAssets ?? Array.Empty<string>();
            foreach (var additionalAsset in additionals)
                stringBuilder.Append("    <None Include=\"").Append(additionalAsset).AppendLine("\" />");

            var binaryReferences = new HashSet<string>(assembly.CompiledAssemblyReferences);
            foreach (var responseFileData in responseFileDatas)
                binaryReferences.UnionWith(responseFileData.FullPathReferences);

            foreach (var assemblyReference in assembly.AssemblyReferences)
            {
                if (assemblyUsage.IsPrecompiledAssembly(assemblyReference))
                    binaryReferences.Add(assemblyReference.outputPath);
            }

            foreach (var reference in binaryReferences)
            {
                var escapedFullPath = GetNormalisedAssemblyPath(reference);
                var assemblyName = GetAssemblyNameFromPath(reference);

                stringBuilder
                  .Append("    <Reference Include=\"").Append(assemblyName).AppendLine("\">")
                  .Append("      <HintPath>").Append(escapedFullPath).AppendLine("</HintPath>")
                  .AppendLine("    </Reference>");
            }

            if (0 < assembly.AssemblyReferences.Length)
            {
                stringBuilder
                  .AppendLine("  </ItemGroup>")
                  .AppendLine("  <ItemGroup>");

                foreach (var reference in assembly.AssemblyReferences)
                {
                    if (assemblyUsage.IsProjectAssembly(reference))
                    {
                        var referenceName = !hasPlayerAssemblyFlag ? reference.name :
                            !assembly.Defines.Contains("UNITY_EDITOR") ? reference.name + ".Player" : reference.name;
                        stringBuilder
                          .Append("    <ProjectReference Include=\"").Append(referenceName).AppendLine(".csproj\">")
                          .Append("      <Project>{").Append(GetProjectGuid(referenceName)).AppendLine("}</Project>")
                          .Append("      <Name>").Append(referenceName).AppendLine("</Name>")
                          .AppendLine("    </ProjectReference>");
                    }
                }
            }

            stringBuilder
              .AppendLine("  </ItemGroup>")
              .AppendLine("  <Import Project=\"$(MSBuildToolsPath)\\Microsoft.CSharp.targets\" />")
              .AppendLine(
                "  <!-- To modify your build process, add your task inside one of the targets below and uncomment it.")
              .AppendLine("       Other similar extension points exist, see Microsoft.Common.targets.")
              .AppendLine("  <Target Name=\"BeforeBuild\">")
              .AppendLine("  </Target>")
              .AppendLine("  <Target Name=\"AfterBuild\">")
              .AppendLine("  </Target>")
              .AppendLine("  -->")
              .AppendLine("</Project>");

            return stringBuilder.ToString();
        }

        private string BuildSolutionFileContent(StringBuilder stringBuilder, List<ProjectPart> parts, Type[] types)
        {
            stringBuilder
              .AppendLine()
              .AppendLine("Microsoft Visual Studio Solution File, Format Version 11.00")
              .AppendLine("# Visual Studio 2010");
            foreach (var projectPart in parts)
            {
                var hasPlayerAssemblyFlag = !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.PlayerAssemblies);
                var projectName = !hasPlayerAssemblyFlag ? projectPart.Name :
                    !projectPart.Defines.Contains("UNITY_EDITOR") ? projectPart.Name + ".Player" : projectPart.Name;

                // GUID is for C# class libraries
                stringBuilder
                  .Append("Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"")
                  .Append(projectPart.Name)
                  .Append("\", \"")
                  .Append(projectName)
                  .Append(".csproj\", \"{")
                  .Append(GetProjectGuid(projectName))
                  .AppendLine("}\"")
                  .AppendLine("EndProject");
            }

            stringBuilder.AppendLine("Global")
              .AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution")
              .AppendLine("\t\tDebug|Any CPU = Debug|Any CPU")
              .AppendLine("\tEndGlobalSection")
              .AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");

            foreach (var projectPart in parts)
            {
                var hasPlayerAssemblyFlag = !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.PlayerAssemblies);
                var projectName = !hasPlayerAssemblyFlag ? projectPart.Name :
                    !projectPart.Defines.Contains("UNITY_EDITOR") ? projectPart.Name + ".Player" : projectPart.Name;
                var projectGuid = GetProjectGuid(projectName);

                stringBuilder
                  .Append("\t\t{").Append(projectGuid).AppendLine("}.Debug|Any CPU.ActiveCfg = Debug|Any CPU")
                  .Append("\t\t{").Append(projectGuid).AppendLine("}.Debug|Any CPU.Build.0 = Debug|Any CPU");
            }

            stringBuilder.AppendLine("\tEndGlobalSection")
              .AppendLine("\tGlobalSection(SolutionProperties) = preSolution")
              .AppendLine("\t\tHideSolutionNode = FALSE")
              .AppendLine("\tEndGlobalSection")
              .AppendLine("EndGlobal");

            return stringBuilder.ToString();
        }


        public static IEnumerable<string> GetNoWarn(List<string> codes)
        {
    #if UNITY_2020_1 // 2020.1.3 'PlayerSettings' has no 'suppressCommonWarnings', so reflect for it
          var type = typeof(PlayerSettings);
          var propertyInfo = type.GetProperty("suppressCommonWarnings");
          if (propertyInfo != null && propertyInfo.GetValue(null) is bool && (bool)propertyInfo.GetValue(null))
          {
            codes.AddRange(new[] {"0169", "0649"});
          }
    #elif UNITY_2020_2_OR_NEWER
            if (PlayerSettings.suppressCommonWarnings)
                codes.AddRange(new[] { "0169", "0649" });
    #endif

            return codes.Distinct();
        }

        protected string GetNormalisedAssemblyPath(string path)
        {
            if (!_normalisedPaths.TryGetValue(path, out var normalisedPath))
            {
                normalisedPath = Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
                normalisedPath = SecurityElement.Escape(normalisedPath).NormalizePath();
                _normalisedPaths.Add(path, normalisedPath);
            }

            return normalisedPath;
        }

        protected string GetAssemblyNameFromPath(string path)
        {
            if (!_assemblyNames.TryGetValue(path, out var name))
            {
                name = NvimEditorUtils.FileNameWithoutExtension(path);
                _assemblyNames.Add(path, name);
            }

            return name;
        }

        public string GetProjectGuid(string name)
        {
            var guidHashInput = ProjectName + name + "salt";
            if (!_projectGuids.TryGetValue(name, out var guid))
            {
                using (var md5 = MD5.Create())
                {
                    var hash = md5.ComputeHash(Encoding.Default.GetBytes(guidHashInput));
                    guid = new Guid(hash).ToString();
                }
            }

            return guid;
        }

        protected string GetLangVersion(IEnumerable<string> langVersionList, ProjectPart assembly)
        {
            var langVersion = langVersionList.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(langVersion))
                return langVersion;
    #if UNITY_2020_2_OR_NEWER
            return assembly.CompilerOptions.LanguageVersion;
    #else
            return "latest";
    #endif
        }

        protected string[] GetRoslynAnalyzers(ProjectPart assembly, ILookup<string, string> otherResponseFilesData)
        {
    #if UNITY_2020_2_OR_NEWER
            return otherResponseFilesData["analyzer"].Concat(otherResponseFilesData["a"])
              .SelectMany(x => x.Split(';'))
              //TODO: Need roslyn fix defined constraint on assembly for versions lover then 2021
              // #if !ROSLYN_ANALYZER_FIX
              //           .Concat(PluginImporter.GetAllImporters()
              //           .Where(i => !i.isNativePlugin && AssetDatabase.GetLabels(i).SingleOrDefault(l => l == "RoslynAnalyzer") != null)
              //           .Select(i => i.assetPath))
              // #else
              //#endif
              .Concat(assembly.CompilerOptions.RoslynAnalyzerDllPaths)
              .Select(GetNormalisedAssemblyPath)
              .Distinct()
              .ToArray();
    #else
            return otherResponseFilesData["analyzer"].Concat(otherResponseFilesData["a"])
              .SelectMany(x => x.Split(';'))
              .Distinct()
              .Select(GetNormalisedAssemblyPath)
              .ToArray();
    #endif
        }

        protected static string[] GetRoslynAdditionalFiles(ProjectPart assembly, ILookup<string, string> otherResponseFilesData)
        {
            var additionalFilePathsFromCompilationPipeline = Array.Empty<string>();
    #if UNITY_2021_3 // 2021.3 does not expose the property, so reflect for it
          var type = assembly.CompilerOptions.GetType();
          var propertyInfo = type.GetProperty("RoslynAdditionalFilePaths");
          if (propertyInfo != null && propertyInfo.GetValue(assembly.CompilerOptions) is string[] value)
          {
            additionalFilePathsFromCompilationPipeline = value;
          }
    #elif UNITY_2022_2_OR_NEWER // https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Compilation.ScriptCompilerOptions.RoslynAdditionalFilePaths.html
            additionalFilePathsFromCompilationPipeline = assembly.CompilerOptions.RoslynAdditionalFilePaths;
    #endif
            return otherResponseFilesData["additionalfile"]
              .SelectMany(x => x.Split(';'))
              .Concat(additionalFilePathsFromCompilationPipeline)
              .Distinct().ToArray();
        }

        protected static string GetGlobalAnalyzerConfigFile(ProjectPart assembly)
        {
            var configFile = string.Empty;
    #if UNITY_2021_3 // 2021.3 does not expose the property, so reflect for it
          var type = assembly.CompilerOptions.GetType();
          var propertyInfo = type.GetProperty("AnalyzerConfigPath");
          if (propertyInfo != null && propertyInfo.GetValue(assembly.CompilerOptions) is string value)
          {
            configFile = value;
          }
    #elif UNITY_2022_2_OR_NEWER
            configFile = assembly.CompilerOptions.AnalyzerConfigPath; // https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Compilation.ScriptCompilerOptions.AnalyzerConfigPath.html
    #endif

            return configFile;
        }

        protected static void AppendWarningAsError(StringBuilder stringBuilder,
          IEnumerable<string> args, IEnumerable<string> argsMinus, IEnumerable<string> argsPlus)
        {
            var treatWarningsAsErrors = false;
            var warningIds = new List<string>();
            var notWarningIds = new List<string>(argsMinus);

            foreach (var s in args)
            {
                if (s == "+" || s == "") treatWarningsAsErrors = true;
                else if (s == "-") treatWarningsAsErrors = false;
                else warningIds.Add(s);
            }

            warningIds.AddRange(argsPlus);

            stringBuilder.Append("    <TreatWarningsAsErrors>").Append(treatWarningsAsErrors).AppendLine("</TreatWarningsAsErrors>");
            if (warningIds.Count > 0)
                stringBuilder.Append("    <WarningsAsErrors>").CompatibleAppendJoin(';', warningIds).AppendLine("</WarningsAsErrors>");
            if (notWarningIds.Count > 0)
                stringBuilder.Append("    <WarningsNotAsErrors>").CompatibleAppendJoin(';', notWarningIds).AppendLine("</WarningsNotAsErrors>");
        }

        //TODO: Understand this
        protected static ILookup<string, string> GetOtherArgumentsFromResponseFilesData(List<ResponseFileData> responseFilesData)
        {
            var paths = responseFilesData.SelectMany(x =>
              {
                  return x.OtherArguments
                  .Where(a => a.StartsWith("/", StringComparison.Ordinal) || a.StartsWith("-", StringComparison.Ordinal))
                  .Select(b =>
                  {
                      var index = b.IndexOf(":", StringComparison.Ordinal);
                      if (index > 0 && b.Length > index)
                      {
                          var key = b.Substring(1, index - 1);
                          return new KeyValuePair<string, string>(key, b.Substring(index + 1));
                      }

                      const string warnaserror = "warnaserror";
                      if (b.Substring(1).StartsWith(warnaserror, StringComparison.Ordinal))
                      {
                          return new KeyValuePair<string, string>(warnaserror, b.Substring(warnaserror.Length + 1));
                      }
                      const string nullable = "nullable";
                      if (b.Substring(1).StartsWith(nullable, StringComparison.Ordinal))
                      {
                          var res = b.Substring(nullable.Length + 1);
                          if (string.IsNullOrWhiteSpace(res) || res.Equals("+"))
                              res = "enable";
                          else if (res.Equals("-"))
                              res = "disable";
                          return new KeyValuePair<string, string>(nullable, res);
                      }

                      return default;
                  });
              })
              .Distinct()
              .ToLookup(o => o.Key, pair => pair.Value);

            return paths;
        }

        private bool HasFileChanged(string path, string newContent)
        {
            try
            {
                if (!File.Exists(path))
                    return true;

                const int bufferSize = 100 * 1024;

                if (_buffer == null)
                    _buffer = new char[bufferSize];

                using (var reader = new StreamReader(path))
                {
                    int read, offset = 0;
                    do
                    {
                        read = reader.ReadBlock(_buffer, 0, _buffer.Length);
                        for (var i = 0; i < read; i++)
                        {
                            if (_buffer[i] != newContent[offset + i])
                                return true;
                        }

                        offset += read;
                    } while (read > 0);

                    var isSame = offset == newContent.Length;
                    return !isSame;
                }
            }
            catch
            {
                return true;
            }

        }

        //TODO: GO over very carefully because I might not want to use this in the end we will see
        private Dictionary<string, List<string>> GetAdditionalAssets()
        {
            var assemblyDllNames = new FilePathTrie<string>();
            var interestingAssets = new List<string>();
            var assetPaths = AssetDatabase.GetAllAssetPaths();
            foreach (var assetPath in assetPaths)
            {
                if (IsInternalizedPackagePath(assetPath))
                    continue;

                if (assetPath.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)
                        || assetPath.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase))
                {
                    var assemblyDllName = CompilationPipeline.GetAssemblyNameFromScriptPath(assetPath);
                    assemblyDllNames.Insert(Path.GetDirectoryName(assetPath), assemblyDllName);
                }

                interestingAssets.Add(assetPath);
            }

            const string fallbackAssemblyDllName = "Assembly-CSharp.dll";

            var assetsByAssemblyDll = new Dictionary<string, List<string>>();
            foreach (var asset in interestingAssets)
            {
                if (AssetDatabase.IsValidFolder(asset))
                {

                }
                else
                {
                    var extension = Path.GetExtension(asset);
                    if (!asset.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && (_builtinExtensions.ContainsKey(extension) || _userExtensions.Contains(extension)))
                    {
                        var assemblyDllName = assemblyDllNames.FindClosestMatch(asset);
                        if (string.IsNullOrEmpty(assemblyDllName))
                        {
                            assemblyDllName = CompilationPipeline.GetAssemblyNameFromScriptPath($"{asset}.cs");
                        }

                        if (string.IsNullOrEmpty(assemblyDllName))
                            assemblyDllName = fallbackAssemblyDllName;

                        if (!assetsByAssemblyDll.TryGetValue(assemblyDllName, out var assets))
                        {
                            assets = new List<string>();
                            assetsByAssemblyDll[assemblyDllName] = assets;
                        }

                        var absolutePath = Path.GetFullPath(asset.NormalizePath());
                        var path = NvimEditorUtils.SkipPathPrefix(absolutePath, ProjectDirectoryWithSlash);

                        var escapedPath = SecurityElement.Escape(path);
                        assets.Add(escapedPath);
                    }
                }
            }

            var assetsByAssemblyName = new Dictionary<string, List<string>>(assetsByAssemblyDll.Count);
            foreach (var entry in assetsByAssemblyDll)
            {
                var assemblyName = NvimEditorUtils.FileNameWithoutExtension(entry.Key);
                assetsByAssemblyName[assemblyName] = entry.Value;
            }

            return assetsByAssemblyName;
        }

        public Assembly GetAssemblyFromName(string name)
        {
            if (_allAssemblies == null || _allAssemblies.Length <= 0)
                _allAssemblies = GetAllAssemblies();

            foreach (var assembly in _allAssemblies)
            {
                if (assembly.name == name)
                    return assembly;
            }
            return null;
        }

        public Assembly[] GetAllAssemblies()
        {
            if (_allEditorAssemblies == null)
            {
                _allEditorAssemblies = GetAssembliesByType(AssembliesType.Editor);
                _allAssemblies = _allEditorAssemblies;
            }

            if (ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.PlayerAssemblies))
            {
                if (_allPlayerAssemblies == null)
                {
                    _allPlayerAssemblies = GetAssembliesByType(AssembliesType.Player);
                    _allAssemblies = new Assembly[_allEditorAssemblies.Length + _allPlayerAssemblies.Length];
                    Array.Copy(_allEditorAssemblies, _allAssemblies, _allEditorAssemblies.Length);
                    Array.Copy(_allPlayerAssemblies, 0, _allAssemblies, _allEditorAssemblies.Length, _allPlayerAssemblies.Length);
                }
            }
            return _allAssemblies;
        }

        private Assembly[] GetAssembliesByType(AssembliesType type)
        {
            var compilationAssemblies = CompilationPipeline.GetAssemblies(type);

            var assemblies = new Assembly[compilationAssemblies.Length];
            var i = 0;
            foreach (var compilationPipelineAssembly in compilationAssemblies)
            {
                var outputPath = type == AssembliesType.Editor
                  ? $@"Temp\Bin\Debug\{compilationPipelineAssembly.name}\"
                  : $@"Temp\Bin\Debug\{compilationPipelineAssembly.name}\Player\";
                assemblies[i] = new Assembly(
                        compilationPipelineAssembly.name,
                        outputPath,
                        compilationPipelineAssembly.sourceFiles,
                        compilationPipelineAssembly.defines,
                        compilationPipelineAssembly.assemblyReferences,
                        compilationPipelineAssembly.compiledAssemblyReferences,
                        compilationPipelineAssembly.flags,
                        compilationPipelineAssembly.compilerOptions,
    #if UNITY_2020_2_OR_NEWER
       compilationPipelineAssembly.rootNamespace
    #endif
                        );
                i++;
            }

            return assemblies;
        }

        private bool ShouldFileBePartOfSolution(string file)
        {
            if (IsInternalizedPackagePath(file))
                return false;

            return HasValidExtension(file);
        }

        public bool HasValidExtension(string file)
        {
            if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var extension = Path.GetExtension(file);
            return _builtinExtensions.ContainsKey(extension) || _userExtensions.Contains(extension);
        }

        private bool IsInternalizedPackagePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var packageInfo = GetPackageInfoForAssetPath(path);
            if (packageInfo == null)
            {
                return false;
            }

            var packageSource = packageInfo.source;
            switch (packageSource)
            {
                case PackageSource.Embedded:
                    return !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.Embedded);
                case PackageSource.Registry:
                    return !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.Registry);
                case PackageSource.BuiltIn:
                    return !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.BuiltIn);
                case PackageSource.Unknown:
                    return !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.Unknown);
                case PackageSource.Local:
                    return !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.Local);
                case PackageSource.Git:
                    return !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.Git);
    #if UNITY_2019_3_OR_NEWER
                case PackageSource.LocalTarball:
                    return !ProjectGenerationFlag.HasFlag(ProjectGenerationFlag.LocalTarBall);
    #endif
            }

            return false;
        }

        private PackageInfo GetPackageInfoForAssetPath(string assetPath)
        {
            string packageName = null;
            const string packagesPrefix = "packages/";
            if (assetPath.StartsWith(packagesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var followUpSeparator = assetPath.IndexOf('/', packagesPrefix.Length);
                packageName = followUpSeparator == -1 ? assetPath : assetPath.Substring(0, followUpSeparator);
            }

            if (packageName == null)
            {
                return null;
            }

            if (_packageInfoCache.TryGetValue(packageName, out var cachedPackageInfo))
            {
                return cachedPackageInfo;
            }

            var packageNameUpper = packageName.ToUpperInvariant();
            if (_packageInfoCache.TryGetValue(packageNameUpper, out cachedPackageInfo))
            {
                return cachedPackageInfo;
            }

            //Not adding directly setting care
            var result = PackageInfo.FindForAssetPath(packageName);
            _packageInfoCache[packageName] = result;
            _packageInfoCache[packageName] = result;

            return result;
        }


        internal class FilePathTrie<TData>
        {
            private static readonly char[] Separators = { '\\', '/' };

            private readonly TrieNode m_Root = new TrieNode();

            private class TrieNode
            {
                public Dictionary<string, TrieNode> Children;
                public TData Data;
            }

            public void Insert(string filePath, TData data)
            {
                var parts = filePath.Split(Separators);

                var node = m_Root;
                foreach (var part in parts)
                {
                    if (node.Children == null)
                        node.Children = new Dictionary<string, TrieNode>(StringComparer.OrdinalIgnoreCase);
                    if (!node.Children.ContainsKey(part))
                        node.Children[part] = new TrieNode();

                    node = node.Children[part];
                }

                node.Data = data;
            }

            public TData FindClosestMatch(string filePath)
            {
                var parts = filePath.Split(Separators);

                var node = m_Root;
                foreach (var part in parts)
                {
                    if (node.Children != null && node.Children.TryGetValue(part, out var next))
                        node = next;
                    else
                        break;
                }

                return node.Data;
            }
        }


    }

    public static class NvimEditorUtils
    {

        public static bool EditorPathExists(string editorPath)
        {
    		return (SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX && new DirectoryInfo(editorPath).Exists)
    				|| (SystemInfo.operatingSystemFamily != OperatingSystemFamily.MacOSX && new FileInfo(editorPath).Exists);
        }

        public static string NormalizePath(this string path)
        {
            return path.Replace(Path.DirectorySeparatorChar == '\\'
              ? '/'
              : '\\', Path.DirectorySeparatorChar);
        }

        public static StringBuilder CompatibleAppendJoin(this StringBuilder stringBuilder, char separator, IEnumerable<string> parts)
        {
            var first = true;
            foreach (var part in parts)
            {
                if (!first)
                    stringBuilder.Append(separator);
                stringBuilder.Append(part);
                first = false;
            }

            return stringBuilder;
        }

        public static string SkipPathPrefix(string path, string prefix)
        {
            var root = prefix[prefix.Length - 1] == Path.DirectorySeparatorChar ? prefix : prefix + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.Ordinal) ? path.Substring(root.Length) : path;
        }

        public static string FileNameWithoutExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }

            var indexOfDot = -1;
            var indexOfSlash = 0;
            for (var i = path.Length - 1; i >= 0; i--)
            {
                if (indexOfDot == -1 && path[i] == '.')
                {
                    indexOfDot = i;
                }

                if (indexOfSlash == 0 && path[i] == '/' || path[i] == '\\')
                {
                    indexOfSlash = i + 1;
                    break;
                }
            }

            if (indexOfDot == -1)
            {
                indexOfDot = path.Length;
            }

            return path.Substring(indexOfSlash, indexOfDot - indexOfSlash);
        }


    }
}
