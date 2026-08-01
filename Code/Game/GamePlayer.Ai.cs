using Forp.Object;
using Forp.Object.Building;
using Forp.Object.Unit;
using Sandbox.Diagnostics;
using System;

namespace Forp.Game;

public sealed partial class GamePlayer : Component
{
	[Property] public bool IsAi { get; set; } = false;

	public void HandleNextTurn_ServerOnly()
	{
		Assert.True(Networking.IsHost);

		// TODO : do we want a direct relationship of player -> units?
		foreach (var Hex in GameManager.Instance.BoardHexes)
		{
			if (Hex == null || Hex.UnitData == null)
			{
				continue;
			}

			if (Hex.UnitData.OwnerGuid == ConnectionId)
			{
				// TODO 
				// (yes this part works, i tested it :^)
			}
		}
	}
}
