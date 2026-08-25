using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NvimUnity
{
    //Which IGenerator NeovimScriptEditor.CreateGenerator builds. Classic = the
    //legacy-style csproj (VS/Rider shape — verified to load fine in the Roslyn
    //LS); SdkStyleRoslyn = RoslynProjectGenerator's modern SDK-style csproj.
    public enum ProjectGeneratorType
    {
        Classic = 0,
        SdkStyleRoslyn = 1,
    }

    [FilePath("ProjectSettings/NvimUnityConfig.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class NvimUnityConfig : ScriptableSingleton<NvimUnityConfig>
    {
        [Header("Output")]
        [SerializeField] private string m_SolutionName = "Unity";
        [SerializeField] private string m_OutputDirectory = ""; // empty => project root

        [Header("Generation")]
        [SerializeField] private ProjectGeneratorType m_GeneratorType = ProjectGeneratorType.Classic;
        // SDK-style only. Empty/"auto" => mapped per assembly from its API
        // compatibility level (net471 / netstandard2.1) by RoslynProjectGenerator.
        [SerializeField] private string m_TargetFramework = "";
        // SDK-style only. Empty/"latest"/"auto" => Unity's own pinned compiler
        // language version (never the SDK's notion of latest).
        [SerializeField] private string m_LangVersion = "";
        [SerializeField] private bool m_IncludePackages = true;
        [SerializeField] private bool m_IncludeTests = true;

        [Header("Auto Sync")]
        [SerializeField] private bool m_AutoSyncOnAssetChange = true;

        [Header("Stability")]
        [SerializeField] private List<AssemblyGuidEntry> m_AssemblyGuids = new();

        [Serializable]
        private struct AssemblyGuidEntry
        {
            public string AssemblyName;
            public string Guid; // stored as string for Unity serialization
        }

        public string SolutionName
        {
            get => string.IsNullOrWhiteSpace(m_SolutionName) ? "Unity" : m_SolutionName.Trim();
            set { m_SolutionName = value; Save(); }
        }

        public string OutputDirectory
        {
            get => m_OutputDirectory ?? "";
            set { m_OutputDirectory = value ?? ""; Save(); }
        }

        public ProjectGeneratorType GeneratorType
        {
            get => m_GeneratorType;
            set { m_GeneratorType = value; Save(); }
        }

        public string TargetFramework
        {
            get => (m_TargetFramework ?? "").Trim(); // may be empty = auto
            set { m_TargetFramework = value; Save(); }
        }

        public string LangVersion
        {
            get => (m_LangVersion ?? "").Trim(); // may be empty = auto/pinned
            set { m_LangVersion = value; Save(); }
        }

        public bool IncludePackages
        {
            get => m_IncludePackages;
            set { m_IncludePackages = value; Save(); }
        }

        public bool IncludeTests
        {
            get => m_IncludeTests;
            set { m_IncludeTests = value; Save(); }
        }

        public bool AutoSyncOnAssetChange
        {
            get => m_AutoSyncOnAssetChange;
            set { m_AutoSyncOnAssetChange = value; Save(); }
        }

        public Guid GetOrCreateGuidForAssembly(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
                throw new ArgumentException("assemblyName is null/empty.");

            for (int i = 0; i < m_AssemblyGuids.Count; i++)
            {
                if (m_AssemblyGuids[i].AssemblyName == assemblyName &&
                    System.Guid.TryParse(m_AssemblyGuids[i].Guid, out var parsed))
                {
                    return parsed;
                }
            }

            var g = System.Guid.NewGuid();
            m_AssemblyGuids.Add(new AssemblyGuidEntry { AssemblyName = assemblyName, Guid = g.ToString("D").ToUpperInvariant() });
            Save();
            return g;
        }

        public void Save() => Save(true);
    }
}
