using Forp.Game;
using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.Resources;
using System;

namespace Forp.Object.Unit;

public class AiUnit : Component
{
	private ObjectUnit ObjectUnit { get => GetComponent<ObjectUnit>(); }

	public void MoveRandomly_ServerOnly()
	{
		Assert.True(Networking.IsHost);

		if (Random.Shared.FromList(ObjectUnit.OwnerHex.AllBrothers) is { } MoveHex)
		{
			GameManager.Instance.Server_MoveUnitToHex(ObjectUnit.OwnerHex, MoveHex, Connection.Local.Id);
		}
	}
}

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

	public bool IsAi { get; set; }
}

public class ObjectUnit : Obj
{
	[Property] public List<GameObject> Buildings { get; set; }
	[Property] public Material SelectedMaterial { get; set; }
	[Property] public int Health { get; set; }
	[Property] public int Attack { get; set; }
	[Property] public int ViewRange { get; set; }
	[Property] public int MoveRange { get; set; }
	[Property] public int ProductionToBuild { get; set; }

	public GamePlayer OwnerPlayer { get; set; }
	public Hex OwnerHex { get; set; }

	protected override void OnStart()
	{
		base.OnStart();

		SetCameraMode(GamePlayer.Local.CameraMode);
	}

	protected override void OnDestroy()
	{
		ShowHealth = false;
		ShowBuildings = false;

		base.OnDestroy();
	}

	[Rpc.Broadcast]
	public void OnDamageTaken(float Damage)
	{
		var TextTransform = WorldTransform;
		TextTransform.Position += Vector3.Up * 150;
		GameText.CreateTextObject<DamageText>(TextTransform, $"-{Damage}");

		// update text if there
		ToggleHealth(ShowHealth);
	}

	public void SetCameraMode(ECameraMode CameraMode)
	{
		ShowHealth = CameraMode == ECameraMode.Combat;
		ShowBuildings = CameraMode == ECameraMode.Build;
	}

	private bool _showHealth = false;
	public bool ShowHealth
	{
		get => _showHealth;
		set
		{
			if (_showHealth == value)
			{
				return;
			}

			_showHealth = value;
			ToggleHealth(_showHealth);
		}
	}

	private GameText HealthText = null;
	private void ToggleHealth(bool Show)
	{
		if (HealthText.IsValid())
		{
			HealthText.DestroyGameObject();
			HealthText = null;
		}

		if (!Show)
		{
			return;
		}

		var TextTransform = WorldTransform;
		TextTransform.Position += Vector3.Up * 200;

		var OwnerString = OwnerPlayer == null ? "AI" : OwnerPlayer.SteamName;
		HealthText = GameText.CreateTextObject<GameText>(TextTransform, $"{DisplayName} : {OwnerString}\n{Health}hp");
	}

	private bool _showBuildings = false;
	public bool ShowBuildings
	{
		get => _showBuildings;
		set
		{
			if (GamePlayer.Local != OwnerPlayer)
			{
				return;
			}

			if (_showBuildings == value)
			{
				return;
			}

			_showBuildings = value;
			ToggleBuidlings(_showBuildings);
		}
	}

	private readonly List<GameObject> TextBuildingObjects = new();
	private void ToggleBuidlings(bool Show)
	{
		foreach (var TextBuilding in TextBuildingObjects)
		{
			TextBuilding.Destroy();
		}
		TextBuildingObjects.Clear();

		if (!Show)
		{
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
