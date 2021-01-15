using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CellCorner : MapPoint
{
	public CellCorner downslopeCorner; //The lowest corner next to this one, based on elevation
	public CellEdge downslopeEdge; //The edge connecting this corner to the downslope

	public List<CellCorner> neighborCorners = new List<CellCorner>();
	public List<CellEdge> connectedEdges = new List<CellEdge>();
	public List<CellCenter> touchingCells = new List<CellCenter>();
}
