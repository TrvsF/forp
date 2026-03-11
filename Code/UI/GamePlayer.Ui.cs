using Forp.Object;
using Forp.Object.Building;
using Forp.Object.Unit;
using Sandbox;
using Sandbox.Diagnostics;
using System;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Threading;
using System.Xml.Linq;

namespace Forp.Game;

public struct FPlayerUiInfo
{
	public FPlayerUiInfo() { }

	public string Name = "NONE";
	public int Gold = 0;
	public int Xperiance = 0;
	public int Territory = 0;
	public int Production = 0;

	public Object.Obj HoveredObject = null;
	public ObjectUnit SelectedUnit = null;
	public Hex SelectedHex = null;
	public List<string> BuildObjects = new();

	public readonly string GetTopBarString()
	{
		return $"{Name} \u00A3{Gold} \u00A5{Production} {Xperiance}xp {Territory}%";
	}

	public readonly string GetBottomBarString()
	{
		var BottomString = String.Empty;

		if (SelectedHex.IsValid() && SelectedHex.IsRevealed)
		{
			var OwnerName = "None";
			var IsLocallyOwner = false;

			if (GameManager.Instance.GetGamePlayer(SelectedHex.GetOwnerId()) is { } Owner)
			{
				OwnerName = Owner.SteamName;
				IsLocallyOwner = SelectedHex.IsLocallyOwned();
			}

			BottomString += $"type {SelectedHex.Type} with {SelectedHex.Production} resources owned by {OwnerName}\n";

			if (SelectedHex.UnitObject.IsValid())
			{
				bool IsAi = SelectedHex.UnitObject.GetComponent<AiUnit>() != null;
				BottomString += $"occupied by {SelectedHex.UnitObject.DisplayName} ai={IsAi}\n";
			}

			if (SelectedHex.BuildingObject.IsValid())
			{
				BottomString += $"with building {SelectedHex.BuildingObject.DisplayName} {IsLocallyOwner}\n";
			}
		}
		else if (SelectedUnit.IsValid())
		{
			BottomString += $"unit {SelectedUnit.DisplayName} {SelectedUnit.Attack}A {SelectedUnit.Health}HP\n";

			if (HoveredObject is ObjectUnit { } HoveredUnit && HoveredUnit != SelectedUnit)
			{
				BottomString += $"attack {HoveredUnit.DisplayName}";
			}
		}
		else if (HoveredObject.IsValid())
		{
			BottomString += $"{HoveredObject.DisplayName}\n";
		}

		return BottomString;
	}

	public readonly bool AnythingSelected()
	{
		return SelectedUnit != null || SelectedHex != null;
	}
}

public sealed partial class GamePlayer : Component
{
	[Property] GameObject UpgradeIcon { get; set; }
	[Property] GameObject UpgradeRowStart { get; set; }
	[Property] List<GameObject> Upgrades { get; set; } = new();

	const float GameObjectPadding = 66;

	public void DisplayUpgradeIcon(bool Show)
	{
		var Ray = Camera.ScreenPixelToRay(Camera.ScreenRect.BottomLeft + new Vector2(GameObjectPadding, -GameObjectPadding));
		UpgradeIcon.WorldPosition = Ray.Position + Ray.Forward * GameObjectPadding;
	}

	bool ShowUpgrades = false;
	readonly List<GameObject> ShownUpgrades = new();
	public void OnUpgradeIconClick()
	{
		ShowUpgrades = !ShowUpgrades;

		foreach (var ShownUpgrade in ShownUpgrades)
		{
			ShownUpgrade.Destroy();
		}
		ShownUpgrades.Clear();

		if (!ShowUpgrades)
		{
			return;
		}

		var LeftPadding = 0;
		foreach (var Upgrade in Upgrades)
		{
			Transform SpawnTransform = new();
			SpawnTransform.Position += Camera.WorldRotation.Right * LeftPadding;
			ShownUpgrades.Add(SpawnObject(Upgrade, SpawnTransform, Connection.Local, UpgradeRowStart));

			LeftPadding += 8;
		}
	}

	public void GetPlayerUiInfo(out FPlayerUiInfo OutPlayerUiInfo)
	{
		OutPlayerUiInfo = new();
		OutPlayerUiInfo.Name = Local?.SteamName;
		OutPlayerUiInfo.Gold = Gold;
		OutPlayerUiInfo.Xperiance = Xperiance;

		GameManager.Instance.GetPlayerBoardStats(out var PlayerBoardStats);
		if (PlayerBoardStats.TryGetValue(Local, out var PlayerStats))
		{
			OutPlayerUiInfo.Territory = (int)(((float)PlayerStats.TerritoryCount / GameManager.Instance.BoardHexes.Count) * 100f);
			OutPlayerUiInfo.Production = PlayerStats.Production;
		}

		OutPlayerUiInfo.SelectedHex = SelectedHex;
		OutPlayerUiInfo.SelectedUnit = SelectedUnit;
		OutPlayerUiInfo.HoveredObject = HoveredObject;

		// if we've got a valid hex what can we build
		if (SelectedHex.IsValid() && SelectedHex.BuildingObject.IsValid())
		{
			foreach (var BuildUnit in SelectedHex.BuildingObject.Units)
			{
				if (BuildUnit.GetComponent<ObjectUnit>() is { } BuildObjectUnit)
				{
					OutPlayerUiInfo.BuildObjects.Add(BuildObjectUnit.ObjectId);
				}
			}
		}
	}
}
