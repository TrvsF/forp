using Forp.Object;
using Forp.Object.Building;
using Forp.Object.Unit;
using Forp.Util;
using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.Network;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Forp.Game;

public sealed class GameManager : SingletonComponent<GameManager>, Component.INetworkListener
{
	[Property] public GameObject HexObject { get; set; }
	[Property] public GameObject PlayerPrefab { get; set; }
	[Property] public Dictionary<string, GameObject> ObjectPrefabs { get; set; }

	[Sync(SyncFlags.FromHost)] public NetList<GamePlayer> GamePlayers { get; private set; } = new();
	[Sync(SyncFlags.FromHost)] public int Turn { get; set; } = 0;

	/////////////////////////////////////////////////////////////////////////////////////////

	private readonly List<Guid> WaitingConnections = new();

	public void NextTurn()
	{
		Server_OnNextTurnRequest(Connection.Local.Id);
	}

	[Rpc.Host]
	private void Server_OnNextTurnRequest(Guid ConnectionId)
	{
		if (WaitingConnections.Contains(ConnectionId))
		{
			Log.Warning($"trying to add id {ConnectionId} again!");
			return;
		}

		WaitingConnections.Add(ConnectionId);

		if (GamePlayers.Count == WaitingConnections.Count)
		{
			foreach (var Hex in Scene.GetComponentsInChildren<Hex>())
			{
				if (!Hex.IsValid())
				{
					continue;
				}

				// we make a copy & reasign with these 2 call change 

				if (Hex.UnitData != null)
				{
					FUnit UnitData = Hex.UnitData;
					UnitData.TurnsAlive += 1;
					UnitData.TurnMovementSpent = 0;

					Hex.UnitData = UnitData;
				}

				if (Hex.BuildingData != null)
				{
					// TODO : why isnt this the same as unit?
					FBuilding BuildingData = new();
					BuildingData.SetData(Hex.BuildingData);
					BuildingData.TurnsAlive += 1;

					Hex.BuildingData = BuildingData;
				}
			}

			WaitingConnections.Clear();
			++Turn;
		}
	}

	/////////////////////////////////////////////////////////////////////////////////////////

	void INetworkListener.OnActive(Connection ConnectionChannel)
	{
		Log.Info($"Connection activating with name = {ConnectionChannel.DisplayName}:{ConnectionChannel.Ping} | is host = {ConnectionChannel.IsHost}");

		StartClient_SeverOnly(ConnectionChannel);
	}

	void INetworkListener.OnDisconnected(Connection ConnectionChannel)
	{
		// Assert.True(Networking.IsHost); // after changing hosts this assert fails :^)

		Log.Info($"Connection {ConnectionChannel} disconnecting");

		GamePlayer PlayerToDestroy = null;
		foreach (var PlayerState in GamePlayers)
		{
			if (PlayerState.Connection == ConnectionChannel)
			{
				PlayerToDestroy = PlayerState;
			}
		}

		if (PlayerToDestroy != null)
		{
			GamePlayers.Remove(PlayerToDestroy);

			// TODO : destroy units & such

			PlayerToDestroy.GameObject.Root.Destroy();
		}
	}

	protected override async Task OnLoad()
	{
		if (Scene.IsEditor)
		{
			return;
		}

		if (!Networking.IsActive)
		{
			CreateLobby();
		}

		Assert.IsValid(HexObject);
		Assert.IsValid(PlayerPrefab);

		// create board
		if (Networking.IsHost)
		{
			Transform HexTransform = new();
			CloneConfig HexConfig = new(HexTransform);
			var Hex = HexObject.Clone(HexConfig);
			Hex.Network.SetOrphanedMode(NetworkOrphaned.Host);
			Hex.NetworkSpawn(Connection.Host);

			_ = GenerateBoardAsync(Hex);
		}
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();

		// HACK clients spawn with an invalid unit object, remove this
		// TODO : figure out where the unit comes from
		foreach (var UnitObject in Scene.GetAll<ObjectUnit>())
		{
			if (HACK_GetHexFromUnit(UnitObject) == null)
			{
				Log.Warning($"FOUND AN INVALID UNIT {UnitObject}, DESTROYING");
				UnitObject.Destroy();
			}
		}
	}

	private async Task GenerateBoardAsync(GameObject Hex)
	{
		// TODO : loading screen
		var MainHex = Hex.GetComponent<Hex>();
		MainHex.CreateSurroundBrothers();

		await CreateSurroundAsync(MainHex, 7);
	}

	private async Task CreateSurroundAsync(Hex Hex, int Depth)
	{
		if (Depth <= 0 || !Hex.IsValid())
			return;

		Hex.CreateSurroundBrothers();

		foreach (var Brother in Hex.AllBrothers)
		{
			await CreateSurroundAsync(Brother, Depth - 1);
		}
	}

	private void StartClient_SeverOnly(Connection ConnectionChannel)
	{
		Assert.True(Networking.IsHost);

		bool CreatedPlayerState = CreatePlayerObject_ServerOnly(ConnectionChannel, out var GamePlayer);

		if (!CreatedPlayerState || GamePlayer == null)
		{
			Networking.Disconnect();
			throw new Exception($"Something went wrong when trying to create PlayerState for {ConnectionChannel.DisplayName}");
		}

		GamePlayers.Add(GamePlayer);

		var SpawnHex = Random.Shared.FromList(Scene.GetAllComponents<Hex>().ToList());
		Instance.Server_CreateHexUnitObject("unit-settler", SpawnHex, ConnectionChannel.Id);
	}

	[Rpc.Host]
	public void Server_UnitAttack(FUnit AttackerUnit, FUnit DefenderUnit)
	{
		Assert.NotNull(AttackerUnit);
		Assert.NotNull(DefenderUnit);

		if (!AttackerUnit.Hex.IsValid() || !DefenderUnit.Hex.IsValid())
		{
			Log.Warning($"{AttackerUnit} or {DefenderUnit} does not have a valid hex when calling attack");
			return;
		}

		Log.Info($"updating data...");
		DefenderUnit.Hex.UnitData = DefenderUnit.Hex.UnitData with
		{
			Health = DefenderUnit.Hex.UnitData.Health - AttackerUnit.Attack
		};
	}

	[Rpc.Host]
	public void Server_CreateHexUnitObject(string ObjectId, Hex Hex, Guid ConnectionId)
	{
		Assert.IsValid(Hex);
		Assert.NotNull(ConnectionId);

		var HexObject = ObjectPrefabs[ObjectId];
		Assert.NotNull(HexObject);
		var TypedObject = HexObject.GetComponent<ObjectUnit>();
		Assert.NotNull(TypedObject);

		FUnit ObjectData = new()
		{
			ObjectId = TypedObject.ObjectId,
			Name = TypedObject.DisplayName,
			Transform = Hex.WorldTransform,
			OwnerGuid = ConnectionId,
			Health = TypedObject.Health,
			Attack = TypedObject.Attack,
			MoveRange = TypedObject.MoveRange,
			Hex = Hex,
		};

		Hex.UnitData = ObjectData;
	}

	[Rpc.Host]
	public void Server_CreateHexBuildingObject(GameObject HexObject, Hex Hex, bool FromUnit, Guid ConnectionId)
	{
		Assert.IsValid(Hex);
		Assert.NotNull(ConnectionId);

		var TypedObject = HexObject.GetComponent<ObjectBuilding>();
		Assert.NotNull(TypedObject);

		FBuilding ObjectData = new()
		{
			ObjectId = TypedObject.ObjectId,
			Name = TypedObject.DisplayName,
			Transform = Hex.WorldTransform,
			OwnerGuid = ConnectionId,
			ProductionToBuild = TypedObject.ProductionToBuild,
			Hex = Hex,
		};

		Hex.BuildingData = ObjectData;
		Hex.SetOwner_ServerOnly(ObjectData, true);

		if (FromUnit) // TODO : check building charge
		{
			Hex.UnitData = null;
		}
	}

	[Rpc.Host]
	public void Server_MoveUnitToHex(Hex OldHex, Hex NewHex, Guid ConnectionId)
	{
		Assert.IsValid(OldHex);
		Assert.IsValid(NewHex);

		var OldUnitData = OldHex.UnitData;
		if (OldUnitData == null)
		{
			Log.Warning("MoveUnitToHex called with invalid Unit");
			return;
		}

		if (OldUnitData.OwnerGuid != ConnectionId)
		{
			Log.Warning($"{ConnectionId} trying to make an object which is not theirs");
			return;
		}

		var OldHexPos = OldHex.WorldPosition.WithZ(0);
		var NewHexPos = NewHex.WorldPosition.WithZ(0);
		var HexDistance = Vector3.DistanceBetween(OldHexPos, NewHexPos);
		var HexesBetween = (int)Math.Round(HexDistance / 200f);
		Log.Info($"Hex distance = {HexesBetween} turns spent = {OldUnitData.TurnMovementSpent} moverange = {OldUnitData.MoveRange}");
		var MovementLeftover = OldUnitData.MoveRange - (HexesBetween + OldUnitData.TurnMovementSpent);
		if (MovementLeftover < 0)
		{
			return;
		}

		FUnit NewUnitData = OldUnitData with
		{
			Transform = NewHex.WorldTransform,
			OwnerGuid = ConnectionId,
			Hex = NewHex,
		};

		OldHex.UnitData = null;
		NewHex.UnitData = NewUnitData;
	}

	public GamePlayer GetGamePlayer(Guid ConnectionId)
	{
		foreach (var GamePlayer in GamePlayers)
		{
			if (GamePlayer.ConnectionId == ConnectionId)
			{
				return GamePlayer;
			}
		}
		return null;
	}

	// TODO : we need a better relationship
	public Hex HACK_GetHexFromUnit(ObjectUnit Unit)
	{
		var Hexes = Scene.GetComponentsInChildren<Hex>();
		foreach (var Hex in Hexes)
		{
			if (!Hex.IsValid())
			{
				continue;
			}

			if (Hex.UnitObject == Unit)
			{
				return Hex;
			}
		}
		Log.Info($"cannot find hex for unit {Unit}");
		return null;
	}

	private bool CreatePlayerObject_ServerOnly(Connection ConnectionChannel, out GamePlayer OutGamePlayer)
	{
		Assert.True(Networking.IsHost);
		Assert.True(PlayerPrefab.IsValid(), "Could not spawn player as no PlayerPrefab assigned to network manager");

		Transform PlayerTransform = new()
		{
			Position = new(-500, 0, 500)
		};

		CloneConfig PlayerSpawnConfig = new(PlayerTransform);

		var PlayerObject = PlayerPrefab.Clone(PlayerSpawnConfig);
		PlayerObject.Name = $"PLAYER:{ConnectionChannel.DisplayName}";
		PlayerObject.Network.SetOrphanedMode(NetworkOrphaned.Destroy);
		PlayerObject.NetworkSpawn(ConnectionChannel);
		PlayerObject.NetworkSpawn();

		OutGamePlayer = PlayerObject.GetComponent<GamePlayer>();

		if (OutGamePlayer == null)
		{
			throw new Exception($"Could not spawn player as no PlayerStatePrefab assigned to network manager for {ConnectionChannel.DisplayName}");
		}

		if (OutGamePlayer.Initilize_ServerOnly(ConnectionChannel))
		{
			return true;
		}

		OutGamePlayer.GameObject.DestroyImmediate();
		return false;
	}

	private static bool CreateLobby(string LobbyName = "awesomelobby", LobbyPrivacy Privacy = LobbyPrivacy.Public)
	{
		LobbyConfig Config = new();
		Config.Name = LobbyName;
		Config.DestroyWhenHostLeaves = false;
		Config.MaxPlayers = 8;
		Config.Privacy = Privacy;

		Networking.CreateLobby(Config);

		return true;
	}
}
