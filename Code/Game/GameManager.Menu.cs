using Forp.Object;
using Forp.Object.Unit;
using Forp.Util;
using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.Network;
using Forp.Object.Building;
using Sandbox.Razor;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Forp.Game;

record MenuObject
{
	public MenuObject(Hex InHex, int InSpeed, int InRotation)
	{
		Hex = InHex;
		Speed = InSpeed;
		Rotation = InRotation;
	}

	public Hex Hex { get; set; }
	public int Speed { get; set; }
	public int Rotation { get; set; }
}

public partial class GameManager
{
	private List<MenuObject> MenuHexes = new();

	int TicksSinceLastSpawn = 0;
	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();

		if (Mode != EGameManagerMode.Menu)
		{
			return;
		}

		++TicksSinceLastSpawn;

		if (TicksSinceLastSpawn > Random.Shared.Int(200, 400))
		{
			SpawnRandomObject();
			TicksSinceLastSpawn = 0;
		}

		for (var MenuHexIndex = MenuHexes.Count - 1; MenuHexIndex >= 0; --MenuHexIndex)
		{
			var MenuHex = MenuHexes[MenuHexIndex];
			MenuHex.Hex.WorldPosition += Vector3.Right * MenuHex.Speed;
			MenuHex.Hex.WorldRotation = MenuHex.Hex.WorldRotation.RotateAroundAxis(Vector3.Up, MenuHex.Rotation);

			if (MenuHex.Hex.WorldPosition.y < -2500)
			{
				MenuHex.Hex.DestroyGameObject();
				MenuHexes.RemoveAt(MenuHexIndex);
			}
		}
	}

	List<string> RandomBuilding = new()
	{
		// "building-tower",
		"building-city",
	};

	List<string> RandomUnits = new()
	{
		"unit-settler",
		"unit-combat",
	};

	protected void SpawnRandomObject()
	{
		Vector3 RandomPos = new(Random.Shared.Int(-500, 500), 2000, Random.Shared.Int(-200, 200));

		CloneConfig HexConfig = new()
		{
			StartEnabled = true,
			Transform = WorldTransform.WithPosition(RandomPos),
		};

		var Hex = HexPrefab.Clone(HexConfig);
		Hex.Network.SetOrphanedMode(NetworkOrphaned.Host);
		Hex.NetworkSpawn(Connection.Host);

		var HexComponent = Hex.GetComponent<Hex>();
		Assert.NotNull(HexComponent);
		HexComponent.IsRevealed = true;
		HexComponent.LocalCameraMode = ECameraMode.Normal;

		MenuHexes.Add(new(HexComponent, Random.Shared.Int(3, 12), Random.Shared.Int(0, 12)));

		switch (Random.Shared.Int(0, 2))
		{
			case 0:
				Server_CreateHexUnitObject(Random.Shared.FromList(RandomUnits), HexComponent, Connection.Local.Id);
				break;
			case 1:
				Server_CreateHexBuildingObject(Random.Shared.FromList(RandomBuilding), HexComponent, false, Connection.Local.Id);
				break;
			case 2:
				break;
		}
	}

	protected void SpawnMenu()
	{
		SpawnRandomObject();
	}
}