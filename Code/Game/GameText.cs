using Forp.Object;
using Forp.Util;
using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Text;

namespace Forp.Game;

public class GameText : Component
{
	[RequireComponent] protected TextRenderer TextRenderer { get; set; }

	public void SetText(string Text)
	{
		TextRenderer.Text = Text;
	}

	protected override void OnStart()
	{
		base.OnStart();

		TextRenderer.Scale = 0.5f;
	}

	public static T CreateTextObject<T>(Transform Transform, string Text = "") where T : GameText
	{
		var TextPrefab = GameManager.Instance.GetTextPrefab<T>();
		Assert.IsValid(TextPrefab);

		var ConstructionClone = TextPrefab.Clone();
		ConstructionClone.WorldTransform = Transform;

		var TextObject = ConstructionClone.GetComponent<T>();
		TextObject.SetText($"{Text}");

		return TextObject;
	}
}

public sealed class DamageText : GameText
{
	protected override void OnStart()
	{
		base.OnStart();

		TextRenderer.Color = Color.Red;
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();

		WorldPosition += Vector3.Up * 20;
	}
}