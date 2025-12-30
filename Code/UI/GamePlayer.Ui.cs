using Forp.Object;
using Forp.Object.Building;
using Forp.Object.Unit;
using Sandbox;
using Sandbox.Diagnostics;
using System;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml.Linq;

namespace Forp.Game;

public struct FPlayerUiInfo
{
	public FPlayerUiInfo() { }

	public string Name = "NONE";
	public int Gold = 0;

	public Object.Object HoveredObject = null;
	public ObjectUnit SelectedUnit = null;
	public Hex SelectedHex = null;
	public List<string> BuildObjects = new();

	public string GetTopBarString()
	{
		return $"{Name} £{Gold}";
	}

	public string GetBottomBarString()
	{
		var BottomString = String.Empty;

		if (SelectedHex.IsValid())
		{
			var OwnerName = "None";
			var IsLocallyOwner = false;

			if (GameManager.Instance.GetGamePlayer(SelectedHex.GetOwnerId()) is { } Owner)
			{
				OwnerName = Owner.SteamName;
				IsLocallyOwner = SelectedHex.IsLocallyOwned();
			}

			BottomString += $"type {SelectedHex.Type} with {SelectedHex.Resources} resources owned by {OwnerName}\n";

			if (SelectedHex.UnitObject.IsValid())
			{
				BottomString += $"occupied by {SelectedHex.UnitObject.DisplayName}\n";
			}

			if (SelectedHex.BuildingObject.IsValid())
			{
				BottomString += $"with building {SelectedHex.BuildingObject.DisplayName}\n";

				if (SelectedHex.BuildingObject.Units.Count != 0 && IsLocallyOwner)
				{
					BottomString += "you can build";
					foreach (var BuildUnit in SelectedHex.BuildingObject.Units)
					{
						if (BuildUnit.GetComponent<ObjectUnit>() is { } BuildObjectUnit)
						{
							BottomString += $" {BuildObjectUnit.ObjectId}";
						}
					}
				}
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
	const string BaseOutString = "Hex ";

	public void GetPlayerUiInfo(out FPlayerUiInfo OutPlayerUiInfo)
	{
		OutPlayerUiInfo = new();
		OutPlayerUiInfo.Name = Local?.SteamName;
		OutPlayerUiInfo.Gold = Gold;
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
