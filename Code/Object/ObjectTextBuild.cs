using Forp.Game;
using Forp.Object.Building;
using Forp.Object.Unit;
using Sandbox;
using Sandbox.Diagnostics;
using System;
using System.Threading.Tasks;
using static Sandbox.VideoWriter;

namespace Forp.Object;

public class TextBuilding : Obj
{
	[Property] public GameObject ObjectToBuild { get; set; } 
	[Property] public int ProductionToBuild { get; set; } 

	public Hex BelongingHex { get; set; }
	public bool FromUnit { get; set; }
	public int TotalProduction { get; set; }

	public virtual bool CanBeBuilt(Hex Hex)
	{
		return true;
	}

	public override void OnClick()
	{
		base.OnClick();

		var BelongingUnit = GameObject.Parent.GetComponent<ObjectUnit>();
		GameManager.Instance.Server_CreateHexBuildingObject(ObjectToBuild, BelongingUnit.OwnerHex, true, Connection.Local.Id);
	}
}
