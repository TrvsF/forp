using Forp.Object;
using Forp.Object.Building;
using Forp.Object.Unit;
using Forp.Util;
using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.Network;
using Sandbox.Razor;
using Sandbox.UI;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using static Sandbox.Material;

namespace Forp.Game;

public enum EGameManagerMode
{
	Game,
	Menu,
}

public partial class GameManager : SingletonComponent<GameManager>, Component.INetworkListener, ISceneLoadingEvents
{
	public static readonly Guid AiGuid = new("d85b1407-351d-4694-9392-03acc5870eb1");

	[Property] public EGameManagerMode Mode { get; set; }
 	[Property] public GameObject HexPrefab { get; set; }
	[Property] public GameObject PlayerPrefab { get; set; }
	[Property] public GameObject GameTextPrefab { get; set; }
	[Property] public GameObject DamageTextPrefab { get; set; }
	[Property] public int NumAis { get; set; }
	[Property] private HashSet<GameObject> ObjectPrefabs { get; set; } // TODO : turn into dict at startup

	public GameObject GetTextPrefab<T>() where T : GameText
	{
		if (typeof(T) == typeof(GameText))
		{
			return GameTextPrefab;
		}

		if (typeof(T) == typeof(DamageText))
		{
			return DamageTextPrefab;
		}

		throw new Exception($"cannot find text object for {typeof(T)}");
	}

	public GameObject GetObject(string ObjectId)
	{
		GameObject Object = null;
		foreach (var ObjectPrefab in ObjectPrefabs)
		{
			if (ObjectPrefab.GetComponent<Obj>().ObjectId == ObjectId)
			{
				Object = ObjectPrefab;
				break;
			}
		}

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
		foreach (var Player in GamePlayers)
		{
			OutPlayerBoardStats.Add(Player, new());
		}

		foreach (var Hex in BoardHexes)
		{
			var FoundPlayer = GetGamePlayer(Hex.GetOwnerId());

			if (FoundPlayer == null)
			{
				continue;
			}

			OutPlayerBoardStats[FoundPlayer].Production += Hex.Production;
			OutPlayerBoardStats[FoundPlayer].TerritoryCount += 1;
		}
	}


	/////////////////////////////////////////////////////////////////////////////////////////

	[Sync(SyncFlags.FromHost)] private NetList<Guid> WaitingConnections { get; set; } = new();

	public void NextTurn()
	{
		Server_OnNextTurnRequest(Connection.Local.Id);
	}

	public void GetWaitingConnections(out int Waiting, out int Total)
	{
		Waiting = WaitingConnections.Count + NumAis;
		Total = GamePlayers.Count;
	}

	public bool WaitingForLocal()
	{
		return !WaitingConnections.Contains(Connection.Local.Id) && WaitingConnections.Count == GamePlayers.Count - 1;
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

		if (GamePlayers.Count == WaitingConnections.Count + NumAis)
		{
			DoNextTurn_ServerOnly();
		}
	}

	public event Action OnNextTurn;

	[Rpc.Broadcast]
	private void Broadcast_OnNextTurn()
	{
		OnNextTurn?.Invoke();
	}

	class HexUnitProduction
	{
		public HashSet<Hex> Hexes { get; } = new();
		public int Production { get; set; } = 0;
		public int Units { get; set; } = 0;
	}

	private void DoNextTurn_ServerOnly()
	{
		Assert.True(Networking.IsHost);

		// tick ai ////////////////////
		foreach (var Hex in BoardHexes)
		{
			if (Hex.UnitData == null || !Hex.UnitData.IsAi)
			{
				continue;
			}

			foreach (var BrotherHex in Hex.AllBrothers)
			{
				if (BrotherHex == null || BrotherHex.UnitData == null || BrotherHex.UnitData.IsAi)
				{
					continue;
				}

				Server_UnitAttackUnit(Hex, BrotherHex, Connection.Local.Id);
			}

			if (Random.Shared.FromList(Hex.AllBrothers.ToList()) is { } RandomMoveHex)
			{
				Server_MoveUnitToHex(Hex, RandomMoveHex, Connection.Local.Id);
			}
		}

		List<HexUnitProduction> SharedProductionHexes = new();

		foreach (var Hex in BoardHexes)
		{
			if (!Hex.IsValid())
			{
				continue;
			}

			// tick our hex's objects ///////////////////////////
			if (Hex.UnitData != null)
			{
				Hex.UnitData = Hex.UnitData with
				{
					TurnsAlive = Hex.UnitData.TurnsAlive + 1,
					ActionPointsSpent = 0,
				};
			}

			if (Hex.BuildingData != null)
			{
				Hex.BuildingData = Hex.BuildingData with
				{
					TurnsAlive = Hex.BuildingData.TurnsAlive + 1,
				};
			}

			if (Hex.ObjectData != null)
			{
				Hex.ObjectData = Hex.ObjectData with
				{
					TurnsAlive = Hex.ObjectData.TurnsAlive + 1,
				};
			}
			////////////////////////////////////////////////////

			bool DoThing = true;
			foreach (var ProductionHexes in SharedProductionHexes)
			{
				if (!Hex.HasOwner() || ProductionHexes.Hexes.Contains(Hex))
				{
					DoThing = false;
					break;
				}
			}

			if (DoThing)
			{
				HexUnitProduction HexProductionSet = new();

				void AddNeighbourBrothers(Hex Hex)
				{
					if (!HexProductionSet.Hexes.Add(Hex) || !Hex.HasOwner())
					{
						return;
					}
					HexProductionSet.Production += Hex.Production;
					HexProductionSet.Units += Hex.QueuedUnits.Count;

					foreach (var Brother in Hex.AllBrothers)
					{
						if (Brother.IsValid() && Brother.GetOwnerId() == Hex.GetOwnerId())
						{
							AddNeighbourBrothers(Brother);
						}
					}
				}

				AddNeighbourBrothers(Hex);
				SharedProductionHexes.Add(HexProductionSet);
			}
		}

		foreach (var ProductionHexes in SharedProductionHexes)
		{
			if (ProductionHexes.Hexes.Count == 0 || ProductionHexes.Units == 0)
			{
				continue;
			}

			int Production = (int)Math.Ceiling((double)ProductionHexes.Production / ProductionHexes.Units);

			foreach (var Hex in ProductionHexes.Hexes)
			{
				for (var ObjectIndex = Hex.QueuedUnits.Count - 1; ObjectIndex >= 0; --ObjectIndex)
				{
					Hex.QueuedUnits[ObjectIndex] = Hex.QueuedUnits[ObjectIndex] with
					{
						Production = Hex.QueuedUnits[ObjectIndex].Production + Production
					};

					var QueuedObject = Hex.QueuedUnits[ObjectIndex];
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
								Server_CreateHexUnitObject(QueuedObject.ObjectId, HexBrother, Hex.GetOwnerId(), false);
								break;
							}
						}

						Hex.QueuedUnits.RemoveAt(ObjectIndex);
						continue;
					}
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

		if (Mode == EGameManagerMode.Menu || BoardHexes.Count == 0)
		{
			return;
		}

		Log.Info($"Connection activating with name = {ConnectionChannel.DisplayName}:{ConnectionChannel.Ping} | is host = {ConnectionChannel.IsHost}");

		StartGameClient_SeverOnly(ConnectionChannel.Id, false);
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

	void INetworkListener.OnBecameHost(Connection PreviousHost)
	{
		Networking.Disconnect();
	}

	void ISceneLoadingEvents.AfterLoad(Scene Scene)
	{
		if (!Networking.IsHost)
		{
			return;
		}

		Assert.IsValid(HexPrefab);
		Assert.IsValid(PlayerPrefab);
		Assert.IsValid(GameTextPrefab);
		Assert.IsValid(DamageTextPrefab);

		if (Mode == EGameManagerMode.Menu)
		{
			MenuLoad();
			return;
		}

		PlayerColours = new(PlayerColours.Shuffle());

		_ = GenerateBoardAsync();
		Log.Info($"created board with {BoardHexes.Count} hexes");

		if (!Networking.IsActive)
		{
			CreateLobby();
		}

		InitAi();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if (Mode == EGameManagerMode.Menu)
		{
			MenuTick();
			return;
		}
	}

	private void InitAi()
	{
		for (int _ = 0; _ < NumAis; ++_)
		{
			StartGameClient_SeverOnly(Connection.Host.Id, true);
		}
	}

	private Hex MakeHex(Vector3 ParentPosition, int BrotherIndex)
	{
		var SpawnOffset = Object.Hex.BrotherOffsets[BrotherIndex];

		var ZOffset = Random.Shared.Next(-5, 6);
		Transform SpawnTransform = new()
		{
			Position = ParentPosition + new Vector3(SpawnOffset.x, SpawnOffset.y, ZOffset)
		};
		CloneConfig HexConfig = new(SpawnTransform);

		var Hex = HexPrefab.Clone(HexConfig);
		Hex.Network.SetOrphanedMode(NetworkOrphaned.Host);
		Hex.NetworkSpawn(Connection.Host);

		var HexComponent = Hex.GetComponent<Hex>();
		Assert.NotNull(HexComponent);
		BoardHexes.Add(HexComponent);

		return HexComponent;
	}

	private async Task GenerateBoardAsync()
	{
		// TODO : loading screen
		await CreateBoard(44, 33);
	}

	private async Task CreateBoard(int Width, int Height)
	{
		Hex CurrentHex = MakeHex(new(), 0);

		bool HAlt = false;
		for (int HeightIndex = 0; HeightIndex < Height; ++HeightIndex)
		{
			CurrentHex = MakeHex(CurrentHex.WorldPosition, HAlt ? 3 : 4);
			HAlt = !HAlt;

			var HHex = CurrentHex;
			for (int WidthIndex = 0; WidthIndex < Width; ++WidthIndex)
			{
				HHex = MakeHex(HHex.WorldPosition, 2);
			}
		}

		foreach (var Hex in BoardHexes)
		{
			for (int BrotherIndex = 0; BrotherIndex < Object.Hex.BrotherOffsets.Count; ++BrotherIndex)
			{
				var BrotherOffset = Object.Hex.BrotherOffsets[BrotherIndex];
				Vector3 From = Hex.WorldPosition + new Vector3(BrotherOffset.x, BrotherOffset.y, 100f);
				Vector3 To = Hex.WorldPosition + new Vector3(BrotherOffset.x, BrotherOffset.y, -100f);

				// Scene.DebugOverlay.Line(From, To, Color.Red, 555555);

				var Trace = Scene.Trace.Ray(From, To).Run();
				if (Trace.GameObject?.GetComponent<Hex>() is { } HitHex)
				{
					Hex.AllBrothers[BrotherIndex] = HitHex;
				}
			}
		}
	}

	private List<Hex> ValidSpawnHexes { get => BoardHexes.Where(Hex => Hex.Type == EHexType.Grass && Hex.UnitData == null).ToList(); }

	private Hex AssignStartingHex(Guid ConnectionGuid)
	{
		var GrassHexes = ValidSpawnHexes.Shuffle();
		if (GrassHexes.Count() == 0)
		{
			Log.Error("CANNOT FIND STARTING HEX, AWFUL, SHIT");
		}

		foreach (var GrassHex in GrassHexes)
		{
			// THE ISSUE HERE is there's no nice way to say 'get me all the linked hexes in a x radius'
			// in this case we'd use it to perform a check on their guid, to make sure we're not near another player
			// TODO : MAKE IT
			var IsSpawnable = true;

			foreach (var Brother in GrassHex.GetSurroundingHexesInRange(3))
			{
				if (Brother.UnitData != null)
				{
					IsSpawnable = false;
					break;
				}

				if (Brother.BuildingData != null)
				{
					IsSpawnable = false;
					break;
				}
			}

			if (IsSpawnable)
			{
				return GrassHex;
			}
		}

		return GrassHexes.First();
	}

	private void StartGameClient_SeverOnly(Guid ConnectionGuid, bool IsAi)
	{
		Assert.True(Networking.IsHost);

		var SpawnHex = AssignStartingHex(ConnectionGuid);
		if (!SpawnHex.IsValid())
		{
			Networking.Disconnect();
			throw new Exception($"cannot find a valid spawn hex for {ConnectionGuid}. This is very bad.");
		}

		bool CreatedPlayerState = CreatePlayerObject_ServerOnly(ConnectionGuid, SpawnHex, IsAi, out var GamePlayer);

		if (!CreatedPlayerState || GamePlayer == null)
		{
			Networking.Disconnect();
			throw new Exception($"Something went wrong when trying to create PlayerState for {ConnectionGuid}");
		}

		GamePlayers.Add(GamePlayer);
	}

	// TODO : figure out how to attack using IObj's

	private Hex GetAttackHex(Hex AttackerUnitHex, Hex DefenderUnitHex, out int Distance)
	{
		Hex BestHex = null;
		Distance = int.MaxValue;
		foreach (var Hex in DefenderUnitHex.AllBrothers)
		{
			if (!Hex.IsValid() || !Hex.CanWalkOn()) 
			{
				continue;
			}

			var DistanceToHex = GetHexDistance(AttackerUnitHex, Hex);
			if (DistanceToHex < Distance)
			{
				BestHex = Hex;
				Distance = DistanceToHex;
			}
		}

		return BestHex;
	}

	public bool CanAttack(Hex AttackerUnitHex, Hex DefenderUnitHex, IObj Defender, Guid ConnectionId, out Hex AttackFromHex, out int AttackHexDistance)
	{
		AttackFromHex = null;
		AttackHexDistance = int.MaxValue;

		Assert.NotNull(AttackerUnitHex);
		Assert.NotNull(DefenderUnitHex);
		Assert.NotNull(Defender);
		Assert.NotNull(ConnectionId);

		var AttackerUnit = AttackerUnitHex.UnitData;

		Assert.NotNull(AttackerUnit);

		if (AttackerUnit.OwnerGuid != ConnectionId && !AttackerUnit.IsAi)
		{
			Log.Warning($"trying to attack with unit that it not {ConnectionId}");
			return false;
		}

		if (AttackerUnit.OwnerGuid == Defender.OwnerGuid)
		{
			Log.Warning("trying to attack friendly unit");
			return false;
		}

		AttackFromHex = GetAttackHex(AttackerUnitHex, DefenderUnitHex, out AttackHexDistance);

		if (AttackFromHex == null)
		{
			Log.Warning("cannot find a hex to attack from");
			return false;
		}

		if (AttackerUnit.ActionPoints - AttackerUnit.ActionPointsSpent < AttackHexDistance + 1)
		{
			Log.Warning("trying to attack without enougth APs");
			return false;
		}

		return true;
	}

	[Rpc.Host]
	public void Server_UnitAttackUnit(Hex AttackerUnitHex, Hex DefenderUnitHex, Guid ConnectionId)
	{
		Assert.NotNull(ConnectionId);
		Assert.NotNull(AttackerUnitHex);
		Assert.NotNull(DefenderUnitHex);

		var AttackerUnit = AttackerUnitHex.UnitData;
		var DefenderUnit = DefenderUnitHex.UnitData;

		Assert.NotNull(AttackerUnit);
		Assert.NotNull(DefenderUnit);

		if (!CanAttack(AttackerUnitHex, DefenderUnitHex, DefenderUnit, ConnectionId, out var NewAttackUnitHex, out var BestDistance))
		{
			Log.Warning($"{ConnectionId} trying to call an invalid attack");
			return;
		}

		if (NewAttackUnitHex == null)
		{
			Log.Warning("invalid attacker hex");
			return;
		}

		if (BestDistance > 0)
		{
			Server_MoveUnitToHex(AttackerUnitHex, NewAttackUnitHex, ConnectionId);
			AttackerUnitHex = NewAttackUnitHex;
			AttackerUnit = AttackerUnitHex.UnitData;
		}

		AttackerUnitHex.UnitData = AttackerUnitHex.UnitData with
		{
			ActionPointsSpent = AttackerUnitHex.UnitData.ActionPointsSpent + 1
		};

		if (DefenderUnitHex.UnitObject is { } UnitObject)
		{
			UnitObject.OnDamageTaken(AttackerUnit.Attack);
		}

		DefenderUnitHex.UnitData = DefenderUnitHex.UnitData with
		{
			Health = DefenderUnit.Hex.UnitData.Health - AttackerUnit.Attack
		};

		if (DefenderUnitHex.UnitData.Health <= 0)
		{
			if (DefenderUnitHex.UnitData.IsAi)
			{
				DefenderUnitHex.UnitData.HomeHex.RemoveOwner_ServerOnly();
			}
			DefenderUnitHex.UnitData = null;
		}
	}

	[Rpc.Host]
	public void Server_UnitAttackBuilding(Hex AttackerUnitHex, Hex DefenderBuildingHex, Guid ConnectionId)
	{
		Assert.NotNull(ConnectionId);
		Assert.NotNull(AttackerUnitHex);
		Assert.NotNull(DefenderBuildingHex);

		var AttackerUnit = AttackerUnitHex.UnitData;
		var DefenderBuilding = DefenderBuildingHex.BuildingData;

		Assert.NotNull(AttackerUnit);
		Assert.NotNull(DefenderBuilding);

		if (!CanAttack(AttackerUnitHex, DefenderBuildingHex, DefenderBuilding, ConnectionId, out var NewAttackUnitHex, out var BestDistance))
		{
			Log.Warning($"{ConnectionId} trying to call an invalid attack");
			return;
		}

		if (NewAttackUnitHex == null)
		{
			Log.Warning("invalid attacker hex");
			return;
		}

		if (BestDistance > 0)
		{
			Server_MoveUnitToHex(AttackerUnitHex, NewAttackUnitHex, ConnectionId);
			AttackerUnitHex = NewAttackUnitHex;
			AttackerUnit = AttackerUnitHex.UnitData;
		}

		AttackerUnitHex.UnitData = AttackerUnitHex.UnitData with
		{
			ActionPointsSpent = AttackerUnitHex.UnitData.ActionPointsSpent + 1
		};

		if (DefenderBuildingHex.BuildingObject is { } BuildingObject)
		{
			BuildingObject.OnDamageTaken(AttackerUnit.Attack);
		}

		DefenderBuildingHex.BuildingData = DefenderBuildingHex.BuildingData with
		{
			Health = DefenderBuilding.Hex.BuildingData.Health - AttackerUnit.Attack
		};

		if (DefenderBuildingHex.BuildingData.Health <= 0)
		{
			DefenderBuildingHex.BuildingData = null;
			Hex.SetOwnerHexesRecursive_ServerOnly(DefenderBuildingHex, Guid.Empty, DefenderBuilding.ViewRange);
		}
	}

	[Rpc.Host]
	public void Server_QueueHexObject(string ObjectId, Hex Hex, int ObjectCost)
	{
		Assert.IsValid(Hex);

		var HexObject = GetObject(ObjectId);
		Assert.NotNull(HexObject);
		var ObjectUnit = HexObject.GetComponent<ObjectUnit>();
		Assert.NotNull(ObjectUnit);

		// charge the owner, if we can't afford it back out
		if (GetGamePlayer(Hex.GetOwnerId()) is { } Owner)
		{
			if (Owner.Gold < ObjectCost)
			{
				return;
			}

			Owner.Gold -= ObjectCost;
		}

		FQueueObject QueueObject = new()
		{
			GameObjectId = Guid.NewGuid(),
			ObjectId = ObjectId,
			ObjectName = HexObject.Name,
			ProductionToBuild = ObjectUnit.ProductionToBuild,
			GoldToBuild = ObjectUnit.GoldToBuild,
		};

		Hex.AddQueuedObject_ServerOnly(QueueObject);
	}

	[Rpc.Host]
	public void Server_UpgradeObject(FUpgrade UpgradeData, Hex Hex, Guid ConnectionId)
	{
		Assert.NotNull(UpgradeData);
		Assert.NotNull(ConnectionId);
		Assert.NotNull(Hex);

		if (Hex.UnitData == null)
		{
			Log.Warning("tryna upgrade NOTHIN");
			return;
		}

		if (Hex.UnitData.Upgrade != null)
		{
			Log.Warning("trying to assign upgrade to object with existing upgrade");
			return;
		}

		if (Hex.UnitData.OwnerGuid != ConnectionId)
		{
			Log.Warning($"trying to upgrade a unit that is not {ConnectionId}s");
			return;
		}

		Hex.UnitData = Hex.UnitData with
		{
			Upgrade = UpgradeData,
		};
	}

	[Rpc.Host]
	public void Server_CreateHexUnitObject(string ObjectId, Hex Hex, Guid ConnectionId, bool IsAi)
	{
		Assert.IsValid(Hex);
		Assert.NotNull(ConnectionId);
		if (Hex.UnitData != null)
		{
			Log.Warning($"trying to build unit on already occupied hex {Hex}! ignoring");
			return;
		}

		// if we're spawning an AI give it an empty guid
		if (IsAi)
		{
			ConnectionId = Guid.Empty;
		}

		var HexObject = GetObject(ObjectId);
		Assert.NotNull(HexObject);
		var TypedObject = HexObject.GetComponent<ObjectUnit>();
		Assert.NotNull(TypedObject);

		FUnit ObjectData = new()
		{
			ObjectId = TypedObject.ObjectId,
			Name = TypedObject.DisplayName,
			Transform = Hex.GetObjectSpawnLocation(),
			OwnerGuid = ConnectionId,
			Health = TypedObject.Health,
			Attack = TypedObject.Attack,
			ActionPoints = TypedObject.ActionPoints,
			ViewRange = TypedObject.ViewRange,
			Hex = Hex,
			IsAi = IsAi,
			HomeHex = Hex,
		};

		Hex.UnitData = ObjectData;

		Log.Info($"created object {ObjectId} on {Hex} for {ConnectionId}");
	}

	[Rpc.Host]
	public void Server_CreateHexBuildingObject(string BuildingId, Hex Hex, bool FromUnit, Guid ConnectionId)
	{
		var HexObject = GetObject(BuildingId);
		Assert.NotNull(HexObject);

		Server_CreateHexBuildingObject(HexObject, Hex, FromUnit, ConnectionId);
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
			Transform = Hex.GetObjectSpawnLocation(),
			OwnerGuid = ConnectionId,
			ProductionToBuild = TypedObject.ProductionToBuild,
			ViewRange = TypedObject.ViewRange,
			Health = TypedObject.Health,
			Attack = TypedObject.Attack,
			Hex = Hex,
		};

		Hex.BuildingData = ObjectData;
		Hex.SetOwnerHexesRecursive_ServerOnly(Hex, ConnectionId, TypedObject.ViewRange - 1);

		if (FromUnit) // TODO : check building charge
		{
			Hex.UnitData = null;
		}
	}

	[Rpc.Host]
	public void Server_CreateHexObject(string ObjectId, Hex Hex, Guid ConnectionId)
	{
		Assert.IsValid(Hex);
		Assert.NotNull(ConnectionId);

		if (Hex.ObjectData != null)
		{
			Log.Warning($"trying to build unit on already occupied hex {Hex}! ignoring");
			return;
		}

		var HexObject = GetObject(ObjectId);
		Assert.NotNull(HexObject);
		var TypedObject = HexObject.GetComponent<Obj>();
		Assert.NotNull(TypedObject);

		FObj ObjectData = new()
		{
			ObjectId = TypedObject.ObjectId,
			Name = TypedObject.DisplayName,
			Attack = TypedObject.Attack,
			Health = TypedObject.Health,
			Transform = Hex.GetObjectSpawnLocation(),
			OwnerGuid = ConnectionId,
			Hex = Hex,
		};

		Hex.ObjectData = ObjectData;
	}

	[Rpc.Host]
	public void Server_MoveUnitToHex(Hex OldHex, Hex NewHex, Guid ConnectionId)
	{
		Assert.IsValid(OldHex);
		Assert.IsValid(NewHex);

		if (OldHex == NewHex)
		{
			return;
		}

		if (!NewHex.CanWalkOn())
		{
			Log.Warning("Cannot move to hex that is not walkable");
			return;
		}

		if (NewHex.UnitData != null)
		{
			Log.Warning("Cannot move to hex with valid unit");
			return;
		}

		var OldUnitData = OldHex.UnitData;
		if (OldUnitData == null)
		{
			Log.Warning("MoveUnitToHex called with invalid Unit");
			return;
		}

		if (OldUnitData.OwnerGuid != ConnectionId && !OldUnitData.IsAi)
		{
			Log.Warning($"{ConnectionId} trying to make an object which is not theirs");
			return;
		}

		var HexesBetween = GetHexDistance(OldHex, NewHex);
		Log.Info($"Hex distance = {HexesBetween} turns spent = {OldUnitData.ActionPointsSpent} moverange = {OldUnitData.ActionPoints}");
		var MovementLeftover = OldUnitData.ActionPoints - (HexesBetween + OldUnitData.ActionPointsSpent);
		if (MovementLeftover < 0)
		{
			return;
		}

		FUnit NewUnitData = OldUnitData with
		{
			Transform = NewHex.GetObjectSpawnLocation(),
			Hex = NewHex,
			ActionPointsSpent = OldUnitData.ActionPointsSpent + HexesBetween,
		};

		OldHex.UnitData = null;
		NewHex.UnitData = NewUnitData;
	}

	private int GetHexDistance(Hex FromHex, Hex ToHex)
	{
		Assert.NotNull(FromHex);
		Assert.NotNull(ToHex);

		var OldHexPos = FromHex.WorldPosition.WithZ(0);
		var NewHexPos = ToHex.WorldPosition.WithZ(0);
		var HexDistance = Vector3.DistanceBetween(OldHexPos, NewHexPos);
		return (int)Math.Round(HexDistance / 200f);
	}

	// NOTE : this only works on the local player or if you're the host
	public GamePlayer GetGamePlayer(Guid ConnectionId)
	{
		foreach (var GamePlayer in GamePlayers)
		{
			if (GamePlayer.ConnectionId == ConnectionId)
			{
				return GamePlayer;
			}
		}

		// last resort, god this method is shit
		if (ConnectionId == Connection.Local.Id)
		{
			return GamePlayer.Local;
		}

		return null;
	}

	// TODO : we need a better relationship
	public Hex HACK_GetHexFromUnit(ObjectUnit Unit)
	{
		if (Unit == null)
		{
			return null;
		}

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

	public readonly Color AiColour = (Color)Color.Parse("#1D00FF");
	private Stack<Color> PlayerColours = new(
	[
		(Color)Color.Parse("#DD00FF"),
		(Color)Color.Parse("#44FF00"),
		(Color)Color.Parse("#FF1900"),
		(Color)Color.Parse("#0022FF"),
		(Color)Color.Parse("#FF3F00"),
		(Color)Color.Parse("#FF0000"),
	]);

	public Color GetTintColour(Guid Connection)
	{
		if (Connection.Equals(AiGuid))
		{
			return AiColour;
		}

		if (GetGamePlayer(Connection) is { } OwnerPlayer)
		{
			return OwnerPlayer.Colour;
		}

		return Color.White;
	}

	private bool CreatePlayerObject_ServerOnly(Guid ConnectionGuid, Hex SpawnHex, bool IsAi, out GamePlayer OutGamePlayer)
	{
		Assert.True(Networking.IsHost);
		Assert.True(PlayerPrefab.IsValid(), "Could not spawn player as no PlayerPrefab assigned to network manager");

		var PlayerPrefabComponent = PlayerPrefab.GetComponent<GamePlayer>();
		Assert.NotNull(PlayerPrefabComponent);

		var ConnectionChannel = Connection.Find(ConnectionGuid);
		Assert.NotNull(ConnectionChannel);

		var SpawnTransform = PlayerPrefab.WorldTransform;
		SpawnTransform = SpawnTransform.WithPosition(SpawnHex.WorldPosition + (PlayerPrefabComponent.PlayerCameraPrefab.WorldRotation.Backward * 1337));
		CloneConfig PlayerSpawnConfig = new()
		{
			Transform = SpawnTransform,
		};

		PlayerPrefabComponent.IsAi = IsAi;

		var PlayerObject = PlayerPrefab.Clone(PlayerSpawnConfig);
		PlayerObject.Name = $"PLAYER:{ConnectionChannel.DisplayName}";
		PlayerObject.Network.SetOrphanedMode(NetworkOrphaned.Destroy);
		PlayerObject.NetworkSpawn(ConnectionChannel);

		OutGamePlayer = PlayerObject.GetComponent<GamePlayer>();

		if (OutGamePlayer == null)
		{
			throw new Exception($"Could not spawn player as no PlayerStatePrefab assigned to network manager for {ConnectionChannel.DisplayName}");
		}

		OutGamePlayer.Colour = PlayerColours.Pop();
		OutGamePlayer.Gold = 100;

		if (!OutGamePlayer.Initilize_ServerOnly(ConnectionChannel, SpawnHex, IsAi))
		{
			OutGamePlayer.GameObject.DestroyImmediate();
			return false;
		}

		return true;
	}

	public static bool CreateLobby(string LobbyName = "awesomelobby", LobbyPrivacy Privacy = LobbyPrivacy.Private)
	{
		LobbyConfig Config = new();
		Config.Name = LobbyName;
		Config.DestroyWhenHostLeaves = true;
		Config.MaxPlayers = 5;
		Config.Privacy = Privacy;

		Networking.CreateLobby(Config);

		return true;
	}
}
