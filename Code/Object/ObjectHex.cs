using Forp.Object.Building;
using Forp.Object.Unit;
using Sandbox;
using Sandbox.Diagnostics;
using System;
using static Forp.Game.GamePlayer;
using Sandbox.UI;
using Forp.Game;

namespace Forp.Object;

public enum EHexType
{
	Grass,
	Sand,
	Water,
}

public sealed class Hex : Object
{
	[Property] GameObject FogPrefab { get; set; }
	GameObject FogObject = null;
	[Property] GameObject LightPrefab { get; set; }
	GameObject LightObject = null;

	[Property] Hex HexTL;
	[Property] Hex HexTR;
	[Property] Hex HexML;
	[Property] Hex HexMR;
	[Property] Hex HexBL;
	[Property] Hex HexBR;

	[Sync(SyncFlags.FromHost), Property] public int Resources { get; set; } = 0;
	[Sync, Property] public EHexType Type { get; set; } = EHexType.Grass;
	[Sync, Property] public Color BaseColour { get; set; } = Color.Black;
	[Sync, Change("RepOwnerChanged")] public FBuilding BuildingOwner { get; set; } = null;

	private void RepOwnerChanged()
	{
		if (BuildingOwner == null)
		{
			Log.Info("removing owner!");
			SetBaseColour(TypeColours[Type]);
			return;
		}

		var GamePlayerOwner = GameManager.Instance.GetGamePlayer(BuildingOwner.OwnerGuid);
		if (GamePlayerOwner == null)
		{
			Log.Warning($"unable to find owner of connection id {BuildingOwner.OwnerGuid}");
			return; 
		}

		SetBaseColour(GamePlayerOwner.Colour);
	}

	private void SetOwner(FBuilding OwnerIn, bool DoBrothers = false)
	{
		BuildingOwner = OwnerIn;

		if (!DoBrothers)
		{
			return;
		}

		foreach (var BrothermanHex in AllBrothers)
		{
			BrothermanHex?.BuildingOwner = OwnerIn;
		}
	}

	public static void RevealHexesRecusrive(Hex Hex, bool Reveal, int Depth)
	{
		if (Hex == null || Depth <= 0) return;

		Hex.IsRevealed = Reveal;

		foreach (var Brother in Hex.AllBrothers)
		{
			if (Brother == null) continue;
			RevealHexesRecusrive(Brother.GetComponent<Hex>(), Reveal, Depth - 1);
		}
	}

	public static void HighlightHexesRecusrive(Hex Hex, bool Highlight, int Depth)
	{
		if (Hex == null || Depth <= 0) return;

		Hex.IsHighlighted = Highlight;

		foreach (var Brother in Hex.AllBrothers)
		{
			if (Brother == null) continue;
			HighlightHexesRecusrive(Brother.GetComponent<Hex>(), Highlight, Depth - 1);
		}
	}

	// TODO : changing a property on the data doesn't run the rep, needed 4 now
	[Rpc.Broadcast]
	public void Multicast_OnTurnEnd()
	{
		RepUnitDataChanged();
		OnRep_BuildingData();
	}

	[Sync(SyncFlags.FromHost), Change("OnRep_BuildingData")] public FBuilding BuildingData { get; set; } = null;
	public ObjectBuilding BuildingObject { get; set; } = null;
	private void OnRep_BuildingData()
	{
		if (BuildingData == null)
		{
			if (BuildingObject.IsValid())
			{
				BuildingObject.GameObject.Destroy();
				BuildingObject = null;
			}
			return;
		}

		Log.Info($"{BuildingData.OwnerGuid} building id for {this}\nthese 2 should be the same {GamePlayer.Local?.Connection?.Id} vs {Connection.Local.Id}");

		if (BuildingData.TurnsAlive < BuildingData.ProductionToBuild)
		{
			return;
		}

		if (BuildingData.OwnerGuid == Connection.Local.Id)
		{
			RevealHexesRecusrive(this, true, GameManager.Instance.ObjectPrefabs[BuildingData.ObjectId].GetComponent<ObjectBuilding>().ViewRange + 1);
		}

		if (IsRevealed)
		{
			var Clone = GameManager.Instance.ObjectPrefabs[BuildingData.ObjectId].Clone();
			Clone.WorldTransform = BuildingData.Transform;
			BuildingObject = Clone.GetComponent<ObjectBuilding>();

			SetOwner(BuildingData, true);
		}
	}

	[Sync(SyncFlags.FromHost), Change("RepUnitDataChanged")] public FUnit UnitData { get; set; } = null;
	public ObjectUnit UnitObject { get; set; } = null;
	private void RepUnitDataChanged()
	{
		if (UnitData == null)
		{
			if (UnitObject.IsValid())
			{
				UnitObject.GameObject.Destroy();
				UnitObject = null;
			}
			return;
		}

		Log.Info($"{UnitData?.OwnerGuid} unit id for {this}\nthese 2 should be the same {GamePlayer.Local?.Connection?.Id} vs {Connection.Local.Id}");

		if (UnitData.OwnerGuid == Connection.Local.Id)
		{
			RevealHexesRecusrive(this, true, GameManager.Instance.ObjectPrefabs[UnitData.ObjectId].GetComponent<ObjectUnit>().ViewRange + 1);
		}

		if (IsRevealed && !UnitObject.IsValid())
		{
			var Clone = GameManager.Instance.ObjectPrefabs[UnitData.ObjectId].Clone();
			Clone.WorldTransform = UnitData.Transform;
			UnitObject = Clone.GetComponent<ObjectUnit>();
		}
	}

	public List<Hex> AllBrothers => [HexTL, HexTR, HexMR, HexBR, HexBL, HexML];

	private bool _isHighlighted = false;
	public bool IsHighlighted
	{
		get => _isHighlighted;
		set
		{
			if (_isHighlighted == value)
			{
				return;
			}

			_isHighlighted = value;

			if (_isHighlighted)
			{
				OnHighlight();
			}
			else
			{
				OnDeHighlight();
			}
		}
	}

	private bool _isRevealed = false;
	public bool IsRevealed
	{
		get => _isRevealed;
		set
		{
			if (_isRevealed == value)
			{
				return;
			}

			_isRevealed = value;

			if (_isRevealed)
			{
				OnReveal();
			}
			else
			{
				OnHide();
			}
		}
	}

	private bool _isSelected = false;
	public bool IsSelected
	{
		get => _isSelected;
		set
		{
			if (_isSelected == value)
			{
				return;
			}

			_isSelected = value;

			if (_isSelected)
			{
				OnSelect();
			}
			else
			{
				OnDeselect();
			}
		}
	}

	private readonly List<Vector3> BrotherOffsets = [new(170, 100), new(170, -100), new(0, -200), new(-170, -100), new(-170, 100), new(0, 200)];
	private readonly Dictionary<EHexType, Color> TypeColours = new()
	{
		{ EHexType.Grass, Color.Green },
		{ EHexType.Sand, Color.Yellow },
		{ EHexType.Water, Color.Blue },
	};

	protected override void OnAwake()
	{
		base.OnAwake();

		Assert.IsValid(FogPrefab);

		if (!IsRevealed)
		{
			OnHide();
		}
	}

	protected override void OnStart()
	{
		Assert.IsValid(FogPrefab);

		base.OnStart();

		if (!Networking.IsHost)
		{
			return;
		}

		Resources = Random.Shared.Int(2, 7);
		Type = Random.Shared.FromArray(Enum.GetValues<EHexType>());
		SetBaseColour(TypeColours[Type]);
	}

	private void SetBaseColour(Color Colour)
	{
		BaseColour = Colour;
		BaseColour = BaseColour.Darken(1f / Resources);
		ModelRenderer.Tint = BaseColour.Darken(1f / Resources);
	}

	public override void OnClick()
	{
		base.OnClick();

		IsSelected = !IsSelected;
	}

	private void OnHighlight()
	{
		CloneConfig HighlightConfig = new(WorldTransform);
		LightObject = LightPrefab.Clone(HighlightConfig);
	}

	private void OnDeHighlight()
	{
		if (LightObject.IsValid())
		{
			LightObject.Destroy();
			LightObject = null;
		}
	}

	private void OnReveal()
	{
		if (FogObject.IsValid())
		{
			FogObject.Destroy();
			FogObject = null;
		}

		RepUnitDataChanged();
		OnRep_BuildingData();
	}

	private void OnHide()
	{
		CloneConfig FogConfig = new(WorldTransform);
		FogObject = FogPrefab.Clone(FogConfig);

		RepUnitDataChanged();
		OnRep_BuildingData();
	}

	private void OnSelect()
	{
		ModelRenderer.Tint = BaseColour.Darken(0.66f);
	}

	private void OnDeselect()
	{
		ModelRenderer.Tint = BaseColour;
	}

	public void ClearBrothers()
	{
		HexTL = null;
		HexTR = null;
		HexML = null;
		HexMR = null;
		HexBL = null;
		HexBR = null;
	}

	// TODO : remove below
	public void CreateSurroundBrothers()
	{
		// spawn all our brothers
		for (int BrotherIndex = 0; BrotherIndex < AllBrothers.Count; ++BrotherIndex)
		{
			var BrotherHex = AllBrothers[BrotherIndex];
			if (BrotherHex != null)
			{
				continue;
			}

			var SpawnOffset = BrotherOffsets[BrotherIndex];

			var ZOffset = Random.Shared.Next(-5, 6);
			Transform SpawnTransform = new()
			{
				Position = WorldPosition + SpawnOffset + new Vector3(0, 0, ZOffset)
			};
			CloneConfig HexBortherConfig = new(SpawnTransform);

			BrotherHex = GameObject.Clone(HexBortherConfig).GetComponent<Hex>();
			BrotherHex.GameObject.Network.SetOrphanedMode(NetworkOrphaned.Host);
			BrotherHex.GameObject.NetworkSpawn(Connection.Host);
			BrotherHex.ClearBrothers(); // TODO : better way? we'll need to whipe a lot more at some point...

			switch (BrotherIndex)
			{
				case 0:
					BrotherHex.HexBR = this;
					HexTL = BrotherHex;
					break;
				case 1:
					BrotherHex.HexBL = this;
					HexTR = BrotherHex;
					break;
				case 2:
					BrotherHex.HexML = this;
					HexMR = BrotherHex;
					break;
				case 3:
					BrotherHex.HexTL = this;
					HexBR = BrotherHex;
					break;
				case 4:
					BrotherHex.HexTR = this;
					HexBL = BrotherHex;
					break;
				case 5:
					BrotherHex.HexMR = this;
					HexML = BrotherHex;
					break;
			}
		}

		// hook the brothers up
		for (int BrotherIndex = 0; BrotherIndex < AllBrothers.Count; ++BrotherIndex)
		{
			var BrotherHex = AllBrothers[BrotherIndex];
			Assert.NotNull(BrotherHex);

			var BackIndex = BrotherIndex == 0 ? 5 : BrotherIndex - 1;
			var ForwardIndex = BrotherIndex == 5 ? 0 : BrotherIndex + 1;

			switch (BrotherIndex)
			{
				case 0:
					HexTL.HexBL = AllBrothers[BackIndex];
					HexTL.HexMR = AllBrothers[ForwardIndex];
					break;
				case 1:
					HexTR.HexML = AllBrothers[BackIndex];
					HexTR.HexBR = AllBrothers[ForwardIndex];
					break;
				case 2:
					HexMR.HexTL = AllBrothers[BackIndex];
					HexMR.HexBL = AllBrothers[ForwardIndex];
					break;
				case 3:
					HexBR.HexTR = AllBrothers[BackIndex];
					HexBR.HexML = AllBrothers[ForwardIndex];
					break;
				case 4:
					HexBL.HexMR = AllBrothers[BackIndex];
					HexBL.HexTL = AllBrothers[ForwardIndex];
					break;
				case 5:
					HexML.HexBR = AllBrothers[BackIndex];
					HexML.HexTR = AllBrothers[ForwardIndex];
					break;
			}
		}
	}

	public static void CreateSurroundBrothersRecursive(Hex Hex, int Depth)
	{
		if (Hex == null || Depth <= 0) return;

		Hex.CreateSurroundBrothers();

		foreach (var Brother in Hex.AllBrothers)
		{
			if (Brother == null) continue;
			CreateSurroundBrothersRecursive(Brother.GetComponent<Hex>(), Depth - 1);
		}
	}
	// ^^^^^^^^^^^^^^^^^^
}
