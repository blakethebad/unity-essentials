using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public struct NeovimInfo
{
	public string Path;
	public string Presentation; //TODO: Basically name
	public string BuildNumber; //TODO: Probably extremely useless in my case
}

internal class NeovimScriptEditorConfig : ScriptableSingleton<NeovimScriptEditorConfig>
{
	[SerializeField] internal bool hasChanges;
	[SerializeField] internal bool shouldLoadEditorPlugin;
	[SerializeField] internal bool initializedOnce;
	[SerializeField] internal NeovimInfo[] installations;
	[SerializeField] internal string[] activeScriptCompilationDefines;


    public bool HasChangesInCompilationDefines()
    {
		if(activeScriptCompilationDefines == null)
			return false;

		return !EditorUserBuildSettings.activeScriptCompilationDefines.SequenceEqual(activeScriptCompilationDefines);
    }

}


