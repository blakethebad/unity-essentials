using UnityEngine;
using TMPro;
using UnityEssentials.UI;

public class LevelEndPopup : UIBase
{
	[SerializeField] private TextMeshProUGUI resultText;
	[SerializeField] private TextMeshProUGUI levelScoreText;
	[SerializeField] private TextMeshProUGUI remainingTimeText;

    protected override void OnHide()
    {
    }

    protected override void OnShow(IUIData uiData)
    {
    }
}
