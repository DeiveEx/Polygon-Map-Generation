using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class BetterPerlinNoise
{
	/// <summary>
	/// Samples a value from the generated perlin noise
	/// </summary>
	/// <param name="x">The X coordinate</param>
	/// <param name="y">The Y coordinate</param>
	/// <param name="octaves">How many octaves (kinda like layers of noise) should be used</param>
	/// <param name="persistence">How much each octave should affect noise. Formula is persistence ^ currentOctave. Value is clamped into the range [0, 1]</param>
	/// <param name="lacunarity">How much will the frequency of each octave change. Formula is lacunarity ^ currentOctave</param>
	/// <returns></returns>
	public static float SamplePoint(float x, float y, int octaves = 1, float persistence = .5f, float lacunarity = 2)
	{
		persistence = Mathf.Clamp01(persistence);

		float result = 0;
		float frequency = 1;
		float amplitude = 1;
		float sumOfAmplitudes = 0;

		for (int i = 0; i < octaves; i++)
		{
			result += Mathf.PerlinNoise(x * frequency, y * frequency) * amplitude;

			sumOfAmplitudes += amplitude;
			amplitude *= persistence;
			frequency *= lacunarity;
		}

		//Normalize the result
		return result / sumOfAmplitudes;
	}
}
