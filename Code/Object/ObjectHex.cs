using Forp.Object.Unit;
using Sandbox;
using Sandbox.Diagnostics;
using System;
using Sandbox.UI;
using Forp.Game;
using Forp.Object.Building;

namespace Forp.Object;

public enum EHexType
{
	Grass,
	Sand,
	Water,
}

public record FQueueObject
{
	public Guid GameObjectId { get; init; }

	public string ObjectId { get; init; }
	public string ObjectName { get; init; }
	public int ProductionToBuild { get; init; }

	public int Production { get; set; }
	public bool IsReadyToBuild()
	{
		return Production >= ProductionToBuild;
	}
}

public sealed class Hex : Obj
{
	[Property] GameObject FogPrefab { get; set; }
	GameObject FogObject = null;
	[Property] GameObject LightPrefab { get; set; }
	GameObject LightObject = null;

	[Property] public Hex HexTL { get; private set; }
	[Property] public Hex HexTR { get; private set; }
	[Property] public Hex HexML { get; private set; }
	[Property] public Hex HexMR { get; private set; }
	[Property] public Hex HexBL { get; private set; }
	[Property] public Hex HexBR { get; private set; }

	[Sync(SyncFlags.FromHost)] public NetList<FQueueObject> QueuedUnits { get; set; } = new();
	[Sync(SyncFlags.FromHost), Property] public int Production { get; set; } = 0;
	[Sync(SyncFlags.FromHost), Property] public EHexType Type { get; set; } = EHexType.Grass;
	[Sync(SyncFlags.FromHost), Change, Property] public Color BaseColour { get; set; } = Color.Black;

	/////////////////////////////////////////////////////////////////////////////////////////////////

	[Sync(SyncFlags.FromHost), Change] public FBuilding BuildingData { get; set; } = null;
	public TextRenderer ConstructionObject { get; set; } = null;
	public ObjectBuilding BuildingObject { get; set; } = null;
	private void OnBuildingDataChanged(FBuilding OldBuildingData, FBuilding NewBuildingData)
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

		if (BuildingData.TurnsAlive < BuildingData.ProductionToBuild)
		{
			if (!ConstructionObject.IsValid())
			{
				var ConstructionClone = GameManager.Instance.GameTextPrefab.Clone();
				ConstructionClone.WorldTransform = BuildingData.Transform;
				ConstructionClone.WorldPosition += Vector3.Up * 100;
				ConstructionObject = ConstructionClone.GetComponent<TextRenderer>();
			}

			ConstructionObject.Text = $"{BuildingData.TurnsAlive} / {BuildingData.ProductionToBuild}";
			return;
		}

		if (ConstructionObject.IsValid())
		{
			ConstructionObject.GameObject.Destroy();
		}

		if (BuildingData.OwnerGuid == Connection.Local.Id)
		{
			RevealHexesRecusrive(this, true, BuildingData.ViewRange + 1);
		}

		// we only build once
		if (IsRevealed && !BuildingObject.IsValid())
		{
			var Clone = GameManager.Instance.GetObject(BuildingData.ObjectId).Clone();
			Clone.WorldTransform = BuildingData.Transform;
			BuildingObject = Clone.GetComponent<ObjectBuilding>();
		}
	}

	[Sync(SyncFlags.FromHost), Change] public FUnit UnitData { get; set; } = null;
	public ObjectUnit UnitObject { get; set; } = null;
	private void OnUnitDataChanged(FUnit OldUnitData, FUnit NewUnitData)
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

		if (UnitData.OwnerGuid == Connection.Local.Id)
		{
			RevealHexesRecusrive(this, true, UnitData.ViewRange + 1);
		}

		if (IsRevealed && !UnitObject.IsValid())
		{
			var Clone = GameManager.Instance.GetObject(UnitData.ObjectId).Clone();
			Clone.WorldTransform = UnitData.Transform;
			UnitObject = Clone.GetComponent<ObjectUnit>();
			UnitObject.OwnerHex = this;
			UnitObject.OwnerPlayer = GamePlayer.Local;

			if (GameManager.Instance.GetGamePlayer(UnitData.OwnerGuid) is { } OwnerPlayer)
			{
				UnitObject.ModelRenderer.Tint = OwnerPlayer.Colour;
			}
		}

		if (UnitObject.IsValid())
		{
			UnitObject.Health = UnitData.Health;
			UnitObject.Attack = UnitData.Attack;
			if (UnitData.Health <= 0)
			{
				Log.Info($"you! yes you are DEAD");
				UnitObject.DestroyGameObject(); // TODO : server needs to destroy data 2
				return;
			}
		}
	}

	[Sync(SyncFlags.FromHost), Change] public FObj ObjectData { get; set; } = null;
	public Obj Obj { get; set; } = null;
	private void OnObjectDataChanged(FObj OldObjectData, FObj NewObjectData)
	{
		if (ObjectData == null)
		{
			if (Obj.IsValid())
			{
				Obj.GameObject.Destroy();
				Obj = null;
			}
			return;
		}

		if (IsRevealed && !Obj.IsValid())
		{
			var Clone = GameManager.Instance.GetObject(ObjectData.ObjectId).Clone();
			Clone.WorldTransform = ObjectData.Transform;
			Obj = Clone.GetComponent<ObjectUnit>();

			if (GameManager.Instance.GetGamePlayer(ObjectData.OwnerGuid) is { } OwnerPlayer)
			{
				Obj?.ModelRenderer.Tint = OwnerPlayer.Colour;
			}
		}
	}

	[Sync(SyncFlags.FromHost)] public NetList<FBuilding> BuildingOwners { get; set; } = new();
	public void SetOwner_ServerOnly(FBuilding OwnerIn, bool DoBrothers = false)
	{
		Assert.True(Networking.IsHost);

		BuildingOwners.Add(OwnerIn);

		if (GameManager.Instance.GetGamePlayer(OwnerIn.OwnerGuid) is { } OwnerPlayer)
		{
			SetBaseColour_ServerOnly(OwnerPlayer.Colour);
		}

		if (!DoBrothers)
		{
			return;
		}

		foreach (var BrothermanHex in AllBrothers)
		{
			BrothermanHex?.SetOwner_ServerOnly(OwnerIn, false);
		}
	}

	public void AddQueuedObject_ServerOnly(FQueueObject QueueObject)
	{
		Assert.True(Networking.IsHost);
		QueuedUnits.Add(QueueObject);
	}

	private void OnBaseColourChanged(Color OldColour, Color NewColour)
	{
		ModelRenderer.Tint = BaseColour.Darken(1f / Production);
	}

	private void SetBaseColour_ServerOnly(Color Colour)
	{
		BaseColour = Colour;
		BaseColour = BaseColour.Darken(1f / Production);
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

		QueuedUnits.OnChanged += OnQueuedObjectsChanged; // TODO : unbind?

		if (Networking.IsHost)
		{
			InitHex_ServerOnly();
		}
	}

	public void InitHex_ServerOnly()
	{
		Assert.True(Networking.IsHost);
		Production = Random.Shared.Int(2, 7);
		Type = Random.Shared.FromArray(Enum.GetValues<EHexType>());
		SetBaseColour_ServerOnly(TypeColours[Type]);
		if (Random.Shared.Next(7) == 1 && Type == EHexType.Grass)
		{
			GameManager.Instance.Server_CreateHexObject("tree", this, Connection.Host.Id);
		}
	}

	private void OnQueuedObjectsChanged(NetListChangeEvent<FQueueObject> QueueObjectChanged)
	{
		if (IsLocallyOwned())
		{
			DrawUnitQueue();
		}
	}

	public override void OnClick()
	{
		base.OnClick();

		IsSelected = !IsSelected;
	}

	public Transform GetObjectSpawnLocation()
	{
		var BaseTransform = WorldTransform;
		BaseTransform.Position += Vector3.Up * 40; // 40 = hex width
		return BaseTransform;
	}

	private readonly Dictionary<Guid, GameText> QueuedObjectTexts = new();

	public void DrawUnitQueue()
	{
		List<Guid> GuidsToRemove = new();

		int TextYPadding = 100;
		foreach (var QueuedObject in QueuedUnits)
		{
			TextYPadding += 50;

			if (QueuedObjectTexts.TryGetValue(QueuedObject.GameObjectId, out var FoundText))
			{
				FoundText.GetComponent<TextRenderer>().Text = $"{QueuedObject.ObjectName} {QueuedObject.Production} / {QueuedObject.ProductionToBuild}";

				if (QueuedObject.IsReadyToBuild())
				{
					GuidsToRemove.Add(QueuedObject.GameObjectId);
				}
				continue;
			}

			var ConstructionClone = GameManager.Instance.GameTextPrefab.Clone();
			ConstructionClone.WorldTransform = BuildingData.Transform;
			ConstructionClone.WorldPosition += Vector3.Up * TextYPadding;

			var ConstructionObject = ConstructionClone.GetComponent<TextRenderer>();
			ConstructionObject.Text = $"{QueuedObject.ObjectName} {QueuedObject.Production} / {QueuedObject.ProductionToBuild}";
			QueuedObjectTexts.Add(QueuedObject.GameObjectId, ConstructionClone.GetComponent<GameText>());
		}

		foreach (var Guid in GuidsToRemove)
		{
			RemoveQueuedObjectText(Guid);
		}
	}

	public void RemoveQueuedObjectText(Guid GameObjectId)
	{
		if (QueuedObjectTexts.TryGetValue(GameObjectId, out var FoundText))
		{
			FoundText.DestroyGameObject();
			QueuedObjectTexts.Remove(GameObjectId);
		}
	}

	public void OnNextTurn()
	{
		
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

		// TODO : revisist
		OnUnitDataChanged(new(), new());
		OnBuildingDataChanged(new(), new());
		OnObjectDataChanged(new(), new());
	}

	private void OnHide()
	{
		CloneConfig FogConfig = new(WorldTransform);
		FogObject = FogPrefab.Clone(FogConfig);

		// TODO : revisist
		OnUnitDataChanged(new(), new());
		OnBuildingDataChanged(new(), new());
		OnObjectDataChanged(new(), new());
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

	/////////////////////////////////////////////////////////////////////////////////////////////////

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

	public bool HasOwner()
	{
		return BuildingOwners.Count > 0;
	}

	public bool IsLocallyOwned()
	{
		return GetOwnerId() == GamePlayer.Local.ConnectionId;
	}

	public Guid GetOwnerId()
	{
		if (BuildingOwners.Count == 0)
		{
			return Guid.Empty;
		}

		return BuildingOwners[0].OwnerGuid;
	}

	/////////////////////////////////////////////////////////////////////////////////////////////////

	// TODO : revisit
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
}
