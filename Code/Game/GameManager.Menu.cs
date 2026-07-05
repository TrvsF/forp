using Forp.Object;
using Sandbox.Diagnostics;
using System;

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

	protected void MenuLoad()
	{
		if (Mode != EGameManagerMode.Menu)
		{
			return;
		}

		SpawnRandomObject();
	}

	private int TicksSinceLastSpawn = 0;

	private void MenuTick()
	{
		if (Mode != EGameManagerMode.Menu)
		{
			return;
		}

		++TicksSinceLastSpawn;

		if (TicksSinceLastSpawn > Random.Shared.Int(100, 400))
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

		Assert.NotNull(Scene.Camera);
		UpdateMouseLook();
	}

	private const float MaxYaw = 15f;
	private const float MaxPitch = 10f;
	private const float LookSpeed = 6f;
	private readonly Rotation BaseCameraRotation = Rotation.FromYaw(90);

	void UpdateMouseLook()
	{
		Vector2 Offset = (Mouse.Position / Screen.Size - 0.5f) * 2f;

		float Yaw = -Offset.x * MaxYaw;
		float Pitch = Offset.y * MaxPitch;
		Rotation TargetRotation = BaseCameraRotation * Rotation.From(Pitch, Yaw, 0f);

		Scene.Camera.WorldRotation = Rotation.Slerp(Scene.Camera.WorldRotation, TargetRotation, MathX.Clamp(Time.Delta * LookSpeed, 0f, 1f));
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
		CloneConfig HexConfig = new()
		{
			StartEnabled = true,
		};

		var Hex = HexPrefab.Clone(HexConfig);
		Hex.Network.SetOrphanedMode(NetworkOrphaned.Host);
		Hex.NetworkSpawn(Connection.Host);

		var HexComponent = Hex.GetComponent<Hex>();
		Assert.NotNull(HexComponent);
		HexComponent.IsRevealed = true;
		HexComponent.LocalCameraMode = ECameraMode.Normal;

		MenuHexes.Add(new(HexComponent, Random.Shared.Int(6, 15), Random.Shared.Int(0, 12)));

		switch (Random.Shared.Int(0, 2))
		{
			case 0:
				Server_CreateHexUnitObject(Random.Shared.FromList(RandomUnits), HexComponent, Connection.Local.Id, true);
				HexComponent.UnitObject.SetCameraMode(ECameraMode.Normal);
				break;
			case 1:
				Server_CreateHexBuildingObject(Random.Shared.FromList(RandomBuilding), HexComponent, false, Connection.Local.Id);
				break;
			case 2:
				break;
		}

		Vector3 RandomPos = new(0, 2000, Random.Shared.Int(-200, 200));
		Rotation RandomRotation = Rotation.FromYaw(Random.Shared.Int(0, 360));

		switch (Random.Shared.Int(0, 2))
		{
			case 0:
				HexComponent.WorldPosition = RandomPos.WithX(Random.Shared.Int(300, 900));
				HexComponent.WorldRotation = RandomRotation.RotateAroundAxis(Vector3.Right, 30);
				break;
			case 1:
				HexComponent.WorldPosition = RandomPos.WithX(Random.Shared.Int(-900, -300));
				HexComponent.WorldRotation = RandomRotation.RotateAroundAxis(Vector3.Right, -30);
				break;
			case 2:
				HexComponent.WorldPosition = RandomPos.WithX(Random.Shared.Int(200, 600));
				HexComponent.WorldRotation = RandomRotation.RotateAroundAxis(Vector3.Forward, 30);
				break;
		}
	}
}