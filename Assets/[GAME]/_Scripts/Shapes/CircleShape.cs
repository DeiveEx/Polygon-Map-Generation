using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Custom/Map Shape/Circle Shape")]
public class CircleShape : IslandShape
{
	[Range(0, 1)]
	public float size = 1;

	public override bool IsPointInsideShape(Vector2 point, Vector2 mapSize, int seed = 0)
	{
		base.IsPointInsideShape(point, mapSize, seed);

		//Normalize the position to a -1 to 1 value
		Vector2 normalizedPosition = new Vector2() {
			x = ((point.x / mapSize.x) - 0.5f) * 2,
			y = ((point.y / mapSize.y) - 0.5f) * 2
		};

		float value = Vector2.Distance(Vector2.zero, normalizedPosition);

		return value < size;
	}
}
