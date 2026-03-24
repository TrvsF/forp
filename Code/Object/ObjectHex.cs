using Forp.Object.Unit;
using Sandbox;
using Sandbox.Diagnostics;
using System;
using Sandbox.UI;
using Forp.Game;
using Forp.Object.Building;
using System.Transactions;

namespace Forp.Object;

public enum EHexType
{
	Grass,
	Sand,
	Stone,
	Water,
}

public record FQueueObject
{
	public Guid GameObjectId { get; init; }

	public string ObjectId { get; init; }
	public string ObjectName { get; init; }
	public int ProductionToBuild { get; init; }
	public int GoldToBuild { get; init; }

	public int Production { get; set; }
	public bool IsReadyToBuild()
	{
		return Production >= ProductionToBuild;
	}

	public bool CanBeBuilt()
	{
		return GamePlayer.Local.Gold >= GoldToBuild;
	}

	public string GetBuildString()
	{
		return $"{ObjectName} {Production} / {ProductionToBuild}";
	}
}

public sealed class Hex : Obj
{
	[Property] Material HighlightMaterial { get; set; }
	[Property] GameObject FogPrefab { get; set; }
	GameObject FogObject = null;

	[Sync(SyncFlags.FromHost)] public NetList<FQueueObject> QueuedUnits { get; set; } = new();
	[Sync(SyncFlags.FromHost), Property] public int Production { get; set; } = 0;
	[Sync(SyncFlags.FromHost), Property] public EHexType Type { get; set; } = EHexType.Grass;
	[Sync(SyncFlags.FromHost), Change, Property] public Color BaseColour { get; set; } = Color.White;

	[Property] public Dictionary<EHexType, Model> TypeModels { get; private set; }

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
				ConstructionClone.NetworkMode = NetworkMode.Never;
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
			CloneConfig BuildingCloneConfig = new()
			{
				StartEnabled = true,
				Parent = GameObject,
			};

			var Clone = GameManager.Instance.GetObject(BuildingData.ObjectId).Clone(BuildingCloneConfig);
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
			CloneConfig UnitCloneConfig = new()
			{
				StartEnabled = true,
				Parent = GameObject,
			};

			var Clone = GameManager.Instance.GetObject(UnitData.ObjectId).Clone(UnitCloneConfig);
			UnitObject = Clone.GetComponent<ObjectUnit>();
			UnitObject.OwnerHex = this;
			UnitObject.WorldPosition += Vector3.Up * 20f;
			UnitObject.WorldRotation = Rotation.FromYaw(180);

			if (GameManager.Instance.Mode != EGameManagerMode.Menu)
			{
				//UnitObject.ModelRenderer.RenderOptions.Overlay = true;
			}

			if (!UnitData.IsAi && GameManager.Instance.GetGamePlayer(UnitData.OwnerGuid) is { } OwnerPlayer)
			{
				UnitObject.OwnerPlayer = OwnerPlayer;
				UnitObject.ModelRenderer?.Tint = OwnerPlayer.Colour;
			}
			else
			{
				UnitObject.AddComponent<AiUnit>();
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

			if (UnitData.Upgrade != null)
			{
				UnitObject.ApplyUpgrade(UnitData.Upgrade);
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
			CloneConfig ObjCloneConfig = new()
			{
				StartEnabled = true,
				Parent = GameObject,
			};

			var Clone = GameManager.Instance.GetObject(ObjectData.ObjectId).Clone(ObjCloneConfig);
			Clone.WorldPosition += Vector3.Up * 30f; // HACK
		}
	}

	[Sync(SyncFlags.FromHost)] public NetList<FBuilding> BuildingOwners { get; set; } = new();
	public void SetOwner_ServerOnly(FBuilding OwnerIn)
	{
		Assert.True(Networking.IsHost);

		BuildingOwners.Add(OwnerIn);

		if (GameManager.Instance.GetGamePlayer(OwnerIn.OwnerGuid) is { } OwnerPlayer)
		{
			SetBaseColour_ServerOnly(OwnerPlayer.Colour);
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
		//BaseColour = BaseColour.Darken(1f / Production);
	}

	public static readonly List<Vector3> BrotherOffsets = [new(170, 100), new(170, -100), new(0, -200), new(-170, -100), new(-170, 100), new(0, 200)];

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

		ModelRenderer.Model = TypeModels[Type];
	}

	protected override void OnDestroy()
	{
		LocalCameraMode = ECameraMode.Normal;

		base.OnDestroy();
	}

	public void InitHex_ServerOnly()
	{
		Assert.True(Networking.IsHost);

		Type = Random.Shared.FromArray(Enum.GetValues<EHexType>());

		switch (Type)
		{
			case EHexType.Grass:
				Production = Random.Shared.Int(5, 9);
				break;
			case EHexType.Sand:
				Production = Random.Shared.Int(2, 3);
				break;
			case EHexType.Stone:
				Production = Random.Shared.Int(6, 10);
				break;
			case EHexType.Water:
				Production = Random.Shared.Int(3, 5);
				break;
		}

		if (Random.Shared.Next(7) == 1 && Type == EHexType.Grass)
		{
			GameManager.Instance.Server_CreateHexObject("tree", this, Connection.Host.Id);
			GameManager.Instance.Server_CreateHexUnitObject("unit-combat", this, Connection.Host.Id, true);
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
				FoundText.SetText($"{QueuedObject.GetBuildString()}");

				if (QueuedObject.IsReadyToBuild())
				{
					GuidsToRemove.Add(QueuedObject.GameObjectId);
				}
				continue;
			}

			var TextTransform = BuildingData.Transform;
			TextTransform.Position += Vector3.Up * TextYPadding;

			var TextObject = GameText.CreateTextObject<GameText>(TextTransform, $"{QueuedObject.GetBuildString()}");
			QueuedObjectTexts.Add(QueuedObject.GameObjectId, TextObject);
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

	private readonly List<GameText> ProductionTexts = new();

	private void ProductionShown(bool Shown)
	{
		foreach (var ProductionText in ProductionTexts)
		{
			ProductionText.DestroyGameObject();
		}
		ProductionTexts.Clear();

		if (Shown && IsRevealed)
		{
			var TextTransform = WorldTransform;
			TextTransform.Position += Vector3.Up * 25;

			var ProductionText = GameText.CreateTextObject<GameText>(TextTransform, $"{Production}⬡");
			ProductionText.GetComponent<TextRenderer>().Color = Color.Lerp(Color.FromBytes(255, 120, 120), Color.White, Production / 10f);
			ProductionTexts.Add(ProductionText);
		}
	}

	private void OnHighlight()
	{
		if (!CanWalkOn())
		{
			return;
		}

		ModelRenderer.MaterialOverride = HighlightMaterial;
	}

	private void OnDeHighlight()
	{
		ModelRenderer.MaterialOverride = null;
	}

	private void OnReveal()
	{
		if (FogObject.IsValid())
		{
			FogObject.Destroy();
			FogObject = null;
		}

		// TODO : revisit
		OnUnitDataChanged(new(), new());
		OnBuildingDataChanged(new(), new());
		OnObjectDataChanged(new(), new());

		// TODO : REALLY revisit
		if (GamePlayer.Local != null)
		{
			LocalCameraMode = GamePlayer.Local.CameraMode;
		}
	}

	private void OnHide()
	{
		CloneConfig FogConfig = new(WorldTransform);
		FogObject = FogPrefab.Clone(FogConfig);

		// TODO : revisit
		OnUnitDataChanged(new(), new());
		OnBuildingDataChanged(new(), new());
		OnObjectDataChanged(new(), new());
	}

	private void OnSelect()
	{
		// ModelRenderer.Tint = BaseColour.Darken(0.66f);
	}

	private void OnDeselect()
	{
		// ModelRenderer.Tint = BaseColour;
	}

	/////////////////////////////////////////////////////////////////////////////////////////////////

	[Sync(SyncFlags.FromHost)] public NetList<Hex> AllBrothers { get; set; } = [null, null, null, null, null, null];

	private ECameraMode _LocalCameraMode = ECameraMode.Normal;
	public ECameraMode LocalCameraMode
	{
		get => _LocalCameraMode;
		set
		{
			_LocalCameraMode = value;

			if (UnitObject.IsValid())
			{
				UnitObject.SetCameraMode(_LocalCameraMode);
			}

			ProductionShown(_LocalCameraMode == ECameraMode.Build);
		}
	}

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

	public static void SetOwnerHexesRecursive_ServerOnly(Hex Hex, FBuilding OwnerBuilding, int Depth)
	{
		Assert.True(Networking.IsHost);
		if (Hex == null || Depth <= 0) return;

		Hex.SetOwner_ServerOnly(OwnerBuilding);

		foreach (var Brother in Hex.AllBrothers)
		{
			if (Brother == null) continue;
			SetOwnerHexesRecursive_ServerOnly(Brother.GetComponent<Hex>(), OwnerBuilding, Depth - 1);
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

	public bool CanWalkOn()
	{
		return Type != EHexType.Water;
	}

	public Guid GetOwnerId()
	{
		if (BuildingOwners.Count == 0)
		{
			return Guid.Empty;
		}

		return BuildingOwners[0].OwnerGuid;
	}
}
