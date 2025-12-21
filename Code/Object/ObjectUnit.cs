using Forp.Game;
using Sandbox;
using Sandbox.Resources;
using System;

namespace Forp.Object.Unit;

public record FUnit : IObject
{
	public string ObjectId { get; set; }

	public string Name { get; set; }
	public Transform Transform { get; set; }
	public Guid OwnerGuid { get; set; }

	public Hex Hex { get; set; }
	public int TurnsAlive { get; set; }
	public int ViewRange { get; set; }

	public int Health { get; set; }
	public int Attack { get; set; }

	public int MoveRange { get; set; }
	public int TurnMovementSpent { get; set; }
}

public class ObjectUnit : Object
{
	[Property] public List<GameObject> Buildings { get; set; }
	[Property] public int Health { get; set; }
	[Property] public int Attack { get; set; }
	[Property] public int ViewRange { get; set; }
	[Property] public int MoveRange { get; set; }

	protected override void OnStart()
	{
		base.OnStart();

		// TODO : relationship with FUint 4 attacking without knowing the hex
		// (or just a relation ship with a hex :D
	}

	public GamePlayer OwnerPlayer { get; set; }
	public Hex OwnerHex { get; set; }
}
