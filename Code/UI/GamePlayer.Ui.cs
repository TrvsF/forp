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

	public bool IsValid()
	{
		return Name != "NONE";
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
			OutPlayerUiInfo.OutHex = SelectedHex;
			OutPlayerUiInfo.SelectedName += $"type {SelectedHex.Type} with {SelectedHex.Resources} resources\n";

			if (SelectedHex.UnitObject.IsValid())
			{
				OutPlayerUiInfo.SelectedName += $"occupied by {SelectedHex.UnitObject.DisplayName}\n";
			}

			if (SelectedHex.BuildingObject.IsValid())
			{
				OutPlayerUiInfo.SelectedName += $"with building {SelectedHex.BuildingObject.DisplayName}\n";

				if (SelectedHex.BuildingObject.Units.Count != 0)
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
			OutPlayerUiInfo.SelectedName += $"unit {SelectedUnit.DisplayName}\n";
		}

		return OutPlayerUiInfo.SelectedName != BaseOutString;
	}
}
