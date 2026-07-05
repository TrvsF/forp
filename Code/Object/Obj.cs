using Forp.Game;
using Sandbox;
using Sandbox.Diagnostics;
using System;
using static Sandbox.VideoWriter;

namespace Forp.Object;

public interface IObj
{
	public string ObjectId { get; set; }

	public string Name { get; set; }
	public Transform Transform { get; set; }
	public Guid OwnerGuid { get; set; }
	public int TurnsAlive { get; set; }

	public int Health { get; set; }
	public int Attack { get; set; }

	public Hex Hex { get; set; }
}

public record FObj : IObj
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
}

public class Obj : Component
{
	[Property] public string DisplayName { get; set; }
	[Property] public string Tooltip { get; set; }
	[Property] public string ObjectId { get; set; }
	[Property] public int Health { get; set; }
	[Property] public int Attack { get; set; }

	[RequireComponent] public SkinnedModelRenderer ModelRenderer { get; set; }
	[RequireComponent] public ModelCollider ModelCollider { get; set; }

	public GamePlayer OwnerPlayer { get; set; }
	public Hex OwnerHex { get; set; }

	protected override void OnStart()
	{
		base.OnStart();
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
	protected void ToggleHealth(bool Show)
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

	[Rpc.Broadcast]
	public void OnDamageTaken(float Damage)
	{
		var TextTransform = WorldTransform;
		TextTransform.Position += Vector3.Up * 150;
		GameText.CreateTextObject<DamageText>(TextTransform, $"-{Damage}");

		OnDamageTaken_Internal();
	}

	protected virtual void OnDamageTaken_Internal() { }
	public virtual void OnClick() { }
}
