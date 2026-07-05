using Forp.Object;
using Forp.Object.Building;
using Forp.Object.Unit;
using Sandbox.Diagnostics;
using System;
using System.Linq.Expressions;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using static Sandbox.Services.Stats;

namespace Forp.Game;

public enum ECameraMode
{
	Normal,
	Build,
	Combat,
}

public record FPlayerBoardStats
{
	public int Production { get; set; }
	public int TerritoryCount { get; set; }
	public int TerritoryPercentage { get => (int)(((float)TerritoryCount / GameManager.Instance.BoardHexes.Count) * 100f); }
}

public sealed partial class GamePlayer : Component
{
	public static GamePlayer Local { get; private set; } = null;

	[Property] public GameObject PlayerCameraPrefab { get; private set; }
	public CameraComponent Camera { get; private set; }
	public GamePlayerGUi GUi { get; private set; }

	[Sync(SyncFlags.FromHost), Property] public ulong SteamId { get; private set; }
	[Sync(SyncFlags.FromHost), Property] public string SteamName { get; private set; }
	[Sync(SyncFlags.FromHost), Property] public Guid ConnectionId { get; private set; }

	[Sync(SyncFlags.FromHost), Property] public NetList<FUpgrade> Upgrades { get; private set; }
	[Sync(SyncFlags.FromHost), Property] public Color Colour { get; set; }
	[Sync(SyncFlags.FromHost), Property] public int Gold { get; set; }

	public override string ToString()
	{
		return $"GamePlayer : {SteamName} : c={IsConnected}?{(IsConnected ? Connection : string.Empty)} ai={IsAi}";
	}

	public Connection Connection { get; private set; }
	public bool IsConnected => Connection != null && Connection.IsActive;

	private ECameraMode _CameraMode = ECameraMode.Build;
	public ECameraMode CameraMode
	{
		get => _CameraMode;
		set
		{
			if (_CameraMode == value)
			{
				return;
			}

			_CameraMode = value;
			OnCameraModeChange();
		}
	}

	private Obj HoveredObject { get; set; }
	private Hex SelectedHex { get; set; } = null;

	private ObjectUnit _SelectedUnit = null;
	public ObjectUnit SelectedUnit
	{
		get => _SelectedUnit;
		set
		{
			if (value != null)
			{
				_SelectedUnit = value;
				SelectUnit();
			}
			else
			{
				DeselectUnit();
				_SelectedUnit = value;
			}
		}
	}

	private void OnCameraModeChange()
	{
		foreach (var Hexogon in GameManager.Instance.BoardHexes)
		{
			Hexogon.LocalCameraMode = CameraMode;
		}
	}

	private void SelectUnit()
	{
		if (SelectedUnit.OwnerPlayer?.ConnectionId != Local.ConnectionId)
		{
			return;
		}

		if (GameManager.Instance.HACK_GetHexFromUnit(SelectedUnit) is { } UnitHex)
		{
			if (SelectedUnit.SelectedMaterial.IsValid())
			{
				SelectedUnit.ModelRenderer.MaterialOverride = SelectedUnit.SelectedMaterial;
			}
			var MoveRange = UnitHex.UnitData.ActionPoints - UnitHex.UnitData.ActionPointsSpent + 1;
			Hex.HighlightHexesRecusrive(UnitHex, true, MoveRange);
		}
	}

	private void DeselectUnit()
	{
		if (GameManager.Instance.HACK_GetHexFromUnit(SelectedUnit) is { } UnitHex)
		{
			if (SelectedUnit.SelectedMaterial.IsValid())
			{
				SelectedUnit.ModelRenderer.ClearMaterialOverrides();
			}
			Hex.HighlightHexesRecusrive(UnitHex, false, SelectedUnit.ActionPoints + 1);
		}

		foreach (var Hex in GameManager.Instance.BoardHexes)
		{
			Hex.IsHighlighted = false;
		}
	}

	protected override void OnAwake()
	{
		base.OnAwake();

		if (!Network.IsOwner)
		{
			return; // !
		}

		Log.Info($"player awoke ai={IsAi}");

		Connection = Connection.Local; // TODO : this is only set on server & local client per player... 
										// why dont connections replicate ? [may/26]
		if (IsAi)
		{
			return; // !
		}

		Log.Warning("why");
		Local = this;
		Mouse.Visibility = MouseVisibility.Visible;
		Assert.True(CreateCamera());

		RefreshGUi();
	}

	private bool CreateCamera()
	{
		Transform CloneTransform = new();
		var Cloneconfig = new CloneConfig();
		Cloneconfig.Parent = GameObject;
		Cloneconfig.StartEnabled = true;
		Cloneconfig.Transform = CloneTransform;
		var CameraObject = PlayerCameraPrefab.Clone(Cloneconfig);

		Camera = CameraObject.GetComponentInChildren<CameraComponent>();
		if (Camera == null)
		{
			Log.Error("failed to create camera for local player!");
			return Camera != null;
		}

		GUi = CameraObject.GetComponentInChildren<GamePlayerGUi>(true);
		if (GUi == null)
		{
			Log.Error("failed to create gui for local player!");
			foreach (var ObjectChild in CameraObject.Children)
			{
				Log.Info(ObjectChild.Name);
				if (ObjectChild.GetComponent<GamePlayerGUi>() != null)
				{
					GUi = ObjectChild.GetComponent<GamePlayerGUi>();
				}
			}
			return GUi != null;
		}

		return true;
	}

	protected override void OnUpdate()
	{
		if (IsProxy || IsAi)
		{
			return;
		}

		DoMovement();
		DoAction();

		var HoverRay = Camera.ScreenPixelToRay(Mouse.Position);
		var HoverTraces = Scene.Trace.Ray(HoverRay.Position, HoverRay.Position + HoverRay.Forward * 10000f).RunAll();

		foreach (var HoverTrace in HoverTraces)
		{
			if (!HoverTrace.Hit)
			{
				continue;
			}

			if (HoverTrace.GameObject.GetComponent<Obj>() is { } HitObject)
			{
				HoveredObject = HitObject;
				break;
			}
		}

		RefreshGUi();
	}

	public bool Initilize_ServerOnly(Connection ConnectionIn, Hex SpawnHex, bool IsAiIn)
	{
		Assert.True(Networking.IsHost);
		Assert.NotNull(ConnectionIn);

		if (IsAiIn)
		{
			IsAi = IsAiIn;

			Connection = ConnectionIn;
			ConnectionId = Guid.NewGuid();
			SteamId = 0;
			SteamName = $"{Time.Now}";
		}
		else
		{
			Connection = ConnectionIn;
			ConnectionId = ConnectionIn.Id;
			SteamId = ConnectionIn.SteamId;
			SteamName = ConnectionIn.DisplayName;
		}

		Log.Info($"{this} is initing");

		GameManager.Instance.Server_CreateHexUnitObject("unit-settler", SpawnHex, ConnectionId, IsAi);
		var ValidBrothers = SpawnHex.AllBrothers.Where(Hex => Hex != null && Hex.ObjectData == null && Hex.CanWalkOn()).OrderBy(Hex => Random.Shared.Next());
		if (!ValidBrothers.Any())
		{
			Log.Warning($"urrr, couldn't spawn a brother unit... this isn't good...");
			return false;
		}

		var Brother = ValidBrothers.First();
		GameManager.Instance.Server_CreateHexUnitObject("unit-combat", Brother, ConnectionId, IsAi);

		return true;
	}

	const float MovementExp = 2f;

	private void DoMovement()
	{
		Camera.WorldPosition += Camera.WorldRotation.Forward * Input.MouseWheel.y * 50;

		if (Input.Down("forward"))
		{
			Camera.WorldPosition += Vector3.Forward * MovementExp;
		}
		if (Input.Down("backward"))
		{
			Camera.WorldPosition += Vector3.Backward * MovementExp;
		}
		if (Input.Down("left"))
		{
			Camera.WorldPosition += Vector3.Left * MovementExp;
		}
		if (Input.Down("right"))
		{
			Camera.WorldPosition += Vector3.Right * MovementExp;
		}

		if (Input.Down("mouse2"))
		{
			Camera.WorldPosition += new Vector3(Mouse.Delta.y, Mouse.Delta.x, 0);
		}

		if (Input.Down("mouse1") && DraggedObject != null)
		{
			DraggedObject.WorldPosition -= new Vector3(Mouse.Delta.y * 0.19f, Mouse.Delta.x * 0.11f, 0);
		}
		if (!Input.Down("mouse1") && DraggedObject != null)
		{
			DraggedObject = null;
		}
	}

	private GameObject DraggedObject = null;

	private void DoAction()
	{
		if (Input.Pressed("mouse1"))
		{
			Mouse.CursorType = "crosshair";

			List<Obj> ClickedObjects = new();
			var ClickRay = Camera.ScreenPixelToRay(Mouse.Position);
			var ClickTraces = Scene.Trace.Ray(ClickRay.Position, ClickRay.Position + ClickRay.Forward * 10000f).RunAll();

			foreach (var ClickTrace in ClickTraces)
			{
				if (!ClickTrace.Hit)
				{
					continue;
				}

				if (ClickTrace.GameObject.GetComponent<Obj>() is { } HitObject)
				{
					HitObject.OnClick();
					ClickedObjects.Add(HitObject);
				}

				if (ClickTrace.GameObject.GetComponent<Upgrade>() is { })
				{
					DraggedObject = ClickTrace.GameObject;
				}

				if (GUi != null && ClickTrace.GameObject == GUi.UpgradeGUi)
				{
					GUi.OnUpgradeClicked(Local);
				}
			}

			///////////////////////////////
			// TODO : sort these & use virt

			foreach (var ClickedObject in ClickedObjects)
			{
				if (ClickedObject is ObjectUnit ObjectUnit)
				{
					DeselectHex();

					if (SelectedUnit == ObjectUnit)
					{
						SelectedUnit = null;
						return;
					}

					if (SelectedUnit.IsValid())
					{
						if (GameManager.Instance.CanAttack(SelectedUnit.OwnerHex, ObjectUnit.OwnerHex, ObjectUnit.OwnerHex.UnitData, Connection.Local.Id, out var _, out var _))
						{
							GameManager.Instance.Server_UnitAttackUnit(SelectedUnit.OwnerHex, ObjectUnit.OwnerHex, Connection.Local.Id);
						}

						SelectedUnit = null;
						return;
					}

					SelectedUnit = ObjectUnit;
					return;
				}
				
				if (ClickedObject is ObjectBuilding ObjectBuilding)
				{
					if (SelectedUnit.IsValid())
					{
						if (GameManager.Instance.CanAttack(SelectedUnit.OwnerHex, ObjectBuilding.OwnerHex, ObjectBuilding.OwnerHex.BuildingData, Connection.Local.Id, out var _, out var _))
						{
							GameManager.Instance.Server_UnitAttackBuilding(SelectedUnit.OwnerHex, ObjectBuilding.OwnerHex, Connection.Local.Id);
						}

						SelectedUnit = null;
					}
				}
			}

			///////////////////////////////

			var PriorHex = SelectedHex;
			DeselectHex();

			var FoundHex = GetHexFromMousePos(Mouse.Position);
			if (!FoundHex.IsValid() || PriorHex == FoundHex)
			{
				return;
			}

			if (SelectedUnit.IsValid() && SelectedUnit.GetComponent<AiUnit>() == null)
			{
				var UnitHex = GameManager.Instance.HACK_GetHexFromUnit(SelectedUnit);
				Hex.HighlightHexesRecusrive(UnitHex, false, SelectedUnit.ActionPoints + 1);
				GameManager.Instance.Server_MoveUnitToHex(UnitHex, FoundHex, Connection.Id);
			}
			else
			{
				SelectedHex = FoundHex;
				SelectedHex.OnClick();
			}

			SelectedUnit = null;
		}

		if (Input.Pressed("camera_combat"))
		{
			CameraMode = ECameraMode.Combat;
		}

		if (Input.Pressed("camera_normal"))
		{
			CameraMode = ECameraMode.Normal;
		}

		if (Input.Pressed("camera_build"))
		{
			CameraMode = ECameraMode.Build;
		}
	}

	private void DeselectHex()
	{
		if (!SelectedHex.IsValid())
		{
			return;
		}

		SelectedHex.IsSelected = false;
		SelectedHex = null;
	}

	private Hex GetHexFromMousePos(Vector2 MousePos)
	{
		var ClickRay = Camera.ScreenPixelToRay(MousePos);
		var ClickTraces = Scene.Trace.Ray(ClickRay.Position, ClickRay.Position + ClickRay.Forward * 10000f).RunAll();

		foreach (var ClickTrace in ClickTraces)
		{
			if (!ClickTrace.Hit)
			{
				continue;
			}

			if (ClickTrace.GameObject.GetComponent<Object.Hex>() is { } HitHex)
			{
				return HitHex;
			}
		}

		return null;
	}

	//////////////////////////////////////////////////////////////////////////////

	// TODO : move me
	// & clean this shi up

	public static T SpawnObject<T>(GameObject SpawnGameObject, Transform SpawnTransform, Connection Connection, GameObject Parent = null, bool NetworkSpawn = false) where T : new()
	{
		var SpawnedGameObject = SpawnObject(SpawnGameObject, SpawnTransform, Connection, Parent, NetworkSpawn);

		var SpawnedComponent = SpawnedGameObject.GetComponent<T>();
		Assert.NotNull(SpawnedComponent, $"FAILED TO SPAWN OBJECT {SpawnObject} FOR CONNECTION {Connection}");

		return SpawnedComponent;
	}

	public static GameObject SpawnObject(GameObject SpawnObject, Transform SpawnTransform, Connection Connection, GameObject Parent = null, bool NetworkSpawn = false)
	{
		Assert.NotNull(Connection, $"SPAWN CONNECTION NULL");
		Assert.NotNull(SpawnObject, $"SPAWN OBJECT NULL");

		SpawnObject.NetworkMode = NetworkSpawn ? NetworkMode.Object : NetworkMode.Never;

		CloneConfig SpawningConfig = new(SpawnTransform);
		SpawningConfig.Parent = Parent;

		var SpawnedObject = SpawnObject.Clone(SpawningConfig);
		Assert.NotNull(SpawnedObject, $"FAILED TO SPAWN OBJECT {SpawnObject} FOR CONNECTION {Connection}");

		if (NetworkSpawn)
		{
			SpawnedObject.Network.SetOrphanedMode(NetworkOrphaned.Destroy);
			SpawnedObject.NetworkSpawn(Connection);
			SpawnedObject.NetworkSpawn();
		}

		return SpawnedObject;
	}
}
