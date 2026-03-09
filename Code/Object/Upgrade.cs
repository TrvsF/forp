using Forp.Object;
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

	public List<string> GetBuildingIds()
	{
		return Buildings.Select(Building => Building.GetComponent<TextBuilding>().ObjectId).ToList();
	}
}

