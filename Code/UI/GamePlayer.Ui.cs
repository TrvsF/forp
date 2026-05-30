using Forp.Object;
using Forp.Object.Building;
using Forp.Object.Unit;
using Sandbox;
using Sandbox.Diagnostics;
using System;
using static Sandbox.Material;

namespace Forp.Game;

public struct FPlayerUiInfo
{
	public FPlayerUiInfo() { }

	public string Name = "NONE";
	public int Gold = 0;
	public int Territory = 0;
	public int Production = 0;

	public Object.Obj HoveredObject = null;
	public ObjectUnit SelectedUnit = null;
	public Hex SelectedHex = null;
	public List<string> BuildObjects = new();

	public readonly List<string> GetTopBarStrings()
	{
		GameManager.Instance.GetPlayerBoardStats(out var BoardStats);
		return BoardStats.Select(Player => $"{Player.Key.SteamName} \u00A3{Player.Key.Gold} {Player.Value.Production}⬡ {Player.Value.TerritoryPercentage}%").ToList();
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
	public void GetPlayerUiInfo(out FPlayerUiInfo OutPlayerUiInfo)
	{
		OutPlayerUiInfo = new();
		OutPlayerUiInfo.Name = Local?.SteamName;
		OutPlayerUiInfo.Gold = Gold;

		GameManager.Instance.GetPlayerBoardStats(out var PlayerBoardStats);
		if (PlayerBoardStats.TryGetValue(Local, out var PlayerStats))
		{
			OutPlayerUiInfo.Territory = PlayerStats.TerritoryPercentage;
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
					OutPlayerUiInfo.BuildObjects.Add($"{BuildObjectUnit.ObjectId} £{BuildObjectUnit.GoldToBuild}");
				}
			}
		}
	}

	private void RefreshGUi()
	{
		if (GUi == null)
		{
			return; // :(
		}

		var TopTextRenderer = GUi.TopText.GetComponent<TextRenderer>();
		Assert.NotNull(TopTextRenderer);
		TopTextRenderer.Text = "";

		var UpgradeModel = GUi.UpgradeGUi.GetComponent<SkinnedModelRenderer>();
		Assert.NotNull(UpgradeModel);
		UpgradeModel.Tint = Colour;

		var AvatarDresser = GUi.Avatar.GetComponent<Dresser>();
		Assert.NotNull(AvatarDresser);
		var AvatarModel = GUi.Avatar.GetComponent<SkinnedModelRenderer>();
		Assert.NotNull(AvatarModel);

		if (HoveredObject is ObjectUnit HoveredUnit)
		{
			AvatarModel.Tint = HoveredUnit.ModelRenderer.Tint;
			AvatarDresser.Clothing = HoveredUnit.GetComponent<Dresser>().Clothing;
			AvatarDresser.Source = Dresser.ClothingSource.Manual;
			AvatarDresser.Apply();
		}
		else
		{
			AvatarModel.Tint = Color.White;
			AvatarDresser.Source = Dresser.ClothingSource.LocalUser;
			AvatarDresser.Apply();
		}

		SetBottomText();
	}

	private void SetBottomText()
	{
		var BottomTextString = string.Empty;

		if (HoveredObject is ObjectUnit ObjectUnit)
		{
			var UnitPrefix = ObjectUnit.IsAi ? "Native" : $"{ObjectUnit.OwnerPlayer.SteamName}'s";
			BottomTextString = $"{UnitPrefix} {ObjectUnit.DisplayName} {ObjectUnit.Health}hp\n{ObjectUnit.Tooltip}";

			if (ObjectUnit.OwnerPlayer?.ConnectionId == Local.ConnectionId)
			{
				BottomTextString += $"{ObjectUnit.Attack} Attack";
			}
		}
		else if (HoveredObject is ObjectBuilding ObjectBuilding)
		{
			BottomTextString = $"{ObjectBuilding.DisplayName} {ObjectBuilding.Health}hp\n{ObjectBuilding.Tooltip}";
		}

		var BottomTextRenderer = GUi.BottomText.GetComponent<TextRenderer>();
		Assert.NotNull(BottomTextRenderer);
		BottomTextRenderer.Text = $"{BottomTextString}";
	}
}

public sealed class GamePlayerGUi : Component
{
	[Property] public GameObject UpgradeGUi { get; private set; }
	[Property] public GameObject TopText { get; private set; }
	[Property] public GameObject BottomText { get; private set; }
	[Property] public GameObject Avatar { get; private set; }

	private bool ShowUpgrades = false;
	private List<GameObject> ShownUpgrades = new();
	public void OnUpgradeClicked(GamePlayer UpgradePlayer)
	{
		ShowUpgrades = !ShowUpgrades;
		if (!ShowUpgrades)
		{
			foreach (var ShownUpgrade in ShownUpgrades)
			{
				ShownUpgrade.Destroy();
			}
			ShownUpgrades.Clear();
			return;
		}
		
		if (UpgradePlayer == null)
		{
			Log.Warning("upgrade pressed by no-one (spooky)");
			return;
		}

		float XOffset = 50f;
		foreach (var Upgrade in UpgradePlayer.Upgrades)
		{
			var UpgradeObject = GameManager.Instance.GetObject(Upgrade.ObjectId);

			CloneConfig CloneConfig = new()
			{
				Parent = UpgradeGUi,
				StartEnabled = true,
			};
			
			var ShownUpgrade = UpgradeObject.Clone(CloneConfig);
			ShownUpgrade.LocalScale = Vector3.One;
			ShownUpgrade.LocalPosition += new Vector3(0f, XOffset, 0f);
			ShownUpgrades.Add(ShownUpgrade);

			XOffset += 100f;
		}
	}
}