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

	[RequireComponent] public SkinnedModelRenderer ModelRenderer { get; set; }
	[RequireComponent] public ModelCollider ModelCollider { get; set; }

	protected override void OnStart()
	{
		base.OnStart();
	}

	[Rpc.Broadcast]
	public void OnDamageTaken(float Damage)
	{
		var TextTransform = WorldTransform;
		TextTransform.Position += Vector3.Up * 150;
		GameText.CreateTextObject<DamageText>(TextTransform, $"-{Damage}");

		OnDamageTaken_Internal();
	}

	protected virtual void OnDamageTaken_Internal()
	{ }

	public virtual void OnClick() { }
}
