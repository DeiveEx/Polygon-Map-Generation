using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Custom/Map Shape/Noise")]
public class NoiseShape : IslandShape
{
	public int variation = 0;
    public float noiseSize = 1;
    [Range(1, 4)] public int octaves = 4;

	public override bool IsPointInsideShape(Vector2 point, Vector2 mapSize, int seed = 0)
	{
		base.IsPointInsideShape(point, mapSize, seed);

		float noiseSeed = Random.Range(0, 10000f) + variation * 10;

		//Normalize the position to a -1 to 1 value
		Vector2 normalizedPosition = new Vector2() {
			x = ((point.x / mapSize.x) - 0.5f) * 2,
			y = ((point.y / mapSize.y) - 0.5f) * 2
		};

		float value = BetterPerlinNoise.SamplePoint(normalizedPosition.x * noiseSize + noiseSeed, normalizedPosition.y * noiseSize + noiseSeed, octaves); //The perlin noise function isn't random, so we need to add a "seed" to offset the values

		//We check if the value of the perlin is greater than the border value
		return value > 0.3f + 0.3f * normalizedPosition.magnitude * normalizedPosition.magnitude; //I don't really understand how this formula works, I just see that 0.3f is the lowest possible value and that it makes a radial patterm since it uses the magnetude of the point
	}
}
