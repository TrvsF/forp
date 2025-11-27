using Forp.Object;
using Forp.Object.Building;
using Forp.Object.Unit;
using Sandbox.Diagnostics;
using System;

namespace Forp.Game;

public sealed partial class GamePlayer : Component
{
	public static GamePlayer Local { get; private set; } = null;

	[Sync(SyncFlags.FromHost), Property] public ulong SteamId { get; private set; }
	[Sync(SyncFlags.FromHost), Property] public string SteamName { get; private set; }

	[Sync, Property] public int Gold { get; set; } = 0;
	[Sync, Property] public Color Colour { get; set; } = Color.Black;

	public Connection Connection { get; private set; }
	public bool IsConnected => Connection != null && Connection.IsActive;

	private CameraComponent Camera { get => GameObject.GetComponentInChildren<CameraComponent>(); }

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

	private void SelectUnit()
	{
		var UnitHex = GameManager.Instance.HACK_GetHexFromUnit(SelectedUnit);
		var MoveRange = UnitHex.UnitData.MoveRange - UnitHex.UnitData.TurnMovementSpent + 1;
		Hex.HighlightHexesRecusrive(UnitHex, true, MoveRange);
	}

	private void DeselectUnit()
	{
		var UnitHex = GameManager.Instance.HACK_GetHexFromUnit(SelectedUnit);
		Hex.HighlightHexesRecusrive(UnitHex, false, SelectedUnit.MoveRange);
	}

	protected override void OnStart()
	{
		base.OnStart();

		if (Networking.IsHost)
		{
			Gold = 100;
			Colour = Color.Magenta;
		}

		// TODO : should we make the camera seperate
		// so we don't have to destroy it 4 clients?
		if (IsProxy)
		{
			Camera.Destroy();
		}
		else
		{
			Mouse.Visibility = MouseVisibility.Visible;
		}
	}

	protected override void OnUpdate()
	{
		if (IsProxy)
		{
			return;
		}

		DoMovement();
		DoAction();
	}

	public bool Initilize_ServerOnly(Connection ConnectionIn)
	{
		Assert.True(Networking.IsHost);
		Assert.NotNull(ConnectionIn);

		Connection = ConnectionIn;
		SteamId = Connection.SteamId;
		SteamName = Connection.DisplayName;

		using (Rpc.FilterInclude(Connection))
		{
			Initilize_Client();
		}

		return true;
	}

	[Rpc.Broadcast]
	public void Initilize_Client()
	{
		Connection = Connection.Local; // TODO : is Connection.Local ok?
		Local = this;
	}

	const float MovementExp = 2f;

	private void DoMovement()
	{
		var MouseY = Input.MouseWheel.y;
		Camera.FieldOfView -= (MouseY * 2f);

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
	}

	private void DoAction()
	{
		if (Input.Pressed("mouse1"))
		{
			Mouse.CursorType = "crosshair";

			List<Object.Object> ClickedObjects = new();
			var ClickRay = Camera.ScreenPixelToRay(Mouse.Position);
			var ClickTraces = Scene.Trace.Ray(ClickRay.Position, ClickRay.Position + ClickRay.Forward * 10000f).RunAll();

			foreach (var ClickTrace in ClickTraces)
			{
				if (!ClickTrace.Hit)
				{
					continue;
				}

				if (ClickTrace.GameObject.GetComponent<Object.Object>() is { } HitObject)
				{
					ClickedObjects.Add(HitObject);
				}
			}

			///////////////////////////////
			// TODO : sort these & use virt

			if (IsBuildMenuActive)
			{
				foreach (var ClickedObject in ClickedObjects)
				{
					if (ClickedObject is TextBuilding TextBuilding)
					{
						var HexToBuildOn = TextBuilding.BelongingHex;
						Assert.IsValid(HexToBuildOn);
						Assert.True(HexToBuildOn.BuildingObject == null);

						Transform BuildingTransform = new()
						{
							Position = HexToBuildOn.WorldPosition + new Vector3(0, 0, 40)
						};

						var BuildingToBuild = TextBuilding.ObjectToBuild;
						BuildingToBuild.WorldTransform = BuildingTransform;

						GameManager.Instance.Server_CreateHexBuildingObject(BuildingToBuild, HexToBuildOn, true, Connection.Id);
						break;
					}
				}

				ToggleBuildMenu(null); // we know menu is active so ok to pass null here
				return;
			}

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

					SelectedUnit = ObjectUnit;
					return;
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

			if (SelectedUnit.IsValid())
			{
				Log.Info($"requesting moving {SelectedUnit} to {FoundHex}");
				var UnitHex = GameManager.Instance.HACK_GetHexFromUnit(SelectedUnit);
				Hex.HighlightHexesRecusrive(UnitHex, false, SelectedUnit.MoveRange + 1);
				GameManager.Instance.Server_MoveUnitToHex(UnitHex, FoundHex, Local.Connection.Id);
				SelectedUnit = null;
			}

			SelectedHex = FoundHex;
			SelectedHex.OnClick();
		}
		if (Input.Pressed("mouse3") || Input.Pressed("spacebar"))
		{
			ToggleBuildMenu(GetHexFromMousePos(Mouse.Position));
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

	private bool IsBuildMenuActive = false;
	private readonly List<GameObject> ActiveMenuObjects = new();

	private void ToggleBuildMenu(Hex Hex)
	{
		if (IsBuildMenuActive)
		{
			foreach (var BuildingObject in ActiveMenuObjects)
			{
				BuildingObject.Destroy();
			}
			ActiveMenuObjects.Clear();
			IsBuildMenuActive = false; // !

			return;
		}

		if (!Hex.IsValid())
		{
			Log.Info($"cannot build on nothing!");
			return;
		}

		DeselectHex();

		if (Hex.BuildingObject.IsValid())
		{
			// TODO : DESTROY MENU!
			Log.Info("cannot build on a building");
			return;
		}

		List<GameObject> FoundBuildingObjects = new();

		if (Hex.BuildingOwner != null)
		{
			FoundBuildingObjects.AddRange(Hex.BuildingOwner.Hex.BuildingObject.Buildings);
		}
		if (Hex.UnitObject.IsValid())
		{
			FoundBuildingObjects.AddRange(Hex.UnitObject.Buildings);
		}

		List<GameObject> ValidBuildingObjects = new();

		foreach (var BuildingObject in FoundBuildingObjects)
		{
			if (BuildingObject.GetComponent<TextBuilding>() is { } Building)
			{
				if (!Building.CanBeBuilt(Hex))
				{
					continue;
				}

				ValidBuildingObjects.Add(BuildingObject);
				Building.FromUnit = true; // TODO : remove
			}
			else
			{
				Log.Warning($"found invalid building on hex {Hex}!");
			}
		}


		if (ValidBuildingObjects.Count == 0)
		{
			Log.Info($"no valid buildings to build here!");
			return;
		}

		IsBuildMenuActive = true; // !

		const int Padding = 30;
		var OffsetX = Padding * (ValidBuildingObjects.Count - 1);
		var OffsetY = ValidBuildingObjects.Count * Padding;

		for (int BuildingIndex = 0; BuildingIndex < ValidBuildingObjects.Count; ++BuildingIndex)
		{
			Vector3 SpawnOffset = new(-5, -OffsetX + (Padding * BuildingIndex), 250);

			Transform BuildingTransform = new()
			{
				Position = Hex.GameObject.WorldPosition + SpawnOffset
			};

			if (!ValidBuildingObjects[BuildingIndex].IsValid())
			{
				Log.Warning("invalid building in ValidBuildingObjects!");
				continue;
			}

			var BuildingComponent = SpawnObject<TextBuilding>(ValidBuildingObjects[BuildingIndex], BuildingTransform, Connection);

			BuildingComponent.BelongingHex = Hex;
			ActiveMenuObjects.Add(BuildingComponent.GameObject);
		}
	}

	//////////////////////////////////////////////////////////////////////////////

	public static T SpawnObject<T>(GameObject SpawnObject, Transform SpawnTransform, Connection Connection, bool NetworkSpawn = false) where T : new()
	{
		Assert.NotNull(Connection, $"SPAWN CONNECTION NULL");
		Assert.NotNull(SpawnObject, $"SPAWN OBJECT NULL");

		CloneConfig SpawningConfig = new(SpawnTransform);
		var SpawnedObject = SpawnObject.Clone(SpawningConfig);
		var SpawnedComponent = SpawnedObject.GetComponent<T>();
		Assert.NotNull(SpawnedComponent, $"FAILED TO SPAWN OBJECT {SpawnObject} FOR CONNECTION {Connection}");

		if (NetworkSpawn)
		{
			SpawnedObject.Network.SetOrphanedMode(NetworkOrphaned.Destroy);
			SpawnedObject.NetworkSpawn(Connection);
			SpawnedObject.NetworkSpawn();
		}

		return SpawnedComponent;
	}
}
