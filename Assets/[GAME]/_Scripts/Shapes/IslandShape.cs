using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Custom/Map Shape/Square")]
public class IslandShape : ScriptableObject
{
    public virtual bool IsPointInsideShape(Vector2 point, Vector2 mapSize, int seed = 0)
	{
		Random.InitState(seed);
		return true;
	}
}
