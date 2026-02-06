using Forp.Game;
using Sandbox;
using Sandbox.Resources;
using System;

namespace Forp.Object.Unit;

public record FUnit : IObj
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

public class ObjectUnit : Obj
{
	[Property] public List<GameObject> Buildings { get; set; }
	[Property] public int Health { get; set; }
	[Property] public int Attack { get; set; }
	[Property] public int ViewRange { get; set; }
	[Property] public int MoveRange { get; set; }
	[Property] public int ProductionToBuild { get; set; }

	protected override void OnStart()
	{
		base.OnStart();

		// TODO : relationship with FUint 4 attacking without knowing the hex
		// (or just a relation ship with a hex :D
		ShowBuildings = GamePlayer.Local.UnitBuildMenuActive;
	}

	public GamePlayer OwnerPlayer { get; set; }
	public Hex OwnerHex { get; set; }

	protected override void OnDestroy()
	{
		ShowBuildings = false;

		base.OnDestroy();
	}

	private bool _showBuildings = false;
	public bool ShowBuildings
	{
		get => _showBuildings;
		set
		{
			if (_showBuildings == value)
			{
				return;
			}

			_showBuildings = value;
			ToggleBuidlings(_showBuildings);
		}
	}

	private List<GameObject> TextBuildingObjects = new();
	private void ToggleBuidlings(bool Build)
	{
		if (!Build)
		{
			foreach (var TextBuilding in TextBuildingObjects)
			{
				TextBuilding.Destroy();
			}
			TextBuildingObjects.Clear();
			return;
		}

		foreach (var TextBuilding in Buildings)
		{
			var TextBuildingComponent = TextBuilding.GetComponent<TextBuilding>();
			if (!TextBuildingComponent.IsValid() || !TextBuildingComponent.CanBeBuilt(OwnerHex))
			{
				continue;
			}

			Transform Transform = new();
			Transform.Position += Vector3.Up * 150;
			var BuildingComponent = GamePlayer.SpawnObject<TextBuilding>(TextBuilding, Transform, Connection.Local, GameObject);

			BuildingComponent.BelongingHex = OwnerHex;
			TextBuildingObjects.Add(BuildingComponent.GameObject);
		}
	}
}
