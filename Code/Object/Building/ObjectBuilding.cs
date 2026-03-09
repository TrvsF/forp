using Forp.Game;
using Forp.Object.Unit;
using Sandbox;
using System;
using static Sandbox.VideoWriter;

namespace Forp.Object.Building;

public record FBuilding : IObj
{
	public string ObjectId { get; set; }

	public string Name { get; set; }
	public Transform Transform { get; set; }
	public Guid OwnerGuid { get; set; }
	public int TurnsAlive { get; set; }
	public int ViewRange { get; set; }

	public Hex Hex { get; set; }

	public FUpgrade Upgrade { get; set; }
	public int ProductionToBuild { get; set; } // TODO : remove me
}

public class ObjectBuilding : Obj
{
	[Property] public List<GameObject> Buildings { get; set; }
	[Property] public List<GameObject> Units { get; set; }
	[Property] public int ProductionToBuild { get; set; }
	[Property] public int ViewRange { get; set; }

	protected virtual void OnBuildDone()
	{

	}
}
