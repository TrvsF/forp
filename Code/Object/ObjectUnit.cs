using Forp.Game;
using Sandbox;
using Sandbox.Citizen;
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

		if (Random.Shared.FromList(ObjectUnit.OwnerHex.AllBrothers.ToList()) is { } MoveHex)
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

	public int ActionPoints { get; set; }
	public int ActionPointsSpent { get; set; }

	public FUpgrade Upgrade { get; set; }
	public bool IsAi { get; set; }
	public Hex HomeHex { get; set; } // TODO : AI ONLY DATA
}

public class ObjectUnit : Obj
{
	[RequireComponent] public HighlightOutline HighlightOutline { get; set; }
	[RequireComponent] public CitizenAnimationHelper Animation { get; set; }

	[Property] public List<GameObject> Buildings { get; set; }
	[Property] public int ViewRange { get; set; }
	[Property] public int ActionPoints { get; set; }
	[Property] public int ProductionToBuild { get; set; }
	[Property] public int GoldToBuild { get; set; }


	public bool IsAi { get => GetComponent<AiUnit>() != null; }

	protected override void OnStart()
	{
		base.OnStart();

		HighlightOutline.Enabled = false;

		if (OwnerPlayer != null)
		{
			ModelRenderer.Tint = GameManager.Instance.GetTintColour(OwnerPlayer.ConnectionId);
		}

		if (IsAi)
		{
			ModelRenderer.Tint = GameManager.Instance.GetTintColour(GameManager.AiGuid);
			return;
		}

		if (GamePlayer.Local == null)
		{
			if (GameManager.Instance.Mode != EGameManagerMode.Menu)
			{
				Log.Warning("game player somehow not valid, not good!");
			}
			return;
		}

		SetCameraMode(GamePlayer.Local.CameraMode);
	}

	protected override void OnDestroy()
	{
		ShowHealth = false;
		ShowBuildings = false;

		base.OnDestroy();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if (GameManager.Instance.Mode != EGameManagerMode.Menu)
		{	
			Animation.LookAtEnabled = true;
			Animation.LookAt = GamePlayer.Local.Camera.GameObject;
		}
	}

	private FUpgrade Upgrade { get; set; } = null;
	public void ApplyUpgrade(FUpgrade InUpgrade)
	{
		Assert.NotNull(InUpgrade);

		if (Upgrade != null)
		{
			return;
		}

		Upgrade = InUpgrade;

		Attack += Upgrade.AttackModifyer;
		Health += Upgrade.HealthModifyer;

		foreach (var BuildingId in Upgrade.BuildingIds)
		{
			Buildings.Add(BuildingId);
		}

		SetCameraMode(GamePlayer.Local.CameraMode);
	}


	protected override void OnDamageTaken_Internal()
	{
		base.OnDamageTaken_Internal();

		ToggleHealth(ShowHealth);
	}

	public void SetCameraMode(ECameraMode CameraMode)
	{
		ShowHealth = CameraMode == ECameraMode.Combat;
		ShowBuildings = CameraMode == ECameraMode.Build;
	}

	private bool _showBuildings = false;
	public bool ShowBuildings
	{
		get => _showBuildings;
		set
		{
			if (OwnerPlayer == null || GamePlayer.Local == null)
			{
				return;
			}

			if (GamePlayer.Local.ConnectionId != OwnerPlayer.ConnectionId)
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
		List<GameObject> ValidBuildings = new();
		foreach (var Building in Buildings)
		{
			if (Building.IsValid() && Building.GetComponent<TextBuilding>().CanBeBuilt(OwnerHex))
			{
				ValidBuildings.Add(Building);
			}
		}

		for (int BuildingIndex = 0; BuildingIndex < ValidBuildings.Count; ++BuildingIndex)
		{
			var TextBuilding = ValidBuildings[BuildingIndex];
			var TextBuildingComponent = TextBuilding.GetComponent<TextBuilding>();

			Transform Transform = new();

			float HorizontalOffset = (BuildingIndex - (ValidBuildings.Count - 1) / 2f) * 100f;
			Transform.Position += new Vector3(0f, HorizontalOffset, 150f);
			
			var BuildingComponent = GamePlayer.SpawnObject<TextBuilding>(TextBuilding, Transform, Connection.Local, GameObject);

			BuildingComponent.BelongingHex = OwnerHex;
			TextBuildingObjects.Add(BuildingComponent.GameObject);
		}
	}
}
