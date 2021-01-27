using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MapPoint
{
    public int index;
    public Vector2 position;
	public bool isBorder;
	public bool isWater;
	public bool isOcean;
	public bool isCoast;
	public float elevation; //Normalized
	public float moisture; //Normalized
}
