using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CellCorner : MapPoint
{
	public List<CellCorner> neighboorCorners = new List<CellCorner>();
	public List<MapEdge> connectedEdges = new List<MapEdge>();
	public List<CellCenter> touchingCells = new List<CellCenter>();
}
