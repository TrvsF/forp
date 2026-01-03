using Forp.Object.Building;
using Sandbox;
using Sandbox.Diagnostics;
using System;
using System.Threading.Tasks;
using static Sandbox.VideoWriter;

namespace Forp.Object;

public class TextBuilding : Object
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
	}
}
