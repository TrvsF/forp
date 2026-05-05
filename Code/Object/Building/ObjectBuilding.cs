using Forp.Game;
using Forp.Object.Unit;
using Sandbox;
using System;
using static Sandbox.VideoWriter;

namespace Forp.Object.Building;

public record FBuilding : IObj
{
	public string ObjectId { get; set; }

	public string Name { get; set; }
	public Transform Transform { get; set; }
	public Guid OwnerGuid { get; set; }
	public int TurnsAlive { get; set; }
	public int ViewRange { get; set; }

	public int Health { get; set; }
	public int Attack { get; set; }

	public Hex Hex { get; set; }

	public FUpgrade Upgrade { get; set; }
	public int ProductionToBuild { get; set; } // TODO : remove me
}

public class ObjectBuilding : Obj
{
	[Property] public List<GameObject> Units { get; set; }
	[Property] public int ProductionToBuild { get; set; }
	[Property] public int ViewRange { get; set; }
	[Property] public int Health { get; set; }
	[Property] public int Attack { get; set; }

	public GamePlayer OwnerPlayer { get; set; }
	public Hex OwnerHex { get; set; }

	protected virtual void OnBuildDone()
	{

	}

	protected override void OnStart()
	{
		base.OnStart();

		ShowHealth = true;
	}

	protected override void OnDestroy()
	{
		ShowHealth = false;

		base.OnDestroy();
	}

	private bool _showHealth = false;
	public bool ShowHealth
	{
		get => _showHealth;
		set
		{
			_showHealth = value;
			ToggleHealth(_showHealth);
		}
	}

	private GameText HealthText = null;
	private void ToggleHealth(bool Show)
	{
		if (HealthText.IsValid())
		{
			HealthText.DestroyGameObject();
			HealthText = null;
		}

		if (!Show)
		{
			return;
		}

		var TextTransform = WorldTransform;
		TextTransform.Position += Vector3.Up * 400;

		var OwnerString = OwnerPlayer == null ? "AI" : OwnerPlayer.SteamName;
		HealthText = GameText.CreateTextObject<GameText>(TextTransform, $"{DisplayName} : {OwnerString}\n{Health}hp");
		HealthText.WorldRotation = new();
	}
}
