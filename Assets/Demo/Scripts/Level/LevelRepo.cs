using System.Collections.Generic;
using UnityEssentials.Utilities;

public class LevelRepo : ILevelService
{
	private List<LevelData> _container;

	public LevelRepo(List<LevelData> levels)
	{
		_container = new List<LevelData>();
		_container.AddRange(levels);
	}

    public LevelData GetLevelWithIndex(int levelIndex)
    {
		if(levelIndex < 0 || levelIndex > _container.Count - 1)
		{
			Log.Critical($"Requested a level index of {levelIndex} which is not supported");
			return null;
		}

		return _container[levelIndex];
    }
}

