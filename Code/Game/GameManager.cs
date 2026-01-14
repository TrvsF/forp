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

public sealed class GameManager : SingletonComponent<GameManager>, Component.INetworkListener, ISceneStartup
{
	[Property] public GameObject HexPrefab { get; set; }
	[Property] public GameObject PlayerPrefab { get; set; }
	[Property] public GameObject GameTextPrefab { get; set; }
	[Property] private Dictionary<string, GameObject> ObjectPrefabs { get; set; }

	public GameObject GetObject(string ObjectId)
	{
		ObjectPrefabs.TryGetValue(ObjectId, out GameObject Object);
		if (!Object.IsValid())
		{
			Log.Error($"cannot find object with id {ObjectId}!!!");
		}

		return Object;
	}

	/////////////////////////////////////////////////////////////////////////////////////////

	[Sync(SyncFlags.FromHost)] public NetList<Hex> BoardHexes { get; private set; } = new();
	[Sync(SyncFlags.FromHost)] public NetList<GamePlayer> GamePlayers { get; private set; } = new();
	[Sync(SyncFlags.FromHost)] public int Turn { get; set; } = 0;

	public void GetPlayerBoardStats(out Dictionary<GamePlayer, FPlayerBoardStats> OutPlayerBoardStats)
	{
		OutPlayerBoardStats = new();

		foreach (var Hex in BoardHexes)
		{
			var Player = GetGamePlayer(Hex.GetOwnerId());
			if (!Player.IsValid())
			{
				continue;
			}

			if (!OutPlayerBoardStats.TryGetValue(Player, out var PlayerStats))
			{
				PlayerStats = new();
				OutPlayerBoardStats[Player] = PlayerStats;
			}


			PlayerStats.Production += Hex.Production;
			PlayerStats.TerritoryCount += 1;
		}

		//foreach (var Player in OutPlayerTerritory.Keys)
		//{
		//	OutPlayerTerritory[Player] /= BoardHexes.Count;
		//}
	}


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
			DoNextTurn_ServerOnly();
		}
	}

	[Rpc.Broadcast]
	private void Broadcast_OnNextTurn()
	{
		foreach (var Hex in BoardHexes)
		{
			if (!Hex.IsValid())
			{
				continue;
			}

			Hex.OnNextTurn();
		}
	}

	private void DoNextTurn_ServerOnly()
	{
		Assert.True(Networking.IsHost);

		foreach (var Hex in BoardHexes)
		{
			if (!Hex.IsValid())
			{
				continue;
			}

			if (Hex.UnitData != null)
			{
				Hex.UnitData = Hex.UnitData with
				{
					TurnsAlive = Hex.UnitData.TurnsAlive + 1,
					TurnMovementSpent = 0,
				};
			}

			if (Hex.BuildingData != null)
			{
				Hex.BuildingData = Hex.BuildingData with
				{
					TurnsAlive = Hex.BuildingData.TurnsAlive + 1,
				};
			}

			for (var ObjectIndex = Hex.QueuedObjects.Count - 1; ObjectIndex >= 0; --ObjectIndex)
			{
				Hex.QueuedObjects[ObjectIndex] = Hex.QueuedObjects[ObjectIndex] with
				{
					Production = Hex.QueuedObjects[ObjectIndex].Production + Hex.Production
				};

				var QueuedObject = Hex.QueuedObjects[ObjectIndex];
				if (QueuedObject.IsReadyToBuild())
				{
					// TODO : make recursive or some shit :)
					foreach (var HexBrother in Hex.AllBrothers)
					{
						if (!HexBrother.IsValid())
						{
							continue;
						}

						if (HexBrother.UnitData == null)
						{
							Server_CreateHexUnitObject(QueuedObject.ObjectId, HexBrother, Hex.GetOwnerId());
							break;
						}
					}

					Hex.QueuedObjects.RemoveAt(ObjectIndex);
					continue;
				}
			}
		}

		foreach (var GamePlayer in GamePlayers)
		{
			GamePlayer.Gold += 25;
		}

		WaitingConnections.Clear();
		++Turn;
		Broadcast_OnNextTurn();
	}

	/////////////////////////////////////////////////////////////////////////////////////////

	void INetworkListener.OnActive(Connection ConnectionChannel)
	{
		Assert.True(Networking.IsHost);
		Log.Info($"Connection activating with name = {ConnectionChannel.DisplayName}:{ConnectionChannel.Ping} | is host = {ConnectionChannel.IsHost}");

		StartClient_SeverOnly(ConnectionChannel.Id);
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

	void ISceneStartup.OnHostInitialize()
	{
		Assert.IsValid(HexPrefab);
		Assert.IsValid(PlayerPrefab);

		Transform HexTransform = new();
		CloneConfig HexConfig = new(HexTransform);
		var Hex = HexPrefab.Clone(HexConfig);
		Hex.Network.SetOrphanedMode(NetworkOrphaned.Host);
		Hex.NetworkSpawn(Connection.Host);

		_ = GenerateBoardAsync(Hex);
		Log.Info($"created board with {BoardHexes.Count} hexes");

		if (!Networking.IsActive)
		{
			CreateLobby();
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
		await CreateSurroundAsync(MainHex, 8);
	}

	private async Task CreateSurroundAsync(Hex Hex, int Depth)
	{
		if (!Hex.IsValid())
		{
			return;
		}

		if (!BoardHexes.Contains(Hex))
		{ 
			BoardHexes.Add(Hex);
		}

		if (Depth <= 0)
		{
			return;
		}

		Hex.CreateSurroundBrothers();

		foreach (var Brother in Hex.AllBrothers)
		{
			await CreateSurroundAsync(Brother, Depth - 1);
		}
	}

	private void StartClient_SeverOnly(Guid ConnectionGuid)
	{
		Assert.True(Networking.IsHost);

		bool CreatedPlayerState = CreatePlayerObject_ServerOnly(ConnectionGuid, out var GamePlayer);

		if (!CreatedPlayerState || GamePlayer == null)
		{
			Networking.Disconnect();
			throw new Exception($"Something went wrong when trying to create PlayerState for ");
		}

		GamePlayers.Add(GamePlayer);

		var SpawnHex = Random.Shared.FromList(BoardHexes.ToList());
		if (!SpawnHex.IsValid())
		{
			Log.Warning("ohno");
			return;
		}

		Server_CreateHexUnitObject("unit-settler", SpawnHex, ConnectionGuid);
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

		DefenderUnit.Hex.UnitData = DefenderUnit.Hex.UnitData with
		{
			Health = DefenderUnit.Hex.UnitData.Health - AttackerUnit.Attack
		};
	}

	[Rpc.Host]
	public void Server_QueueHexObject(string ObjectId, Hex Hex)
	{
		Assert.IsValid(Hex);

		var HexObject = ObjectPrefabs[ObjectId];
		Assert.NotNull(HexObject);

		FQueueObject QueueObject = new()
		{
			GameObjectId = Guid.NewGuid(),
			ObjectId = ObjectId,
			ObjectName = HexObject.Name,
			ProductionToBuild = 10,
		};

		Hex.AddQueuedObject_ServerOnly(QueueObject);
	}

	[Rpc.Host]
	public void Server_CreateHexUnitObject(string ObjectId, Hex Hex, Guid ConnectionId)
	{
		Assert.IsValid(Hex);
		Assert.NotNull(ConnectionId);

		if (Hex.UnitData != null)
		{
			Log.Warning($"trying to build unit on already occupied hex {Hex}! ignoring");
			return;
		}

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
			ViewRange = TypedObject.ViewRange,
			Hex = Hex,
		};

		Hex.UnitData = ObjectData;

		Log.Info($"created object {ObjectId} on {Hex} for {ConnectionId}");
	}

	[Rpc.Host]
	public void Server_CreateHexBuildingObject(GameObject HexObject, Hex Hex, bool FromUnit, Guid ConnectionId)
	{
		Assert.IsValid(Hex);
		Assert.NotNull(ConnectionId);

		if (Hex.BuildingData != null)
		{
			Log.Warning($"trying to build building on already occupied hex {Hex}! ignoring");
			return;
		}

		var TypedObject = HexObject.GetComponent<ObjectBuilding>();
		Assert.NotNull(TypedObject);

		FBuilding ObjectData = new()
		{
			ObjectId = TypedObject.ObjectId,
			Name = TypedObject.DisplayName,
			Transform = Hex.WorldTransform,
			OwnerGuid = ConnectionId,
			ProductionToBuild = TypedObject.ProductionToBuild,
			ViewRange = TypedObject.ViewRange,
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
			TurnMovementSpent = OldUnitData.TurnMovementSpent + HexesBetween,
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
		foreach (var Hex in BoardHexes)
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

	private bool CreatePlayerObject_ServerOnly(Guid ConnectionGuid, out GamePlayer OutGamePlayer)
	{
		Assert.True(Networking.IsHost);
		Assert.True(PlayerPrefab.IsValid(), "Could not spawn player as no PlayerPrefab assigned to network manager");

		Transform PlayerTransform = new()
		{
			Position = new(-500, 0, 500)
		};

		CloneConfig PlayerSpawnConfig = new(PlayerTransform);

		var ConnectionChannel = Connection.Find(ConnectionGuid);

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

	private static bool CreateLobby(string LobbyName = "awesomelobby", LobbyPrivacy Privacy = LobbyPrivacy.Private)
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
