using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using csDelaunay; //The Voronoi Library

public class PolygonMap : MonoBehaviour
{
	public bool useSeed;
	public int seed; //The number of polygons/sites we want
	public int polygonCount = 200;
    public Vector2 size;
    public int relaxation = 0;
    public float noiseSize = 1;

	//Graphs. Here are the info necessary to build both the Voronoi Graph than the Delaunay triangulation Graph
	private List<CellCenter> delaunayCenters = new List<CellCenter>();
	private List<CellCorner> voronoiCorners = new List<CellCorner>();
	private List<MapEdge> mapEdges = new List<MapEdge>();

	private const float LAKE_THRESHOLD = 0.3f; //0 to 1. Percentage of how many corners must be water for a cell center to be water too

	[Header("Debug")]
	public bool showVoronoi;
	public bool showDelaunay;
	public int neighboorID = -1;
	public bool showBorder;
	public bool showWater;

	private void Start()
	{
		Generate();
	}

	#region Generation
	[Button]
	public void Generate()
	{
		//Clear all map info
		ResetMapInfo();

		//Set the seed for the random system
		if (useSeed)
		{
			Random.InitState(seed);
		}

		List<Vector2> points = GenerateRandomPoints();
		GenerateGraphs(points);
		AssignElevation();
		//AssignOceanCoastAndLand();
	}

	private void ResetMapInfo()
	{
		voronoiCorners.Clear();
		delaunayCenters.Clear();
		mapEdges.Clear();
	}

	private List<Vector2> GenerateRandomPoints()
	{
		//Generate random points
		List<Vector2> points = new List<Vector2>();

		for (int i = 0; i < polygonCount; i++)
		{
			Vector2 p = new Vector2() {
				x = Random.Range(0, size.x),
				y = Random.Range(0, size.y)
			};

			points.Add(p);
		}

		return points;
	}

	private void GenerateGraphs(List<Vector2> points)
	{
		//Generate the Voronoi
		Rectf bounds = new Rectf(0, 0, size.x, size.y);
		Voronoi voronoi = new Voronoi(points, bounds, relaxation);

		//Cell centers
		Dictionary<Vector2, CellCenter> centersLookup = new Dictionary<Vector2, CellCenter>(); //Quick way to access a CellCenter through its position
		foreach (var site in voronoi.SitesIndexedByLocation)
		{
			CellCenter c = new CellCenter();
			c.index = delaunayCenters.Count;
			c.position = site.Key;

			delaunayCenters.Add(c);
			centersLookup.Add(c.position, c);
		}

		//Cell Corners
		Dictionary<Vector2, CellCorner> cornersLookup = new Dictionary<Vector2, CellCorner>(); //Quick way to access a Cell Corner thorugh its position
		foreach (var edge in voronoi.Edges)
		{
			//If the edge doesn't have clipped ends, it was not withing bounds
			if (edge.ClippedEnds == null)
				continue;

			if (!cornersLookup.ContainsKey(edge.ClippedEnds[LR.LEFT]))
			{
				CellCorner c1 = new CellCorner();
				c1.position = edge.ClippedEnds[LR.LEFT];

				cornersLookup.Add(c1.position, c1);
			}

			if (!cornersLookup.ContainsKey(edge.ClippedEnds[LR.RIGHT]))
			{
				CellCorner c2 = new CellCorner();
				c2.position = edge.ClippedEnds[LR.RIGHT];

				cornersLookup.Add(c2.position, c2);
			}
		}

		foreach (var corner in cornersLookup)
		{
			CellCorner c = new CellCorner();
			c.index = voronoiCorners.Count;
			c.position = corner.Key;
			c.isBorder = c.position.x == 0 || c.position.x == size.x || c.position.y == 0 || c.position.y == size.y;

			voronoiCorners.Add(c);
		}

		//Define a local helper function to help with the loop below
		void AddPointToPointList<T>(List<T> list, T point) where T : MapPoint
		{
			if (!list.Contains(point))
				list.Add(point);
		}

		//Voronoi and Delaunay edges. Each edge point to two cells and two corners, so we can store both the sites and corners into a single edge object (thus making two edges into one object)
		foreach (var voronoiEdge in voronoi.Edges)
		{
			if (voronoiEdge.ClippedEnds == null)
				continue;

			MapEdge edge = new MapEdge();
			edge.index = mapEdges.Count;

			//Set the voronoi edge
			edge.v0 = cornersLookup[voronoiEdge.ClippedEnds[LR.LEFT]];
			edge.v1 = cornersLookup[voronoiEdge.ClippedEnds[LR.RIGHT]];

			//Set the Delaunay edge
			edge.d0 = centersLookup[voronoiEdge.LeftSite.Coord];
			edge.d1 = centersLookup[voronoiEdge.RightSite.Coord];

			mapEdges.Add(edge);

			/*Set the relationships*/

			//Set the relationship between this edge and the connected cells centers/corners
			edge.d0.borderEdges.Add(edge);
			edge.d1.borderEdges.Add(edge);
			edge.v0.connectedEdges.Add(edge);
			edge.v1.connectedEdges.Add(edge);

			//Set the relationship between the CELL CENTERS connected to this edge
			AddPointToPointList(edge.d0.neighborCells, edge.d1);
			AddPointToPointList(edge.d1.neighborCells, edge.d0);

			//Set the relationship between the CORNERS connected to this edge
			AddPointToPointList(edge.v0.neighboorCorners, edge.v1);
			AddPointToPointList(edge.v1.neighboorCorners, edge.v0);

			//Set the relationship of the CORNERS connected to this edge and the CELL CENTERS connected to this edge
			AddPointToPointList(edge.d0.cellCorners, edge.v0);
			AddPointToPointList(edge.d0.cellCorners, edge.v1);

			AddPointToPointList(edge.d1.cellCorners, edge.v0);
			AddPointToPointList(edge.d1.cellCorners, edge.v1);

			//Same as above, but the other way around
			AddPointToPointList(edge.v0.touchingCells, edge.d0);
			AddPointToPointList(edge.v0.touchingCells, edge.d1);

			AddPointToPointList(edge.v1.touchingCells, edge.d0);
			AddPointToPointList(edge.v1.touchingCells, edge.d1);
		}
	}

	private void AssignElevation()
	{
		//Define a local helper function to determine if a corner is a land or not
		bool IsLand(Vector2 position)
		{
			//We use a perlin noise to determine the shape of the island, but anything can be used
			float perlinSeed = useSeed ? seed : Random.Range(0, 100000f);

			//Normalize the position to a -1 to 1 value
			Vector2 normalizedPosition = new Vector2() {
				x = position.x / size.x,
				y = position.y / size.y
			};

			float value = Mathf.PerlinNoise(normalizedPosition.x * noiseSize + perlinSeed, normalizedPosition.y * noiseSize + perlinSeed); //Unity's perlin noise function isn't random, so we need to add a "seed" to offset the values
			return value > (0.3f + 0.3f * position.magnitude * position.magnitude);
		}

		//Define is a corner is land or not based on some shape function (defined above)
		foreach (var corner in voronoiCorners)
		{
			corner.isWater = !IsLand(corner.position);
		}

		//TODO uncomment this when doind elevations
		//Queue<CellCorner> queue = new Queue<CellCorner>();
		//foreach (var corner in voronoiCorners)
		//{
		//	if (corner.isBorder)
		//	{
		//		corner.elevation = 0;
		//		queue.Enqueue(corner);
		//	}
		//	else
		//	{
		//		corner.elevation = Mathf.Infinity;
		//	}
		//}
	}

	private void AssignOceanCoastAndLand()
	{
		Queue<CellCenter> queue = new Queue<CellCenter>();
		int numWater = 0;

		//Set the cell to water/ocean/border
		foreach (var center in delaunayCenters)
		{
			numWater = 0;

			foreach (var corner in voronoiCorners)
			{
				if (corner.isBorder)
				{
					center.isBorder = true;
					center.isOcean = true;
					corner.isWater = true;
					queue.Enqueue(center);
				}

				if (corner.isWater)
				{
					numWater += 1;
				}
			}

			center.isWater = center.isOcean || numWater >= center.cellCorners.Count * LAKE_THRESHOLD;
		}

		//Every cell around a border must be a ocean too
		while (queue.Count > 0)
		{
			CellCenter c = queue.Dequeue();

			foreach (var n in c.neighborCells)
			{
				if(n.isWater && !n.isOcean)
				{
					n.isOcean = true;
					queue.Enqueue(n);
				}
			}
		}

		//Set the Coast cells based on the neighboors. If a cell has at least one ocean and one land neighboor, then the cell is a coast
		foreach (var cell in delaunayCenters)
		{
			int numOcean = 0;
			int numLand = 0;

			foreach (var n in cell.neighborCells)
			{
				numOcean += n.isOcean ? 1 : 0;
				numLand += !n.isWater ? 1 : 0;
			}

			cell.isCoast = numOcean > 0 && numLand > 0;
		}

		//Set the corners attributes based on the connected cells. If all connected cells are ocean, then the corner is ocean. If all cells are land, then the corner is land. Otherwise the corner is a coast
		foreach (var corner in voronoiCorners)
		{
			int numOcean = 0;
			int numLand = 0;

			foreach (var cell in corner.touchingCells)
			{
				numOcean += cell.isOcean ? 1 : 0;
				numLand += !cell.isWater ? 1 : 0;
			}

			corner.isOcean = numOcean == corner.touchingCells.Count;
			corner.isCoast = numOcean > 0 && numLand > 0;
			corner.isWater = corner.isBorder || numLand != corner.touchingCells.Count && !corner.isCoast;
		}
	}
	#endregion

	#region Debug
	private void OnDrawGizmos()
	{
		float pointSize = 0.01f;

		//Delaunay triangulation points
		if(showDelaunay && delaunayCenters != null)
		{
			Gizmos.color = Color.red;
			foreach (var center in delaunayCenters)
			{
				Gizmos.DrawWireSphere(center.position, pointSize);
			}
		}

		//Voronoi points
		if (showVoronoi && voronoiCorners != null)
		{
			foreach (var corner in voronoiCorners)
			{
				Gizmos.color = Color.blue;
				Gizmos.DrawWireSphere(corner.position, pointSize);
			}
		}

		//Edges
		if (mapEdges != null)
		{
			foreach (var edge in mapEdges)
			{
				if (showDelaunay)
				{
					Gizmos.color = Color.black;
					Gizmos.DrawLine(edge.d0.position, edge.d1.position);
				}

				if (showVoronoi)
				{
					Gizmos.color = Color.white;
					Gizmos.DrawLine(edge.v0.position, edge.v1.position);
				}
			}
		}

		//Neightbors visualization
		if(neighboorID >= 0 && delaunayCenters.Count > 0)
		{
			CellCenter c = delaunayCenters[neighboorID];

			if(showVoronoi || showDelaunay)
			{
				Gizmos.color = Color.green;
				Gizmos.DrawWireSphere(c.position, pointSize * 3);
			}

			if (showDelaunay)
			{
				Gizmos.color = Color.magenta;
				for (int i = 0; i < c.neighborCells.Count; i++)
				{
					Gizmos.DrawWireSphere(c.neighborCells[i].position, pointSize * 3);
				}
			}

			if (showVoronoi)
			{
				Gizmos.color = Color.yellow;
				for (int i = 0; i < c.cellCorners.Count; i++)
				{
					Gizmos.DrawWireSphere(c.cellCorners[i].position, pointSize * 2);
				}
			}

			for (int i = 0; i < c.borderEdges.Count; i++)
			{
				if (showDelaunay)
				{
					Gizmos.color = Color.gray;
					Gizmos.DrawLine(c.borderEdges[i].d0.position, c.borderEdges[i].d1.position);
				}
				
				if (showVoronoi)
				{
					Gizmos.color = Color.cyan;
					Gizmos.DrawLine(c.borderEdges[i].v0.position, c.borderEdges[i].v1.position);
				}
			}
		}

		//Borders
		if(showBorder && voronoiCorners != null)
		{
			foreach (var corner in voronoiCorners)
			{
				if (corner.isBorder)
				{
					Gizmos.color = Color.red;
					Gizmos.DrawCube(corner.position, Vector3.one * pointSize * 5);
				}
			}
		}

		//Water
		if (showWater && voronoiCorners != null && delaunayCenters != null)
		{
			Gizmos.color = Color.blue;

			foreach (var corner in voronoiCorners)
			{
				if (corner.isWater)
				{
					Gizmos.DrawSphere(corner.position, pointSize * 5);
				}
			}

			foreach (var cell in delaunayCenters)
			{
				if (cell.isWater)
				{
					Gizmos.DrawCube(cell.position, Vector3.one * pointSize * 5);
				}
			}
		}
	}

	private void OnValidate()
	{
		if (neighboorID < -1)
			neighboorID = -1;

		if (neighboorID >= polygonCount)
			neighboorID = polygonCount - 1;
	}
	#endregion
}
