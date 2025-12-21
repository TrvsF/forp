using Sandbox;
using Sandbox.Diagnostics;
using System;
using static Sandbox.VideoWriter;

namespace Forp.Object;

public interface IObject
{
	public string ObjectId { get; set; }

	public string Name { get; set; }
	public Transform Transform { get; set; }
	public Guid OwnerGuid { get; set; }
	public int TurnsAlive { get; set; }
	public int ViewRange { get; set; }

	public Hex Hex { get; set; }
}

public class Object : Component
{
	[Property] public string DisplayName { get; set; }
	[Property] public string ObjectId { get; set; }

	[RequireComponent] public SkinnedModelRenderer ModelRenderer { get; set; }
	[RequireComponent] public ModelCollider ModelCollider { get; set; }

	protected override void OnStart()
	{
		base.OnStart();
	}

	public virtual void OnClick() { }
}
