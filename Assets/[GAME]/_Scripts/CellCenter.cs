using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CellCenter : MapPoint
{
	public Biomes biome;

	public List<CellCenter> neighborCells = new List<CellCenter>();
	public List<CellEdge> borderEdges = new List<CellEdge>();
	public List<CellCorner> cellCorners = new List<CellCorner>();
}
