using Forp.Util;
using System;
using System.Collections.Generic;
using System.Text;

namespace Forp.Game;

public sealed partial class GameText : Component
{
	[RequireComponent] TextRenderer TextRenderer { get; set; }

	protected override void OnStart()
	{
		base.OnStart();

		TextRenderer.Scale = 1;
	}
}
