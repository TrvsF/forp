using Forp.Game;
using Forp.Object;
using Sandbox.UI;
using System;
using System.Collections.Generic;
using System.Text;

namespace Forp.Object.Unit;

public record FUpgrade
{
	public string ObjectId { get; set; }
	public string Name { get; set; }

	public List<GameObject> BuildingIds { get; set; }
	public int AttackModifyer { get; set; }
	public int HealthModifyer { get; set; }
}

public class Upgrade : Obj
{
	[Property] public List<GameObject> Buildings { get; set; }
	[Property] public int AttackModifyer { get; set; }
	[Property] public int HealthModifyer { get; set; }

	private GameText WorldTooltip = null;
	protected override void OnUpdate()
	{
		base.OnUpdate();

		bool ShouldShow = GamePlayer.Local != null && GamePlayer.Local.DraggedObject == GameObject;
		
		if (ShouldShow)
		{
			if (WorldTooltip == null)
			{
				var OwnerString = OwnerPlayer == null ? "AI" : OwnerPlayer.SteamName;

				WorldTooltip = GameText.CreateTextObject<GameText>(new(), $"{Tooltip}");
				WorldTooltip.SetScale(0.05f);
			}

			WorldTooltip.WorldPosition = GamePlayer.Local.DraggedObject.WorldPosition;
			WorldTooltip.WorldPosition += Vector3.Up * 5f;
		} 
		else if (!ShouldShow && WorldTooltip != null)
		{
			WorldTooltip.DestroyGameObject();
			WorldTooltip = null;
		}
	}

	public void ShowTooltip()
	{
		
	}

	public string GetDesciption()
	{
		return $"{Buildings.Count}b\n{AttackModifyer}a\n{HealthModifyer}hp";
	}

	public List<string> GetBuildingIds()
	{
		return Buildings.Select(Building => Building.GetComponent<TextBuilding>().ObjectId).ToList();
	}

	public FUpgrade GetUpgradeData()
	{
		FUpgrade UpgradeData = new()
		{
			ObjectId = ObjectId,
			Name = DisplayName,
			BuildingIds = Buildings,
			AttackModifyer = AttackModifyer,
			HealthModifyer = HealthModifyer,
		};

		return UpgradeData;
	}
}
