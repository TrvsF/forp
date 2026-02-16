using Forp.Object;
using Forp.Util;
using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Text;

namespace Forp.Game;

public sealed partial class GameText : Component
{
	[RequireComponent] TextRenderer TextRenderer { get; set; }

	public void SetText(string Text)
	{
		TextRenderer.Text = Text;
	}

	protected override void OnStart()
	{
		base.OnStart();

		TextRenderer.Scale = 0.5f;
	}

	public static GameText CreateTextObject(Transform Transform, string Text = "")
	{
		var TextPrefab = GameManager.Instance.GameTextPrefab;
		Assert.IsValid(TextPrefab);

		var ConstructionClone = GameManager.Instance.GameTextPrefab.Clone();
		ConstructionClone.WorldTransform = Transform;

		var TextObject = ConstructionClone.GetComponent<GameText>();
		TextObject.SetText($"{Text}");

		return TextObject;
	}
}
