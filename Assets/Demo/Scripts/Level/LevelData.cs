using System.Collections.Generic;
using UnityEngine;

//TODO: For now, because we don't directly have an asset management logic, we will use this.
//Later when we have that system in place we can have a dictionary with item count item id.
[System.Serializable]
public class EntityData
{
	public int count;
	public BoardEntity prefab;
}

[CreateAssetMenu(menuName = "Level/LevelData", fileName = "New Level Data")]
public class LevelData : ScriptableObject
{
	public float levelDuration;
	public List<EntityData> levelEntities;
}
