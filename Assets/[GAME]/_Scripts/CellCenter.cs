using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CellCenter : MapPoint
{
	public List<CellCenter> neighborCells = new List<CellCenter>();
	public List<MapEdge> borderEdges = new List<MapEdge>();
	public List<CellCorner> cellCorners = new List<CellCorner>();
}
