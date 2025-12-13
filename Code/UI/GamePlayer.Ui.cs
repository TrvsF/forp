using Forp.Object;
using Forp.Object.Building;
using Forp.Object.Unit;
using Sandbox;
using Sandbox.Diagnostics;
using System;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Forp.Game;

public struct FPlayerUiInfo
{
	public FPlayerUiInfo() { }

	public string Name = "NONE";
	public int Gold = 0;

	public string SelectedName = "NONE";

	public List<string> Builds = new();
	public Hex OutHex = null;

	public string GetTopBarString()
	{
		return "";
	}

	public string GetBottomBarString()
	{
		return "";
	}

	public bool AnythingSelected()
	{
		return SelectedName != "NONE";
	}
}

public sealed partial class GamePlayer : Component
{
	const string BaseOutString = "Hex ";

	public bool GetPlayerUiInfo(out FPlayerUiInfo OutPlayerUiInfo)
	{
		OutPlayerUiInfo = new();
		OutPlayerUiInfo.Name = Local?.SteamName;
		OutPlayerUiInfo.Gold = Gold;
		OutPlayerUiInfo.SelectedName = BaseOutString;

		if (SelectedHex.IsValid())
		{
			var OwnerName = "None";
			var IsLocallyOwner = false;

			if (GameManager.Instance.GetGamePlayer(SelectedHex.GetOwnerId()) is { } Owner)
			{
				OwnerName = Owner.SteamName;
				IsLocallyOwner = Owner.ConnectionId == Local.ConnectionId;
			}

			OutPlayerUiInfo.OutHex = SelectedHex;
			OutPlayerUiInfo.SelectedName += $"type {SelectedHex.Type} with {SelectedHex.Resources} resources owned by {OwnerName}\n";

			if (SelectedHex.UnitObject.IsValid())
			{
				OutPlayerUiInfo.SelectedName += $"occupied by {SelectedHex.UnitObject.DisplayName}\n";
			}

			if (SelectedHex.BuildingObject.IsValid())
			{
				OutPlayerUiInfo.SelectedName += $"with building {SelectedHex.BuildingObject.DisplayName}\n";

				if (SelectedHex.BuildingObject.Units.Count != 0 && IsLocallyOwner)
				{
					OutPlayerUiInfo.SelectedName += "you can build";
					foreach (var BuildUnit in SelectedHex.BuildingObject.Units)
					{
						if (BuildUnit.GetComponent<ObjectUnit>() is { } BuildObjectUnit)
						{
							OutPlayerUiInfo.Builds.Add(BuildObjectUnit.ObjectId);
							OutPlayerUiInfo.SelectedName += $" {BuildObjectUnit.ObjectId}";
						}
					}
				}
			}
		}
		else if (SelectedUnit.IsValid())
		{
			OutPlayerUiInfo.SelectedName += $"unit {SelectedUnit.DisplayName} {SelectedUnit.Attack}A {SelectedUnit.Health}HP\n";

			if (HoveredObject is ObjectUnit { } HoveredUnit && HoveredUnit != SelectedUnit)
			{
				OutPlayerUiInfo.SelectedName += $"attack {HoveredUnit.DisplayName}";
			}
		}
		else if (HoveredObject.IsValid())
		{
			OutPlayerUiInfo.SelectedName += $"{HoveredObject.DisplayName}\n";
		}

		return OutPlayerUiInfo.SelectedName != BaseOutString;
	}
}
