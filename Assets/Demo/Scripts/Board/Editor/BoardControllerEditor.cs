using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Board))]
public class BoardEditor : Editor
{
	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();

		EditorGUILayout.Space();
		if(GUILayout.Button("Fit Board To Screen"))
		{
			//The fit writes the inverse collider's serialized bounds and moves the collector, so undo records both
			Undo.RecordObject(serializedObject.FindProperty("inverseCollider").objectReferenceValue, "Fit Board To Screen");
			((Board)target).FitBoardToScreen();
		}
	}
}
